using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibCd
{
    const byte Nop = 0x01, 
        Setloc = 0x02,
        Play = 0x03,
        ReadN = 0x06, 
        Stop = 0x08,
        Pause = 0x09,
        Init = 0x0A,
        Mute = 0x0B, 
        Demute = 0x0C, 
        Setfilter = 0x0D,
        Setmode = 0x0E,
        GetlocL = 0x10,
        GetlocP = 0x11,
        SeekL = 0x15,
        SeekP = 0x16,
        ReadS = 0x1B;

    const int Complete = 0x02;
    const int DataReady = 0x01;
    const byte ModeSize1 = 0x20, ModeSize0 = 0x10;

    const byte StatMotor = 0x02;
    static byte _status;
    static byte _mode;
    static byte _com;
    static readonly byte[] _pos = new byte[4];
    static readonly byte[] _lastResult = new byte[8];
    static int _lastIntr = Complete;

    static uint _cbSync;
    static uint _cbReady;
    static uint _cbData;

    static bool _readActive;
    static bool _xaActive;
    static byte _filterFile;
    static byte _filterChannel;

    internal static readonly object DiscLock = new();

    static readonly bool[] NeedsLoc = BuildNeedsLoc();

    static bool[] BuildNeedsLoc()
    {
        var t = new bool[32];
        t[Play] = t[ReadN] = t[SeekL] = t[SeekP] = t[ReadS] = true;
        return t;
    }

    public static void CdInit(CpuContext c, IMemory m)
    {
        CdResetState();
        c.V0 = CdInitInternal() ? 0u : 1u;
    }

    public static void CdReset(CpuContext c, IMemory m)
    {
        CdResetState();
        c.V0 = CdInitInternal() ? 1u : 0u;
    }

    public static void CdControl(CpuContext c, IMemory m) => c.V0 = (uint)(CommandWait(m, (byte)c.A0, c.A1, c.A2, 0) == 0 ? 1 : 0);
    public static void CdControlF(CpuContext c, IMemory m) => c.V0 = (uint)(CommandWait(m, (byte)c.A0, c.A1, 0, 1) == 0 ? 1 : 0);

    public static void CdControlB(CpuContext c, IMemory m)
    {
        if (CommandWait(m, (byte)c.A0, c.A1, c.A2, 0) != 0) { c.V0 = 0; return; }
        c.V0 = (uint)(SyncResult(m, c.A2) == Complete ? 1 : 0);
    }

    public static void CdSync(CpuContext c, IMemory m)
 => c.V0 = (uint)SyncResult(m, c.A1);

    public static void CdReady(CpuContext c, IMemory m)
    {
        if (c.A1 != 0) WriteResult(m, c.A1);
        c.V0 = (uint)_lastIntr;
    }

    public static void CdRead(CpuContext c, IMemory m)
    {
        int sectors = (int)c.A0;
        uint buf = c.A1;
        _mode = (byte)c.A2;
        int lba = PosToInt(_pos);
        int size = SectorSize(_mode);
        Dispatcher.LoadByLba(lba);
        Log.Sdk($"CdRead sectors={sectors} buf=0x{buf:X8} mode=0x{_mode:X2} lba={lba} size={size}");

        for (int i = 0; i < sectors; i++)
        {
            byte[] data;
            lock (DiscLock) data = Runtime.Cd!.ReadSectorData(lba + i, size);
            for (int j = 0; j < data.Length; j++)
                m.WriteU8(buf + (uint)(i * size + j), data[j]);
        }
        _lastIntr = Complete;
        c.V0 = 1;
    }

    internal static int CurrentLba => PosToInt(_pos);
    internal static double SectorsPerSecond => (_mode & 0x80) != 0 ? 150.0 : 75.0; //cd pacer

    internal static void Tick()
    {
        bool xaMode = (_mode & 0x40) != 0;

        if (_readActive && xaMode)
        {
            PumpXa();
            return;
        }

        if (!_readActive || _cbData == 0) return;
        var c = Runtime.Cpu;
        var m = Runtime.Mem;
        if (c == null || m == null) return;

        var snap = c.Snapshot();
        while (_cbData != 0)
        {
            _lastIntr = DataReady;
            if (_cbReady != 0) { c.A0 = DataReady; c.A1 = 0; Dispatcher.Call(c, m, _cbReady); }
            AdvancePos(1);
            if (_cbData != 0) { c.A0 = DataReady; c.A1 = 0; Dispatcher.Call(c, m, _cbData); }
        }
        c.Restore(snap);
    }

    static void PumpXa()
    {
        if (Runtime.Cd == null) return;
        const int MinBuffer = 2016;
        const int MaxScan = 32;
        bool useFilter = (_mode & 0x08) != 0;
        int scanned = 0;

        while (XaAudio.BufferedSamples < MinBuffer && scanned < MaxScan)
        {
            int lba = PosToInt(_pos);
            if (lba < 0) break;
            byte[] sec;
            lock (DiscLock) sec = Runtime.Cd.ReadSectorData(lba, 2336);
            AdvancePos(1);
            scanned++;
            if ((sec[2] & 0x04) == 0) continue;
            if (useFilter && (sec[0] != _filterFile || sec[1] != _filterChannel)) continue;
            XaAudio.DecodeSector(sec, 8, sec[3]);
        }
    }

    static void AdvancePos(int n)
    {
        IntToPos(PosToInt(_pos) + n, out _pos[0], out _pos[1], out _pos[2]);
    }

    public static void CdReadSync(CpuContext c, IMemory m)
    {
        if (c.A1 != 0) WriteResult(m, c.A1);
        c.V0 = 0;
    }

    public static void CdGetSector(CpuContext c, IMemory m)
    {
        uint madr = c.A0;
        int words = (int)c.A1;
        int lba = PosToInt(_pos);
        byte[] data;
        lock (DiscLock) data = Runtime.Cd!.ReadSectorData(lba);
        int bytes = Math.Min(data.Length, words * 4);
        for (int j = 0; j < bytes; j++)
            m.WriteU8(madr + (uint)j, data[j]);
        c.V0 = 1;
    }

    public static void CdDataSync(CpuContext c, IMemory m) => c.V0 = 0;

    public static void CdSearchFile(CpuContext c, IMemory m)
    {
        uint fp = c.A0;
        string name = ReadCString(m, c.A1);

        if (Runtime.Cd == null || !Runtime.Cd.Fs.Locate(name, out int lba, out uint size))
        {
            Log.Sdk($"CdSearchFile '{name}'wasnt found");
            c.V0 = 0;
            return;
        }
        Log.Sdk($"CdSearchFile '{name}' lba={lba} size={size}");

        IntToPos(lba, out byte mm, out byte ss, out byte ff);
        m.WriteU8(fp + 0, mm);
        m.WriteU8(fp + 1, ss);
        m.WriteU8(fp + 2, ff);
        m.WriteU8(fp + 3, 0);
        m.WriteU32(fp + 4, size);

        int slash = name.LastIndexOfAny(['/', '\\']);
        string basename = slash >= 0 ? name[(slash + 1)..] : name;
        
        for (int i = 0; i < 16; i++)
        {
            m.WriteU8(fp + 8 + (uint)i, i < basename.Length ? (byte)basename[i] : (byte)0);
        }

        c.V0 = fp;
    }

    public static void CdSyncCallback(CpuContext c, IMemory m) { c.V0 = _cbSync; _cbSync = c.A0; }
    public static void CdReadyCallback(CpuContext c, IMemory m) { c.V0 = _cbReady; _cbReady = c.A0; }
    public static void CdReadCallback(CpuContext c, IMemory m) { c.V0 = _cbData; _cbData = c.A0; }
    public static void CdDataCallback(CpuContext c, IMemory m) { c.V0 = _cbData; _cbData = c.A0; }

    public static void CdStatus(CpuContext c, IMemory m) => c.V0 = _status;
    public static void CdMode(CpuContext c, IMemory m) => c.V0 = _mode;
    public static void CdLastCom(CpuContext c, IMemory m) => c.V0 = _com;
    public static void CdMix(CpuContext c, IMemory m) => c.V0 = 1;

    static void CdResetState()
    {
        LibCdStream.OnStopStream();
        _status = StatMotor; //drive aways spin
        _mode = 0;
        _com = 0;
        _lastIntr = Complete;
        _cbSync = _cbReady = _cbData = 0;
        _readActive = false;
        _xaActive = false;
        _filterFile = _filterChannel = 0;
        Array.Clear(_pos);
        Array.Clear(_lastResult);
    }

    static bool CdInitInternal()
    {
        _lastIntr = Complete;
        _lastResult[0] = _status;
        return true;
    }

    static int CommandWait(IMemory m, byte com, uint param, uint result, uint arg)
    {
        if (param != 0 && com < NeedsLoc.Length && NeedsLoc[com])
            ExecCommand(m, Setloc, param, 0);
        return ExecCommand(m, com, param, result);
    }

    static int ExecCommand(IMemory m, byte com, uint param, uint result)
    {
        _com = com;
        _lastIntr = Complete;
        Log.Sdk($"Cd cmd 0x{com:X2} param=0x{param:X8} pos={_pos[0]:X2}:{_pos[1]:X2}:{_pos[2]:X2}");

        switch (com)
        {
            case Setloc:
                if (param != 0)
                    for (int i = 0; i < 4; i++) _pos[i] = m.ReadU8(param + (uint)i);
                break;
            case Setmode:
                if (param != 0) _mode = m.ReadU8(param);
                break;
            case Setfilter:
                if (param != 0) { _filterFile = m.ReadU8(param); _filterChannel = m.ReadU8(param + 1); }
                break;
            case ReadN:
                _readActive = true;
                Dispatcher.LoadByLba(PosToInt(_pos));
                break;
            case ReadS:
                _xaActive = true;
                _readActive = false;
                LibCdStream.OnReadStream(PosToInt(_pos));
                break;
            case GetlocL:
            case GetlocP:
                _lastResult[0] = _pos[0];
                _lastResult[1] = _pos[1];
                _lastResult[2] = _pos[2];
                _lastResult[3] = _mode;
                _lastResult[4] = _filterFile;
                _lastResult[5] = _filterChannel;
                _lastResult[6] = 0;
                _lastResult[7] = 0;
                if (result != 0) WriteResult(m, result);
                return 0;
            case Pause: case Stop: case Init:
                LibCdStream.OnStopStream();
                _readActive = false;
                _xaActive = false;
                break;
            case Nop: case Play: case Mute:
            case Demute: case SeekL: case SeekP:
                break;
            default:
                break;
        }

        _lastResult[0] = _status;
        for (int i = 1; i < _lastResult.Length; i++) _lastResult[i] = 0;
        if (result != 0) WriteResult(m, result);
        return 0;
    }

    static int SyncResult(IMemory m, uint result)
    {
        if (result != 0) WriteResult(m, result);
        return _lastIntr;
    }

    static void WriteResult(IMemory m, uint addr)
    {
        for (int i = 0; i < _lastResult.Length; i++)
            m.WriteU8(addr + (uint)i, _lastResult[i]);
    }

    static int SectorSize(byte mode)
    {
        if ((mode & ModeSize1) != 0) return 2340;
        if ((mode & ModeSize0) != 0) return 2328;
        return 2048;
    }
    
    static string ReadCString(IMemory m, uint addr)
    {
        var sb = new System.Text.StringBuilder();
        for (uint i = 0; i < 128; i++)
        {
            byte b = m.ReadU8(addr + i);
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    static int Bcd(byte b) => (b >> 4) * 10 + (b & 0xF);
    static byte ToBcd(int n) => (byte)(((n / 10) << 4) + (n % 10));

    static int PosToInt(byte[] p) => (Bcd(p[0]) * 60 + Bcd(p[1])) * 75 + Bcd(p[2]) - 150;

    static void IntToPos(int i, out byte mm, out byte ss, out byte ff)
    {
        i += 150;
        ff = ToBcd(i % 75);
        ss = ToBcd(i / 75 % 60);
        mm = ToBcd(i / 75 / 60);
    }
}
