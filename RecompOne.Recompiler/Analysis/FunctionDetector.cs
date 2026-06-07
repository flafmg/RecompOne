using RecompOne.Recompiler.Disasm;
using RecompOne.Recompiler.Elf;

namespace RecompOne.Recompiler.Analysis;

public static class FunctionDetector
{
    public static List<MipsFunction> DetectFromElf(MipsInstruction[] all, ElfInfo elf, string overlayName)
    {
        if (all.Length == 0) return [];

        var funcs = new List<MipsFunction>();
        uint codeStart = all[0].Vram;

        foreach (var sym in elf.Functions.OrderBy(f => f.Address))
        {
            if (sym.Address < codeStart || sym.Address >= codeStart + (uint)(all.Length * 4)) continue;

            int startIdx = InstrIndex(all, sym.Address);
            int endIdx = InstrIndex(all, sym.Address + sym.Size);
            if (startIdx < 0 || startIdx >= all.Length) continue;
            endIdx = Math.Min(endIdx, all.Length);

            funcs.Add(new MipsFunction
            {
                Name = sym.Name,
                OverlayName = overlayName,
                EmittedName = sym.Name,
                Start = sym.Address,
                End = sym.Address + sym.Size,
                Instructions = all[startIdx..endIdx]
            });
        }

        return funcs;
    }

    public static List<MipsFunction> DetectFromScan(MipsInstruction[] all, uint entryPoint, string overlayName)
    {
        if (all.Length == 0) return [];

        uint codeStart = all[0].Vram;
        uint codeEnd = all[^1].Vram + 4;

        var entries = new SortedSet<uint> { entryPoint };
        foreach (var instr in all)
        {
            uint op = instr.Word >> 26;
            if (op == 3) // JAL
                entries.Add(instr.JumpTarget);
        }

        var sorted = entries.Where(e => e >= codeStart && e < codeEnd).OrderBy(e => e).ToList();
        var funcs = new List<MipsFunction>();

        for (int i = 0; i < sorted.Count; i++)
        {
            uint start = sorted[i];
            uint maxEnd = i + 1 < sorted.Count ? sorted[i + 1] : codeEnd;

            int si = InstrIndex(all, start);
            if (si < 0) continue;
            int ei = Math.Clamp(RefineEnd(all, si, InstrIndex(all, maxEnd)), si + 1, all.Length);

            string name = $"func_{start:X8}";
            funcs.Add(new MipsFunction
            {
                Name = name,
                OverlayName = overlayName,
                EmittedName = name,
                Start = start,
                End = all[ei - 1].Vram + 4,
                Instructions = all[si..ei]
            });
        }

        return funcs;
    }

    public static List<MipsFunction> DetectFromAddresses(MipsInstruction[] all, IEnumerable<(uint Address, string? Name)> entries, List<MipsFunction> existing, string overlayName)
    {
        if (all.Length == 0) return [];
        uint codeEnd = all[^1].Vram + 4;

        var entryList = entries.ToList();

        var existingStarts = existing.Select(f => f.Start).Distinct().OrderBy(a => a).ToList();

        var result = new List<MipsFunction>();
        foreach (var (addr, nameHint) in entryList)
        {
            int startIdx = InstrIndex(all, addr);
            if (startIdx < 0 || startIdx >= all.Length) continue;
            
            uint extEnd = existingStarts.FirstOrDefault(s => s > addr, codeEnd);
            int endIdx = Math.Clamp(RefineEnd(all, startIdx, InstrIndex(all, extEnd)), startIdx + 1, all.Length);

            string name = nameHint ?? $"func_{addr:X8}";
            result.Add(new MipsFunction
            {
                Name = name,
                OverlayName = overlayName,
                EmittedName = name,
                Start = addr,
                End = all[endIdx - 1].Vram + 4,
                Instructions = all[startIdx..endIdx]
            });
        }
        return result;
    }

