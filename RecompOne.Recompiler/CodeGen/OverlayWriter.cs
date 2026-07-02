using System.Text;
using System.Text.RegularExpressions;
using RecompOne.Recompiler.Analysis;
using RecompOne.Recompiler.Config;
using RecompOne.Recompiler.Disasm;
using RecompOne.Recompiler.Elf;
using RecompOne.Recompiler.Psx;
using RecompOne.Runtime.Cdrom;

namespace RecompOne.Recompiler.CodeGen;

public static class OverlayWriter
{
    record OverlayResult(string Name, List<MipsFunction> Functions, int LbaStart);

    public static void Write(RecompOneConfig config, CueFs fs, string outDir)
    {
        string className = SafeIdentifier(config.Game.Name);

        Console.WriteLine("[Recompiler] reading SYSTEM.CNF");
        var sysCfg = SystemCfg.Parse(fs);
        Console.WriteLine($"[Recompiler] SYSTEM.CNF: BOOT={sysCfg.BootExe}  TCB={sysCfg.Tcb}  EVENT={sysCfg.Event}  STACK=0x{sysCfg.Stack:X8}");

        var mainExe = Parser.ParseExe(fs, sysCfg.BootExe);
        Console.WriteLine($"[Recompiler] PS-EXE: {mainExe.Region}");
        Console.WriteLine($"[Recompiler] PS-EXE: PC=0x{mainExe.InitialPC:X8}  GP=0x{mainExe.InitialGP:X8}  SP=0x{mainExe.InitialSP:X8}  load=0x{mainExe.Destination:X8} ");

        var overlayResults = new List<OverlayResult>();

        {
            List<MipsFunction> funcs;
            MipsInstruction[] mainInstrs;
            ElfInfo? elfInfo = null;

            if (config.Elf != null)
            {
                if (!File.Exists(config.Elf))
                    throw new FileNotFoundException($"Main ELF not found: {config.Elf}");

                Console.WriteLine($"[Recompiler] Processing main executable: {config.Elf}");
                elfInfo = ElfReader.Read(config.Elf);
                Console.WriteLine($"[Recompiler] ELF map: TextBase=0x{elfInfo.TextBase:X8} Functions={elfInfo.Functions.Count}");
                Console.WriteLine($"[Recompiler] code source: disc PS-EXE {sysCfg.BootExe} ({mainExe.Code.Length}B)");

                mainInstrs = MipsDisasm.Disassemble(mainExe.Code, elfInfo.TextBase);

                funcs = elfInfo.Functions.Count > 0 ? FunctionDetector.DetectFromElf(mainInstrs, elfInfo, "main") : FunctionDetector.DetectFromScan(mainInstrs, elfInfo.LoadAddress, "main");
            }
            else
            {
                Console.WriteLine("[Recompiler] processing main executable");
                mainInstrs = MipsDisasm.Disassemble(mainExe.Code, mainExe.Destination);
                funcs = FunctionDetector.DetectFromScan(mainInstrs, mainExe.InitialPC, "main");
            }

            if (funcs.All(f => f.Start != mainExe.InitialPC))
            {
                var entry = FunctionDetector.DetectFromAddresses(mainInstrs, [(mainExe.InitialPC, null)], funcs, "main");
                funcs.AddRange(entry);
                Console.WriteLine($"[Recompiler] added entry point function at 0x{mainExe.InitialPC:X8}");
            }

            var mainDiscovered = FunctionDetector.DiscoverCalls(mainInstrs, funcs, elfInfo?.NoTypeSymbols ?? [], "main");
            if (mainDiscovered.Count > 0)
            {
                funcs.AddRange(mainDiscovered);
                Console.WriteLine($"[Recompiler] discovered {mainDiscovered.Count} called function(s) in main");
            }

            AddConfigFunctions(funcs, config.Functions, mainInstrs, elfInfo?.NoTypeSymbols ?? [], "main");

            if (config.LinearSweep)
                SweepFunctions(funcs, mainInstrs, elfInfo?.NoTypeSymbols ?? [], "main");

            if (elfInfo != null) AnalyzeJumpTables(funcs, elfInfo, "main");

            ApplyStubsAndIgnored(funcs, config.Stubs, config.Ignored);
            overlayResults.Add(new OverlayResult("main", funcs, -1));
        }

        foreach (var overlayConfig in config.Overlays)
        {
            if (overlayConfig.Elf == null)
            {
                Console.WriteLine($"[Recompiler] WARNING: Overlay '{overlayConfig.Name}' has no ELF defined, this will be skiped");
                continue;
            }
            if (!File.Exists(overlayConfig.Elf))
            {
                Console.WriteLine($"[Recompiler] WARNING: ELF file not found for overlay '{overlayConfig.Name}' ({overlayConfig.Elf}), this will be skiped.");
                continue;
            }

            Console.WriteLine($"[Recompiler] processing the overlay {overlayConfig.Name}");
            var elfInfo = ElfReader.Read(overlayConfig.Elf);

            var (discBin, overlayLba) = ResolveOverlay(fs, overlayConfig);
            if (discBin == null)
            {
                Console.WriteLine($"[Recompiler] WARNING: could not resolve disc data for overlay '{overlayConfig.Name}', skipping");
                continue;
            }

            if (overlayConfig.Rebase != 0)
                RebaseElf(elfInfo, overlayConfig.Rebase, discBin);

            var instrs = MipsDisasm.Disassemble(discBin, elfInfo.TextBase);

            //elf is weird and doest properly provide all functions (specially asm) so resort to checking it
            var funcs = elfInfo.Functions.Count > 0 ? FunctionDetector.DetectFromElf(instrs, elfInfo, overlayConfig.Name) : FunctionDetector.DetectFromScan(instrs, elfInfo.LoadAddress, overlayConfig.Name);

            var discovered = FunctionDetector.DiscoverCalls(instrs, funcs, elfInfo.NoTypeSymbols, overlayConfig.Name);
            if (discovered.Count > 0)
            {
                funcs.AddRange(discovered);
                Console.WriteLine($"[Recompiler] discovered {discovered.Count} called funs in {overlayConfig.Name}");
            }

            AddConfigFunctions(funcs, overlayConfig.Functions, instrs, elfInfo.NoTypeSymbols, overlayConfig.Name);

            if (overlayConfig.LinearSweep ?? config.LinearSweep)
                SweepFunctions(funcs, instrs, elfInfo.NoTypeSymbols, overlayConfig.Name);

            AnalyzeJumpTables(funcs, elfInfo, overlayConfig.Name);

            ApplyStubsAndIgnored(funcs, overlayConfig.Stubs.Concat(config.Stubs), overlayConfig.Ignored.Concat(config.Ignored));
            overlayResults.Add(new OverlayResult(overlayConfig.Name, funcs, overlayLba));
        }

        var allFuncs = overlayResults.SelectMany(o => o.Functions).ToList();
        ResolveCollisions(allFuncs);
        ApplyPatches(allFuncs, config.Patches);
        SdkPatches.Apply(allFuncs);

        var uniqueAddrs = allFuncs.GroupBy(f => f.Start).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
        var knownFuncs = allFuncs.Where(f => uniqueAddrs.Contains(f.Start)).ToDictionary(f => f.Start, f => $"{className}.{f.EmittedName}");

        int conflictCount = allFuncs.Count - knownFuncs.Count;
        Console.WriteLine($"[Recompiler] total functions: {allFuncs.Count}");

        string? mainCall = null;
        if (config.Main != null)
        {
            uint mainAddr = Convert.ToUInt32(config.Main, 16);
            var mainFunc = allFuncs.FirstOrDefault(f => f.Start == mainAddr);
            if (mainFunc == null) throw new InvalidOperationException($"[recompiler] the main function not found at 0x{mainAddr:X8}");
            mainCall = $"{className}.{mainFunc.EmittedName}";
            Console.WriteLine($"[Recompiler] main: {mainCall} @ 0x{mainAddr:X8}");
        }

        foreach (var result in overlayResults)
        {
            Console.WriteLine($"[Recompiler] emiting {result.Name}.cs ({result.Functions.Count} functions)");
            EmitOverlayFile(result.Name, result.Functions, className, knownFuncs, config.Debug, result.LbaStart, outDir);
        }

        Console.WriteLine("[Recompiler] Emitting Entry.cs");
        var overlayNames = overlayResults.Select(o => o.Name).ToList();
        EntryWriter.Write(mainExe, sysCfg.BootExe, className, mainCall, overlayNames, outDir);

        Console.WriteLine("[Recompiler] finished "); //maybe add time it took
    }
    
