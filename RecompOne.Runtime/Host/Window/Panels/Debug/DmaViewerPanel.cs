using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

internal sealed class DmaViewerPanel : IPanel
{
    public string Name => "DMA Viewer";
    public bool IsOpen { get; set; }

    static readonly string[] ChannelNames = ["MDECin", "MDECout", "GPU", "CDROM", "SPU", "PIO", "OTC"];
    static readonly string[] SyncModes = ["Burst", "Slice", "Linked", "???"];
    //needs some improvements
    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(580, 260), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open))
        {
            IsOpen = open; ImGui.End(); 
            return;
        }

        var mem = Runtime.Mem;
        if (mem == null)
        {
            ImGui.TextDisabled("No memory"); ImGui.End(); IsOpen = open;
            return;
        }
        
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY;

        if (ImGui.BeginTable("dma", 7, tableFlags))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Ch",ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Active", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Dir", ImGuiTableColumnFlags.WidthFixed, 58);
            ImGui.TableSetupColumn("Sync", ImGuiTableColumnFlags.WidthFixed, 58);
            ImGui.TableSetupColumn("MADR", ImGuiTableColumnFlags.WidthFixed, 88);
            ImGui.TableSetupColumn("BCR", ImGuiTableColumnFlags.WidthFixed, 88);
            ImGui.TableSetupColumn("CHCR", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            for (int ch = 0; ch < 7; ch++)
            {
                uint baseAddr = 0x1F801080u + (uint)(ch * 0x10);
                uint madr = SafeRead(mem, baseAddr);
                uint bcr = SafeRead(mem, baseAddr + 4);
                uint chcr = SafeRead(mem, baseAddr + 8);

                bool active = (chcr & 0x01000000u) != 0;
                bool toRam = (chcr & 0x00000001u) == 0;
                int syncMode = (int)((chcr >> 9) & 3);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                if (active) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1f, 0.4f, 1f));
                ImGui.TextUnformatted(ChannelNames[ch]);
                if (active) ImGui.PopStyleColor();

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(active ? "yes" : "—");
                ImGui.TableSetColumnIndex(2);
                ImGui.TextDisabled(ch == 6 ? "OTC" : (toRam ? "dev→RAM" : "RAM→dev"));
                ImGui.TableSetColumnIndex(3);
                ImGui.TextDisabled(SyncModes[syncMode]);

                ImGui.TableSetColumnIndex(4);
                ImGui.Text($"{madr:X8}");
                ImGui.TableSetColumnIndex(5);
                ImGui.Text($"{bcr:X8}");
                ImGui.TableSetColumnIndex(6);
                ImGui.Text($"{chcr:X8}");
            }

            ImGui.EndTable();
        }

        uint dicr = SafeRead(mem, 0x1F8010F4u);
        ImGui.Spacing();
        ImGui.TextDisabled("DICR"); ImGui.SameLine();
        ImGui.Text($"{dicr:X8}");
        ImGui.SameLine(); ImGui.Spacing(); ImGui.SameLine();
        ImGui.TextDisabled("Master IRQ"); ImGui.SameLine();
        ImGui.Text((dicr & 0x80000000u) != 0 ? "SET" : "—");

        IsOpen = open;
        ImGui.End();
    }

    static uint SafeRead(Memory.IMemory mem, uint addr)
    {
        try { return mem.ReadU32(addr); }
        catch { return 0; }
    }
}