    public static List<MipsFunction> DiscoverCalls(MipsInstruction[] all, List<MipsFunction> existing, IEnumerable<FunctionEntry> noTypeSymbols, string overlayName)
    {
        if (all.Length == 0) return [];

        uint codeStart = all[0].Vram;
        uint codeEnd = all[^1].Vram + 4;
        var named = noTypeSymbols.GroupBy(s => s.Address).ToDictionary(g => g.Key, g => g.First());

        var allFuncs = new List<MipsFunction>(existing);
        var knownStarts = new HashSet<uint>(existing.Select(f => f.Start));
        var result = new List<MipsFunction>();
        var frontier = new List<MipsFunction>(existing);

        while (frontier.Count > 0)
        {
            var targets = new SortedSet<uint>();
            foreach (var f in frontier)
                foreach (var instr in f.Instructions)
                {
                    if ((instr.Word >> 26) != 3) continue;
                    uint t = instr.JumpTarget;
                    if (t < codeStart || t >= codeEnd) continue;
                    if (knownStarts.Contains(t)) continue;
                    if (allFuncs.Any(g => t > g.Start && t < g.End)) continue;
                    targets.Add(t);
                }

            if (targets.Count == 0) break;

            var bounds = allFuncs.Select(f => f.Start).Concat(targets).Distinct().OrderBy(a => a).ToList();
            var batch = new List<MipsFunction>();
            foreach (var addr in targets)
            {
                var fn = BuildFunc(all, addr, bounds, named, codeEnd, overlayName);
                if (fn == null) continue;
                batch.Add(fn);
                knownStarts.Add(addr);
            }

            result.AddRange(batch);
            allFuncs.AddRange(batch);
            frontier = batch;
        }
        
        var finalStarts = allFuncs.Select(f => f.Start).Distinct().OrderBy(a => a).ToList();
        foreach (var f in result)
        {
            var refreshed = BuildFunc(all, f.Start, finalStarts, named, codeEnd, overlayName);
            if (refreshed == null || refreshed.End >= f.End) continue;
            f.End = refreshed.End;
            f.Instructions = refreshed.Instructions;
        }
        return result;
    }

    public static List<MipsFunction> LinearSweep(MipsInstruction[] all, List<MipsFunction> existing, IEnumerable<FunctionEntry> noTypeSymbols, string overlayName)
    {
        if (all.Length == 0) return [];

        uint codeEnd = all[^1].Vram + 4;
        var named = noTypeSymbols.GroupBy(s => s.Address).ToDictionary(g => g.Key, g => g.First());

        var claimed = new List<MipsFunction>(existing);
        var knownStarts = new SortedSet<uint>(existing.Select(f => f.Start));
        var result = new List<MipsFunction>();

        int i = 0;
        while (i < all.Length)
        {
            uint addr = all[i].Vram;

            var cover = claimed.FirstOrDefault(f => f.Start <= addr && addr < f.End);
            if (cover != null) { i = Math.Max(i + 1, InstrIndex(all, cover.End)); continue; }

            if (all[i].IsNop) { i++; continue; }

            uint nextStart = knownStarts.FirstOrDefault(s => s > addr, codeEnd);
            int boundIdx = InstrIndex(all, nextStart);

            if (!ValidatesAsFunction(all, i, boundIdx)) { i++; continue; }

            int ei = Math.Clamp(RefineEnd(all, i, boundIdx), i + 1, all.Length);

            string name = $"func_{addr:X8}";
            if (named.TryGetValue(addr, out var sym) && !string.IsNullOrEmpty(sym.Name)) name = sym.Name;

            var fn = new MipsFunction
            {
                Name = name,
                OverlayName = overlayName,
                EmittedName = name,
                Start = addr,
                End = all[ei - 1].Vram + 4,
                Instructions = all[i..ei]
            };
            result.Add(fn);
            claimed.Add(fn);
            knownStarts.Add(addr);
            i = ei;
        }

        return result;
    }
    
    static bool ValidatesAsFunction(MipsInstruction[] all, int startIdx, int boundIdx)
    {
        boundIdx = Math.Clamp(boundIdx, startIdx + 1, all.Length);
        for (int i = startIdx; i < boundIdx; i++)
        {
            var instr = all[i];
            if (!instr.IsValid || !instr.IsImplemented) return false;
            if (IsFunctionEnd(all, startIdx, i)) return true;
        }
        return false;
    }

    static MipsFunction? BuildFunc(MipsInstruction[] all, uint addr, List<uint> starts, Dictionary<uint, FunctionEntry> named, uint codeEnd, string overlayName)
    {
        int si = InstrIndex(all, addr);
        if (si < 0 || si >= all.Length) return null;

        uint maxEnd = starts.FirstOrDefault(s => s > addr, codeEnd);
        string name = $"func_{addr:X8}";
        if (named.TryGetValue(addr, out var sym))
        {
            name = sym.Name;
            if (sym.Size > 0 && addr + sym.Size < maxEnd) maxEnd = addr + sym.Size;
        }

        int ei = Math.Clamp(RefineEnd(all, si, InstrIndex(all, maxEnd)), si + 1, all.Length);
        return new MipsFunction
        {
            Name = name,
            OverlayName = overlayName,
            EmittedName = name,
            Start = addr,
            End = all[ei - 1].Vram + 4,
            Instructions = all[si..ei]
        };
    }

