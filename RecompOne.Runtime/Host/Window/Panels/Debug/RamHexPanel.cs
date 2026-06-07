using System.Numerics;
using System.Text;
using ImGuiNET;
using RecompOne.Runtime.Memory;
using System.Globalization;

namespace RecompOne.Runtime.Host.Window;

internal sealed class RamHexPanel : IPanel
{
    public string Name => "RAM Hex";
    public bool IsOpen { get; set; }
    const int BytesPerRow = 16;

    uint _baseAddr;
    string _addrInput = "80000000";
    bool _scrollPending;

    public void JumpTo(uint physAddr)
    {
        _baseAddr = physAddr & ~(uint)(BytesPerRow - 1);
        _addrInput = $"{0x80000000u + physAddr:X8}";
        _scrollPending = true;
    }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open)) { IsOpen = open; ImGui.End(); return; }

        var mem = Runtime.Mem as PSMemory;
        if (mem == null) { ImGui.TextDisabled("No memory"); ImGui.End(); IsOpen = open; return; }

        DrawToolbar();
        ImGui.Separator();
        DrawHexContent(mem.Ram);

        IsOpen = open;
        ImGui.End();
    }

    void DrawToolbar()
    {
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("##addr", ref _addrInput, 10,
            ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (uint.TryParse(_addrInput, NumberStyles.HexNumber, null, out uint parsed))
            {
                uint phys = parsed & 0x1FFFFFFFu;
                if (phys < 0x200000u) JumpTo(phys);
            }
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Go to address (hex)");
    }

    void DrawHexContent(ReadOnlySpan<byte> ram)
    {
        int totalRows = (ram.Length + BytesPerRow - 1) / BytesPerRow;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));

        if (!ImGui.BeginChild("##hexscroll", Vector2.Zero, ImGuiChildFlags.None))
        {
            ImGui.PopStyleVar();
            ImGui.EndChild();
            return;
        }

        float rowH = ImGui.GetTextLineHeightWithSpacing();

        if (_scrollPending)
        {
            int targetRow = (int)(_baseAddr / BytesPerRow);
            ImGui.SetScrollY(targetRow * rowH - ImGui.GetWindowHeight() * 0.4f);
            _scrollPending = false;
        }

        float scrollY = ImGui.GetScrollY();
        int firstRow = Math.Max(0, (int)(scrollY / rowH) - 1);
        int visRows = (int)(ImGui.GetWindowHeight() / rowH) + 2;
        int lastRow = Math.Min(totalRows, firstRow + visRows);

        if (firstRow > 0)
            ImGui.Dummy(new Vector2(1f, firstRow * rowH));

        for (int row = firstRow; row < lastRow; row++)
            DrawRow(ram, row);

        float remaining = (totalRows - lastRow) * rowH;
        if (remaining > 0f)
            ImGui.Dummy(new Vector2(1f, remaining));

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    static readonly StringBuilder _asciiSb = new(BytesPerRow);

    void DrawRow(ReadOnlySpan<byte> ram, int row)
    {
        int baseOff = row * BytesPerRow;
        uint virtAddr = 0x80000000u + (uint)baseOff;
        var log = Runtime.RamLog;

        ImGuiEx.TextDisabled($"{virtAddr:X8}  ");
        ImGui.SameLine();

        _asciiSb.Clear();

        for (int col = 0; col < BytesPerRow; col++)
        {
            int idx = baseOff + col;
            byte b = idx < ram.Length ? ram[idx] : (byte)0;
            float heat = log.HeatAt(idx);

            if (heat > 0.01f)
            {
                var wc = log.WriteColor;
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(wc.X, wc.Y, wc.Z, 0.4f + heat * 0.6f));
                ImGui.Text($"{b:X2}");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.Text($"{b:X2}");
            }

            if (col < BytesPerRow - 1)
            {
                ImGui.SameLine();
                if (col == 7) ImGui.TextDisabled("  ");
                else ImGui.TextDisabled(" ");
                ImGui.SameLine();
            }

            _asciiSb.Append(b >= 32 && b < 127 ? (char)b : '.');
        }

        ImGui.SameLine();
        ImGuiEx.TextDisabled($"  {_asciiSb}");
    }
}
