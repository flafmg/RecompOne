using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;

namespace RecompOne.Runtime.Config;

static file class PanelDefaults
{
    public static bool IsOpenByDefault(IPanel p) => p.Name == "Output";
}

internal static class ConfigManager
{
    static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    const string GameConfigPath = "settings.json";
    const string InterfaceFile = "interface.ini";

    public static GameConfig Game { get; private set; } = new();
    public static ViewConfig  View { get; private set; } = new();

    static string? _pendingImGuiIni;

    public static void Load()
    {
        if (File.Exists(GameConfigPath))
        {
            try { Game = JsonSerializer.Deserialize<GameConfig>(File.ReadAllText(GameConfigPath), _opts) ?? new(); }
            catch { Game = new(); }
        }
        else
        {
            SaveGame();
        }

        if (File.Exists(InterfaceFile))
        {
            var (view, imguiIni) = ParseInterfaceFile(File.ReadAllText(InterfaceFile));
            View = view;
            _pendingImGuiIni = imguiIni;
        }
    }

    
    public static bool ApplyImGuiLayout()
    {
        if (_pendingImGuiIni == null) return false;
        ImGui.LoadIniSettingsFromMemory(_pendingImGuiIni);
        _pendingImGuiIni = null;
        return true;
    }

    public static void ApplyViewToPanels(IReadOnlyList<IPanel> panels)
    {
        foreach (var p in panels)
        {
            if (View.Panels.TryGetValue(p.Name, out var state))
                p.IsOpen = state.Open;
        }
    }

    public static void SaveView(IReadOnlyList<IPanel> panels)
    {
        foreach (var p in panels)
            View.Panels[p.Name] = new PanelState { Open = p.IsOpen };

        var imguiIni = ImGui.SaveIniSettingsToMemory();
        var sb = new StringBuilder();
        sb.AppendLine("[RecompOne]");
        sb.AppendLine($"HideTopBar={View.HideTopBar}");
        foreach (var (name, state) in View.Panels)
            sb.AppendLine($"Panels.{name}={state.Open}");
        sb.AppendLine();
        sb.Append(imguiIni);
        File.WriteAllText(InterfaceFile, sb.ToString());
    }

    public static void ResetView(IReadOnlyList<IPanel> panels)
    {
        View = new();
        foreach (var p in panels)
            p.IsOpen = PanelDefaults.IsOpenByDefault(p);
        ImGui.LoadIniSettingsFromMemory("");
        SaveView(panels);
    }

    public static void SaveGame()
    {
        File.WriteAllText(GameConfigPath, JsonSerializer.Serialize(Game, _opts));
    }

    static (ViewConfig view, string imguiIni) ParseInterfaceFile(string content)
    {
        var view = new ViewConfig();
        var imguiLines = new List<string>();
        bool inRecompOne = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line == "[RecompOne]")
            {
                inRecompOne = true;
                continue;
            }

            if (line.StartsWith('['))
                inRecompOne = false;

            if (inRecompOne)
            {
                if (line.StartsWith("HideTopBar="))
                    view.HideTopBar = bool.TryParse(line[11..], out var b) && b;
                else if (line.StartsWith("Panels."))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        var panelName = line[7..eq];
                        var open = bool.TryParse(line[(eq + 1)..], out var b) && b;
                        view.Panels[panelName] = new PanelState { Open = open };
                    }
                }
            }
            else
            {
                imguiLines.Add(line);
            }
        }

        return (view, string.Join('\n', imguiLines));
    }
}
