using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Bios;

public static class BiosB
{
    struct EvCB { public uint Status, Class, Spec, Mode, Func; }
    const int MaxEvents = 64;
    static readonly EvCB[] _evCBs = new EvCB[MaxEvents];
    struct TCB { public bool Used; }
    const int MaxThreads = 4;
    static readonly TCB[] _tcbs = new TCB[MaxThreads];

    static readonly uint[] _intChain = new uint[4];

    public static uint IntrEnvInInterruptAddr = 0u;

    static uint _padBuf;

    public static void DeliverEvent(uint @class, uint spec)
    {
        for (int i = 0; i < MaxEvents; i++)
            if (_evCBs[i].Status == 2u && _evCBs[i].Class == @class && _evCBs[i].Spec == spec)
                _evCBs[i].Status = 4u;
    }
    
    public static void DeliverEventIntr(CpuContext c, IMemory m, uint @class, uint spec)
    {
        for (int i = 0; i < MaxEvents; i++)
        {
            if (_evCBs[i].Status != 2u || _evCBs[i].Class != @class || _evCBs[i].Spec != spec) continue;
            if ((_evCBs[i].Mode & 0x1000u) != 0 && _evCBs[i].Func != 0u)
            {
                var snap = c.Snapshot();
                RecompOne.Runtime.Dispatch.Dispatcher.Call(c, m, _evCBs[i].Func);
                c.Restore(snap);
            }
            else
            {
                _evCBs[i].Status = 4u;
            }
        }
    }
    
    public static void CardComplete(CpuContext c, IMemory m, uint port)
    {
        var card = (port & 0x10u) != 0 ? Runtime.CardB : Runtime.CardA;
        uint spec = card.Enabled ? 0x0004u : 0x0100u;
        
        DeliverEventIntr(c, m, 0xF4000001u, spec);
        DeliverEventIntr(c, m, 0xF0000011u, spec);
    }

    static void CardRead(CpuContext c, IMemory m)
    {
        var card = (c.A0 & 0x10u) != 0 ? Runtime.CardB : Runtime.CardA;
        if (card.Enabled && c.A2 != 0u)
        {
            Span<byte> f = stackalloc byte[0x80];
            card.FrameRead((int)(c.A1 & 0x3FFu), f);
            for (uint i = 0; i < 0x80u; i++) m.WriteU8(c.A2 + i, f[(int)i]);
        }
        CardComplete(c, m, c.A0);
        c.V0 = 1u;
    }

    static void CardWrite(CpuContext c, IMemory m)
    {
        var card = (c.A0 & 0x10u) != 0 ? Runtime.CardB : Runtime.CardA;
        if (card.Enabled && c.A2 != 0u)
        {
            Span<byte> f = stackalloc byte[0x80];
            for (uint i = 0; i < 0x80u; i++) f[(int)i] = m.ReadU8(c.A2 + i);
            card.FrameWrite((int)(c.A1 & 0x3FFu), f);
        }
        CardComplete(c, m, c.A0);
        c.V0 = 1u;
    }

    public static uint GetFreeEvSlot()
    {
        for (int i = 0; i < MaxEvents; i++) if (_evCBs[i].Status == 0u) return (uint)i;
        return 0xFFFFFFFFu;
    }
    
    static void PadRead(IMemory m)
    {
        if (_padBuf == 0) return;
        ushort s = Hardware.Controller.State;
        ushort swapped = (ushort)((s >> 8) | (s << 8));
        m.WriteU32(_padBuf,     0xFFFF0000u | swapped);
        m.WriteU8(_padBuf + 4, Hardware.Controller.RightX);
        m.WriteU8(_padBuf + 5, Hardware.Controller.RightY);
        m.WriteU8(_padBuf + 6, Hardware.Controller.LeftX);
        m.WriteU8(_padBuf + 7, Hardware.Controller.LeftY);
    }