    static void AddConfigFunctions(List<MipsFunction> funcs, Config.FunctionEntry[] entries, MipsInstruction[] instrs, IEnumerable<Elf.FunctionEntry> noTypeSymbols, string overlayName)
    {
        if (entries.Length == 0) return;

        var have = new HashSet<uint>(funcs.Select(f => f.Start));
        var missing = entries
            .Select(f => (Addr: Convert.ToUInt32(f.Address, 16), f.Name))
            .Where(e => have.Add(e.Addr))
            .ToList();
        if (missing.Count == 0) return;

        var extras = FunctionDetector.DetectFromAddresses(instrs, missing.Select(e => (e.Addr, e.Name)), funcs, overlayName);
        funcs.AddRange(extras);
        var callees = FunctionDetector.DiscoverCalls(instrs, funcs, noTypeSymbols, overlayName);
        funcs.AddRange(callees);
        Console.WriteLine($"[Recompiler] added {extras.Count} config function(s) (+{callees.Count} callees) to {overlayName}");
    }

    static void SweepFunctions(List<MipsFunction> funcs, MipsInstruction[] instrs, IEnumerable<Elf.FunctionEntry> noTypeSymbols, string overlayName)
    {
        var swept = FunctionDetector.LinearSweep(instrs, funcs, noTypeSymbols, overlayName);
        if (swept.Count == 0) return;
        funcs.AddRange(swept);
        var callees = FunctionDetector.DiscoverCalls(instrs, funcs, noTypeSymbols, overlayName);
        funcs.AddRange(callees);
        Console.WriteLine($"[Recompiler] linear sweep found {swept.Count} function(s) (+{callees.Count} callees) in {overlayName}");
    }

