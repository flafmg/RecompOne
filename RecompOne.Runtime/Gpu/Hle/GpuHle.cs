namespace RecompOne.Runtime.Hle;

public static class GpuHle
{
    public static bool Active { get; set; }
    public static IGpuBackend? Backend { get; set; }
}