    public static void RefreshPad(IMemory m) => PadRead(m);
    public static void Dispatch(CpuContext c, IMemory m, uint fn)
    {
        Log.Bios($"B({fn:X2}) {BiosNames.B(fn)}");
        switch (fn)
        {
            case 0x00: c.V0 = 0u; break;
            case 0x01: break;
            case 0x02: c.V0 = 0u; break;
            case 0x03: c.V0 = 0u; break;
            case 0x04: break;
            case 0x05: break;
            case 0x06: break;
            case 0x07: DeliverEvent(c.A0, c.A1); break;
            case 0x08: c.V0 = OpenEvent(c.A0, c.A1, c.A2, c.A3); break;
            case 0x09: CloseEvent(c.A0); c.V0 = 1u; break;
            case 0x0A: c.V0 = WaitEvent(c.A0); break;
            case 0x0B: c.V0 = TestEvent(c.A0); break;
            case 0x0C: EnableEvent(c.A0); c.V0 = 1u; break;
            case 0x0D: DisableEvent(c.A0); c.V0 = 1u; break;
            case 0x0E: c.V0 = OpenTh(c.A0, c.A1, c.A2); break;
            case 0x0F: CloseTh(c.A0); c.V0 = 1u; break;
            case 0x10: break;
            case 0x11: break;
            case 0x12: break;
            case 0x13: break;
            case 0x14: break;
            case 0x15: _padBuf = c.A1; break;
            case 0x16: PadRead(m); break;
            case 0x17: break;
            case 0x18: IntrEnvInInterruptAddr = 0u; break;
            case 0x19: IntrEnvInInterruptAddr = c.A0 != 0u ? c.A0 - 0x36u : 0u; break;
            case 0x1A: break;
            case 0x1B: break;
            case 0x1C: break;
            case 0x1D: break;
            case 0x1E: break;
            case 0x1F: break;
            case 0x20: UnDeliverEvent(c.A0, c.A1); break;
            case 0x2B: break;
            case 0x2C: break;
            case 0x2D: break;
            case 0x2E: break;
            case 0x2F: c.V0 = 0u; break;
            case 0x30: c.V0 = 0u; break;
            case 0x31: c.V0 = 0u; break;
            case 0x32: BiosA.Dispatch(c, m, 0x00); break;
            case 0x33: BiosA.Dispatch(c, m, 0x01); break;
            case 0x34: BiosA.Dispatch(c, m, 0x02); break;
            case 0x35: BiosA.Dispatch(c, m, 0x03); break;
            case 0x36: BiosA.Dispatch(c, m, 0x04); break;
            case 0x37: BiosA.Dispatch(c, m, 0x05); break;
            case 0x38: BiosA.Dispatch(c, m, 0x06); break;
            case 0x39: c.V0 = c.A0 <= 2u ? 2u : 0u; break;
            case 0x3A: c.V0 = 0xFFFFFFFFu; break;
            case 0x3B: Console.Write((char)(c.A0 & 0xFF)); c.V0 = c.A0; break;
            case 0x3C: c.V0 = 0xFFFFFFFFu; break;
            case 0x3D: Console.Write((char)(c.A0 & 0xFF)); c.V0 = c.A0; break;
            case 0x3E: c.V0 = 0u; break;
            case 0x3F: Console.Write(Bios.ReadString(m, c.A0)); c.V0 = c.A0; break;
            case 0x40: c.V0 = 1u; break;
            case 0x41: c.V0 = BiosA.CardFormat(m, c.A0); break;
            case 0x42: c.V0 = BiosA.FirstFile(m, c.A0, c.A1); break;
            case 0x43: c.V0 = BiosA.NextFile(m, c.A0); break;
            case 0x44: c.V0 = 0u; break;
            case 0x45: c.V0 = BiosA.CardDelete(m, c.A0); break;
            case 0x46: c.V0 = 0u; break;
            case 0x47: c.V0 = GetFreeEvSlot(); break;
            case 0x48: c.V0 = 0xFFFFFFFFu; break;
            case 0x49: break;
            case 0x4A: c.V0 = 1u; break;
            case 0x4B: c.V0 = 1u; break;
            case 0x4C: c.V0 = 1u; break;
            case 0x4D: break;
            case 0x4E: CardWrite(c, m); break;
            case 0x4F: CardRead(c, m); break;
            case 0x50: break;
            case 0x51: c.V0 = KromFont.Krom2RawAdd(c.A0); break;
            case 0x53: c.V0 = KromFont.Krom2Offset(c.A0); break;
            case 0x54: c.V0 = BiosA.LastErrno; break;
            case 0x55: c.V0 = 0u; break;
            case 0x56: c.V0 = 0u; break;
            case 0x57: c.V0 = 0u; break;
            case 0x58: break;
            case 0x59: c.V0 = BiosA.TestDevice(m, c.A0); break;
            case 0x5B: c.V0 = 0u; break;
            case 0x5C: c.V0 = 0u; break;
            case 0x5D: break;
            default: break;
        }
    }