    static void EmitOverlayFile(string overlayName, List<MipsFunction> funcs, string className, Dictionary<uint, string> knownFuncs, bool debug, int lbaStart, string outDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using RecompOne.Runtime.Context;");
        sb.AppendLine("using RecompOne.Runtime.Dispatch;");
        sb.AppendLine("using RecompOne.Runtime.Memory;");
        sb.AppendLine();
        sb.AppendLine("namespace Recompiled;");
        sb.AppendLine();
        sb.AppendLine($"public static partial class {className}");
        sb.AppendLine("{");

        foreach (var func in funcs.OrderBy(f => f.Start))
        {
            var labels = LabelManager.Collect(func);
            var ctx = new FunctionContext
            {
                FuncStart = func.Start,
                FuncEnd = func.End,
                KnownFunctions = knownFuncs,
                Labels = labels,
                Debug = debug,
                JumpTablesByJr = func.JumpTables.ToDictionary(j => j.JrVram),
                RaReturnJrs = FunctionDetector.ComputeRaReturnJrs(func)
            };
            sb.Append(FunctionEmitter.Emit(func, ctx));
        }

        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {DispatchTableName(overlayName)} : IOverlay");
        sb.AppendLine("{");
        sb.AppendLine($"    public string Name => \"{overlayName}\";");
        sb.AppendLine($"    public int LbaStart => {lbaStart};");
        sb.AppendLine("    public IReadOnlyDictionary<uint, Action<CpuContext, IMemory>> Functions { get; } =");
        sb.AppendLine("        new Dictionary<uint, Action<CpuContext, IMemory>>");
        sb.AppendLine("        {");
        foreach (var func in funcs.Where(f => !f.IsStub).OrderBy(f => f.Start))
            sb.AppendLine($"            [0x{func.Start:X8}u] = {className}.{func.EmittedName},");
        sb.AppendLine("        };");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(outDir, $"{overlayName}.cs"), sb.ToString());
    }

    static string DispatchTableName(string name) => $"{char.ToUpperInvariant(name[0])}{name[1..]}DispatchTable";

    static void ResolveCollisions(List<MipsFunction> allFuncs)
    {
        var crossOverlayDups = allFuncs.GroupBy(f => f.Name)
            .Where(g => g.Select(f => f.OverlayName).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet();

        foreach (var func in allFuncs)
            func.EmittedName = SafeFuncName(crossOverlayDups.Contains(func.Name) && !string.IsNullOrEmpty(func.OverlayName)
                ? $"{func.Name}_{SafeIdentifier(func.OverlayName)}"
                : func.Name);

        foreach (var group in allFuncs.GroupBy(f => (f.OverlayName, f.EmittedName)).Where(g => g.Count() > 1))
            foreach (var func in group)
                func.EmittedName = $"{func.EmittedName}_{func.Start:X8}";
    }

    static string SafeFuncName(string s) => Regex.Replace(s, @"[^A-Za-z0-9_]", "_");

    static (byte[]? data, int lba) ResolveOverlay(CueFs fs, Config.OverlayConfig cfg)
    {
        try
        {
            if (cfg.Lba >= 0)
            {
                int sz = cfg.Size ?? throw new InvalidOperationException($"'size' is required when using 'lba' for overlay '{cfg.Name}'");
                return (Decrypt(fs.ReadSectors(cfg.Lba, sz), cfg.Decrypt), cfg.Lba);
            }
            if (cfg.File != null)
            {
                if (!fs.Locate(cfg.File, out int lba, out uint fileSize))
                {
                    Console.WriteLine($"[Recompiler] WARNING: disc file not found: {cfg.File}");
                    return (null, -1);
                }
                int absLba = lba + cfg.Offset / 2048;
                byte[] full = fs.ReadFile(cfg.File);
                int start = cfg.Offset + cfg.Skip;
                int length = cfg.Size ?? (full.Length - start);
                return (Decrypt(full.AsSpan(start, length).ToArray(), cfg.Decrypt), absLba);
            }
            Console.WriteLine($"[Recompiler] WARNING: overlay '{cfg.Name}' has no 'file' or 'lba' source defined");
            return (null, -1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Recompiler] WARNING: failed to resolve disc data for '{cfg.Name}': {ex.Message}");
            return (null, -1);
        }
    }

    
    static void RebaseElf(ElfInfo elf, int delta, byte[] discBin)
    {
        uint d = (uint)delta;
        elf.TextBase += d;
        elf.LoadAddress += d;
        foreach (var f in elf.Functions) f.Address += d;
        foreach (var f in elf.NoTypeSymbols) f.Address += d;
        foreach (var s in elf.DataSections) s.Va += d;
        elf.TextData = discBin;
    }

    static byte[] Decrypt(byte[] data, bool decrypt)
    {
        if (!decrypt) return data;
        uint seed = 0;
        for (int i = 0; i + 4 <= data.Length; i += 4)
        {
            seed = (seed + 0x01309125u) * 0x03A452F7u;
            uint w = BitConverter.ToUInt32(data, i) ^ seed;
            BitConverter.GetBytes(w).CopyTo(data, i);
        }
        return data;
    }
    static void AnalyzeJumpTables(List<MipsFunction> funcs, ElfInfo elf, string name)
    {
        int funcsWithTables = 0, totalEntries = 0;
        foreach (var func in funcs)
        {
            func.JumpTables = JumpTableAnalyzer.Analyze(func, elf);
            if (func.JumpTables.Count == 0) continue;
            funcsWithTables++;
            foreach (var jt in func.JumpTables) totalEntries += jt.Entries.Length;
        }
        if (funcsWithTables > 0)
            Console.WriteLine($"[Recompiler] {name}: found jump tables in {funcsWithTables} function(s), {totalEntries} entries in total");
    }
    
    static void ApplyPatches(List<MipsFunction> funcs, Config.PatchEntry[] patches)
    {
        if (patches.Length == 0) return;
        int applied = 0;
        foreach (var patch in patches)
        {
            uint? addr = string.IsNullOrEmpty(patch.Address) ? null : Convert.ToUInt32(patch.Address, 16);
            int matched = 0;
            foreach (var func in funcs)
            {
                if (!string.IsNullOrEmpty(patch.Overlay) && !string.Equals(func.OverlayName, patch.Overlay, StringComparison.OrdinalIgnoreCase)) continue;
                bool hit = addr.HasValue ? func.Start == addr.Value : string.Equals(func.Name, patch.Function, StringComparison.Ordinal);
                if (!hit) continue;
                func.IsPatch = true;
                func.PatchTarget = patch.Target;
                matched++;
                applied++;
            }
            if (matched == 0)
                Console.WriteLine($"[Recompiler] WARNING: patch '{patch.Target}' matched nothing (overlay='{patch.Overlay}' function='{patch.Function}' address='{patch.Address}')");
        }
        Console.WriteLine($"[Recompiler] applied {applied} patches");
    }

    static void ApplyStubsAndIgnored(List<MipsFunction> funcs, IEnumerable<string> stubs, IEnumerable<string> ignored)
    {
        var stubSet = new HashSet<string>(stubs,   StringComparer.OrdinalIgnoreCase);
        var ignoredSet = new HashSet<string>(ignored, StringComparer.OrdinalIgnoreCase);
        foreach (var func in funcs)
        {
            if (ignoredSet.Contains(func.Name)) { func.IsStub = true; func.Name = "__ignored__"; }
            else if (stubSet.Contains(func.Name)) func.IsStub = true;
        }
        funcs.RemoveAll(f => f.Name == "__ignored__");
    }
    
    
    
    static string SafeIdentifier(string s) => Regex.Replace(s, @"[^a-zA-Z0-9_]", "_").TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
    
    
}
