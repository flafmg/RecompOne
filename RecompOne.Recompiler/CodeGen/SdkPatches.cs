using RecompOne.Recompiler.Analysis;

namespace RecompOne.Recompiler.CodeGen;


public static class SdkPatches
{
    static readonly (string Class, string[] Names)[] Libraries =
    {
        ("RecompOne.Runtime.Sdk.LibCd", new[]
        {
            "CdInit", "CdReset", "CdControl", "CdControlF", "CdControlB",
            "CdSync", "CdReady", "CdRead", "CdReadSync", "CdGetSector",
            "CdDataSync", "CdSearchFile", "CdSyncCallback", "CdReadyCallback",
            "CdReadCallback", "CdDataCallback", "CdStatus", "CdMode",
            "CdLastCom", "CdMix",
        }),
        ("RecompOne.Runtime.Sdk.LibEtc", new[]
        {
            "VSync",
        }),
        ("RecompOne.Runtime.Sdk.LibGpu", new[]
        {
            "DrawOTag", "DrawSync",
        }),
        ("RecompOne.Runtime.Sdk.LibCdStream", new[]
        {
            "StSetRing", "StClearRing", "StUnSetRing", "StSetStream",
            "StSetMask", "StGetNext", "StFreeRing", "StGetBackloc",
        }),
        ("RecompOne.Runtime.Sdk.LibPad", new[]
        {
            "PadInitDirect", "PadStartCom", "PadStopCom", "PadEnableCom",
            "PadChkVsync", "PadChkMtap", "PadGetState", "PadInfoMode",
            "PadInfoAct", "PadInfoComb", "PadSetMainMode", "PadSetActAlign",
            "PadSetAct",
        }),

    };

    public static void Apply(List<MipsFunction> funcs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cls, names) in Libraries)
            foreach (var name in names)
                map[name] = $"{cls}.{name}";

        int applied = 0;
        foreach (var func in funcs)
        {
            if (func.IsPatch || func.IsStub) continue;
            if (map.TryGetValue(func.Name, out var target))
            {
                func.IsPatch = true;
                func.PatchTarget = target;
                applied++;
            }
        }
        Console.WriteLine($"[Recompiler] it was applied {applied} reimplementations");
    }
}