    static uint OpenEvent(uint @class, uint spec, uint mode, uint func)
    {
        for (int i = 0; i < MaxEvents; i++)
        {
            if (_evCBs[i].Status == 0u)
            {
                _evCBs[i] = new EvCB { Status = 1u, Class = @class, Spec = spec, Mode = mode, Func = func };
                return 0xF0000000u | (uint)i;
            }
        }
        return 0xFFFFFFFFu;
    }

    static int EvSlot(uint ev)
    {
        int i = (int)(ev & 0xFFu);
        return i < MaxEvents ? i : -1;
    }

    static void CloseEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0) _evCBs[s] = default;
    }

    static uint WaitEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0 && _evCBs[s].Status == 4u) _evCBs[s].Status = 2u;
        return 1u;
    }

    static uint TestEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0 && _evCBs[s].Status == 4u) { _evCBs[s].Status = 2u; return 1u; }
        return 0u;
    }

    static void EnableEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0) _evCBs[s].Status = 2u;
    }

    static void DisableEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0 && _evCBs[s].Status != 0u) _evCBs[s].Status = 1u;
    }

    static void UnDeliverEvent(uint @class, uint spec)
    {
        for (int i = 0; i < MaxEvents; i++)
            if (_evCBs[i].Status == 4u && _evCBs[i].Class == @class && _evCBs[i].Spec == spec)
                _evCBs[i].Status = 2u;
    }
    static uint OpenTh(uint pc, uint spFp, uint gp)
    {
        for (int i = 0; i < MaxThreads; i++)
            if (!_tcbs[i].Used) { _tcbs[i] = new TCB { Used = true }; return 0xFF000000u | (uint)i; }
        return 0xFFFFFFFFu;
    }
    static void CloseTh(uint handle)
    {
        int i = (int)(handle & 0xFFu);
        if (i < MaxThreads) _tcbs[i] = default;
    }

    public static void SysEnqIntRP(CpuContext c, IMemory m)
    {
        uint priority = c.A0 & 3u;
        uint struc = c.A1;
        c.V0 = _intChain[priority];
        m.WriteU32(struc, _intChain[priority]);
        _intChain[priority] = struc;
    }
    public static void SysDeqIntRP(CpuContext c, IMemory m)
    {
        uint priority = c.A0 & 3u;
        uint struc = c.A1;
        if (_intChain[priority] == struc)
        {
            _intChain[priority] = m.ReadU32(struc);
            c.V0 = 1u;
            return;
        }
        uint cur = _intChain[priority];
        while (cur != 0u)
        {
            uint next = m.ReadU32(cur);
            if (next == struc) { m.WriteU32(cur, m.ReadU32(struc)); c.V0 = 1u; return; }
            cur = next;
        }
        c.V0 = 0u;
    }
}
