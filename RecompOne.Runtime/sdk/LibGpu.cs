using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibGpu
{

    public static void DrawOTag(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) return;

        uint addr = c.A0 & 0x1FFFFCu;
        for (int guard = 0; guard < 0x100000; guard++)
        {
            uint header = m.ReadU32(addr);
            uint count = header >> 24;
            for (uint i = 0; i < count; i++)
                gpu.WriteGp0(m.ReadU32(addr + 4u + i * 4u));
            uint next = header & 0xFFFFFFu;
            if (next == 0xFFFFFFu || (next & 0x800000u) != 0) break;
            addr = next & 0x1FFFFCu;
        }
    }
    
    public static void DrawSync(CpuContext c, IMemory m) => c.V0 = 0;
}
