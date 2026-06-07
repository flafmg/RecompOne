using RecompOne.Runtime.Context;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

//silent hill doesnt deal very well with recompiled libpad so hle it, using dualshock
public static class LibPad
{
    const byte Connected = 0x00;
    const byte Disconnected = 0xFF;
    const byte AnalogId = 0x73;
    const uint PadStateDiscon = 0;
    const uint PadStateStable = 6;

    static uint _buf1;
    static uint _buf2;
    static int _smallMotorIdx = 0;
    static int _largeMotorIdx = 1;

    public static void PadInitDirect(CpuContext c, IMemory m)
    {
        _buf1 = c.A0;
        _buf2 = c.A1;
        Log.Sdk($"PadInitDirect buf1=0x{_buf1:X8} buf2=0x{_buf2:X8}");
        c.V0 = 0;
    }

    public static void PadStartCom(CpuContext c, IMemory m) { Refresh(m); c.V0 = 0; }
    public static void PadStopCom(CpuContext c, IMemory m) { c.V0 = 0; }
    public static void PadEnableCom(CpuContext c, IMemory m) { c.V0 = 0; }

    public static void PadChkVsync(CpuContext c, IMemory m) => c.V0 = 1;

    public static void PadChkMtap(CpuContext c, IMemory m) => c.V0 = 0;

    public static void PadGetState(CpuContext c, IMemory m) => c.V0 = IsPort1(c.A0) ? PadStateStable : PadStateDiscon;

    public static void PadInfoMode(CpuContext c, IMemory m) => c.V0 = 0;
    public static void PadInfoComb(CpuContext c, IMemory m) => c.V0 = 0;

    public static void PadInfoAct(CpuContext c, IMemory m)
    {
        c.V0 = (int)c.A2 < 0 ? 2u : 1u;
    }

    public static void PadSetMainMode(CpuContext c, IMemory m) { c.V0 = 0; }

    public static void PadSetActAlign(CpuContext c, IMemory m)
    {
        uint ptr = c.A1;
        uint len = c.A2;
        if (ptr == 0 || len < 2) { c.V0 = 0; return; }
        for (int i = 0; i < (int)len && i < 6; i++)
        {
            byte v = m.ReadU8(ptr + (uint)i);
            if (v == 0x00) _smallMotorIdx = i;
            else if (v == 0x01) _largeMotorIdx = i;
        }
        c.V0 = 1;
    }

    public static void PadSetAct(CpuContext c, IMemory m)
    {
        uint ptr = c.A1;
        uint len = c.A2;
        if (ptr == 0 || len == 0) { c.V0 = 0; return; }
        byte small = _smallMotorIdx < (int)len ? m.ReadU8(ptr + (uint)_smallMotorIdx) : (byte)0;
        byte large = _largeMotorIdx < (int)len ? m.ReadU8(ptr + (uint)_largeMotorIdx) : (byte)0;
        InputManager.SetRumble(large, small);
        c.V0 = 1;
    }

    public static void Refresh(IMemory m)
    {
        if (_buf1 != 0) WritePad(m, _buf1, Controller.State, true);
        if (_buf2 != 0) WritePad(m, _buf2, 0xFFFF, false);
    }

    static bool IsPort1(uint port) => (port & 0x10u) == 0;

    static void WritePad(IMemory m, uint buf, ushort buttons, bool present)
    {
        m.WriteU8(buf + 0, present ? Connected    : Disconnected);
        m.WriteU8(buf + 1, present ? AnalogId     : Disconnected);
        m.WriteU8(buf + 2, (byte)(buttons & 0xFF));
        m.WriteU8(buf + 3, (byte)(buttons >> 8));
        m.WriteU8(buf + 4, present ? Controller.RightX : (byte)0x80);
        m.WriteU8(buf + 5, present ? Controller.RightY : (byte)0x80);
        m.WriteU8(buf + 6, present ? Controller.LeftX  : (byte)0x80);
        m.WriteU8(buf + 7, present ? Controller.LeftY  : (byte)0x80);
    }
}
