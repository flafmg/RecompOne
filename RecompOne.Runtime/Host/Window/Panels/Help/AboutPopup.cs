using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

internal sealed class AboutPopup : IPanel
{
    public string Name => "About";
    public bool IsOpen { get; set; }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(280, 100), ImGuiCond.Always);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));

        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        var center = ImGui.GetContentRegionAvail().X * 0.5f;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + center - ImGui.CalcTextSize(AppVersion.Name).X * 0.5f);
        ImGui.TextUnformatted(AppVersion.Name);
        ImGui.Spacing();

        var ver = $"Version: {AppVersion.Version}";
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + center - ImGui.CalcTextSize(ver).X * 0.5f);
        ImGuiEx.TextDisabled(ver);

        var contributors = "";
            
        IsOpen = open;
        ImGui.End();
    }
}