    static int RefineEnd(MipsInstruction[] all, int startIdx, int maxEndIdx)
    {
        maxEndIdx = Math.Clamp(maxEndIdx, startIdx + 1, all.Length);
        uint reach = all[startIdx].Vram;
        for (int i = startIdx; i < maxEndIdx; i++)
        {
            var instr = all[i];
            if (instr.IsJump || (instr.IsBranch && !instr.IsRegisterJump))
            {
                uint tgt = instr.IsJump ? instr.JumpTarget : instr.BranchTarget;
                if (tgt > reach && tgt > all[startIdx].Vram && tgt <= all[maxEndIdx - 1].Vram) reach = tgt;
            }
            if (IsFunctionEnd(all, startIdx, i) && instr.Vram >= reach)
            {
                int end = i + 2; // include the delay slot
                return Math.Clamp(end, startIdx + 1, maxEndIdx);
            }
        }
        return maxEndIdx;
    }
    
    static bool IsFunctionEnd(MipsInstruction[] all, int startIdx, int i)
    {
        var instr = all[i];
        if (instr.IsReturn) return true;
        if (!instr.IsJrRegister) return false;
        int reg = instr.Rs;
        for (int k = i - 1; k >= startIdx; k--)
        {
            if (!WritesReg(all[k], reg)) continue;
            return !all[k].IsLoad;
        }
        return true;
    }

    static bool WritesReg(MipsInstruction p, int reg)
    {
        if (reg == 0) return false;
        uint op = p.Word >> 26;
        if (op == 0)
        {
            uint fn = p.Word & 0x3F;
            bool noWrite = fn is 8 or 9 or 16 or 18 or 24 or 25 or 26 or 27; // jr,jalr,mthi,mtlo,mult,multu,div,divu
            return !noWrite && p.Rd == reg;
        }
        if (p.IsLoad) return p.Rt == reg;
        if (op is 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15) return p.Rt == reg; // addi(u),slti(u),andi,ori,xori,lui
        return false;
    }

    //shouldbe the right behaviour now? in theory
    public static HashSet<uint> ComputeRaReturnJrs(MipsFunction func)
    {
        var instrs = func.Instructions;
        var writeCount = new int[32];
        var raMoveCount = new int[32];
        bool raIsEntry = true;

        foreach (var ins in instrs)
        {
            int mv = MoveFromRa(ins);
            if (mv > 0 && raIsEntry) raMoveCount[mv]++;
            int dst = DestReg(ins);
            if (dst > 0) writeCount[dst]++;
            if (dst == 31) raIsEntry = false;
        }

        var isAlias = new bool[32];
        for (int r = 1; r < 32; r++)
            if (r != 31 && raMoveCount[r] > 0 && writeCount[r] == raMoveCount[r])
                isAlias[r] = true;

        var result = new HashSet<uint>();
        foreach (var ins in instrs)
        {
            uint op = ins.Word >> 26, fn = ins.Word & 0x3F;
            if (op == 0 && fn == 8 && ins.Rs != 31 && ins.Rs > 0 && isAlias[ins.Rs])
                result.Add(ins.Vram);
        }
        return result;
    }

    static int MoveFromRa(MipsInstruction i)
    {
        uint op = i.Word >> 26, fn = i.Word & 0x3F;
        int rs = i.Rs, rt = i.Rt, rd = i.Rd;
        short imm = i.ImmS;
        if (op == 0 && (fn == 0x21 || fn == 0x25))
        {
            if (rs == 31 && rt == 0) return rd;
            if (rt == 31 && rs == 0) return rd;
        }
        if ((op == 0x08 || op == 0x09 || op == 0x0D) && rs == 31 && imm == 0)
            return rt;
        return -1;
    }

    static int DestReg(MipsInstruction i)
    {
        uint op = i.Word >> 26, fn = i.Word & 0x3F;
        int rt = i.Rt, rd = i.Rd;
        switch (op)
        {
            case 0:
                return fn switch
                {
                    0x08 => -1,
                    0x09 => rd,
                    0x0C or 0x0D => -1,
                    0x11 or 0x13 => -1,
                    0x18 or 0x19 or 0x1A or 0x1B => -1,
                    _ => rd,
                };
            case 0x01: return rt is 0x10 or 0x11 ? 31 : -1;
            case 0x03: return 31;
            case 0x02:
            case 0x04: case 0x05: case 0x06: case 0x07: return -1;
            case 0x08: case 0x09: case 0x0A: case 0x0B:
            case 0x0C: case 0x0D: case 0x0E: case 0x0F:
            case 0x20: case 0x21: case 0x22: case 0x23:
            case 0x24: case 0x25: case 0x26: return rt;
            case 0x10: case 0x11: case 0x12: case 0x13:
                return i.Rs is 0 or 2 ? rt : -1;
            default: return -1;
        }
    }

    static int InstrIndex(MipsInstruction[] all, uint vram)
    {
        if (all.Length == 0) return -1;
        uint base0 = all[0].Vram;
        if (vram < base0) return -1;
        return (int)((vram - base0) / 4);
    }
}
