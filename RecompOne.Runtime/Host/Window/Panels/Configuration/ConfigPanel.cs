using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class ConfigPanel : IPanel
{
    public string Name => "Input";
    public bool IsOpen { get; set; } = false;

    static readonly (string Label, Func<KeyBindings, string> GetKey, Action<KeyBindings, string> SetKey, Func<GamepadBindings, int> GetPad, Action<GamepadBindings, int> SetPad)[] _rows =
    [
        ("Cross", b => b.Cross, (b,v) => b.Cross = v, p => p.Cross, (p,v) => p.Cross = v),
        ("Circle", b => b.Circle, (b,v) => b.Circle = v, p => p.Circle, (p,v) => p.Circle = v),
        ("Square", b => b.Square, (b,v) => b.Square = v, p => p.Square, (p,v) => p.Square = v),
        ("Triangle", b => b.Triangle, (b,v) => b.Triangle = v, p => p.Triangle, (p,v) => p.Triangle = v),
        ("L1", b => b.L1, (b,v) => b.L1 = v, p => p.L1, (p,v) => p.L1 = v),
        ("R1", b => b.R1, (b,v) => b.R1 = v, p => p.R1, (p,v) => p.R1 = v),
        ("L2", b => b.L2, (b,v) => b.L2 = v, p => p.L2, (p,v) => p.L2 = v),
        ("R2", b => b.R2, (b,v) => b.R2 = v, p => p.R2, (p,v) => p.R2 = v),
        ("L3", b => b.L3, (b,v) => b.L3 = v, p => p.L3, (p,v) => p.L3 = v),
        ("R3", b => b.R3, (b,v) => b.R3 = v, p => p.R3, (p,v) => p.R3 = v),
        ("Start", b => b.Start, (b,v) => b.Start = v, p => p.Start, (p,v) => p.Start = v),
        ("Select", b => b.Select, (b,v) => b.Select = v, p => p.Select, (p,v) => p.Select = v),
        ("Up", b => b.Up, (b,v) => b.Up = v, p => p.Up, (p,v) => p.Up = v),
        ("Down", b => b.Down, (b,v) => b.Down = v, p => p.Down, (p,v) => p.Down = v),
        ("Left", b => b.Left, (b,v) => b.Left = v, p => p.Left, (p,v) => p.Left = v),
        ("Right", b => b.Right, (b,v) => b.Right = v, p => p.Right, (p,v) => p.Right = v),
    ];

    int _remapRow = -1;
    bool _remapIsKey = true;

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(480, 420), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        if (ImGui.BeginTable("bindings", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Button",   ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Keyboard", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Gamepad",  ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            var keys = ConfigManager.Game.Keys;
            var pad = ConfigManager.Game.Pad;

            for (int i = 0; i < _rows.Length; i++)
            {
                var (label, getKey, setKey, getPad, setPad) = _rows[i];
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(label);

                ImGui.TableSetColumnIndex(1);
                bool awaitKey = _remapRow == i && _remapIsKey;
                if (ImGui.Button(awaitKey ? "[press key...]" : $"{getKey(keys)}##k{i}", new Vector2(-1, 0)))
                {
                    _remapRow = i; _remapIsKey = true;
                }
                if (awaitKey)
                {
                    var p = GetPressedKey();
                    if (p != null) { setKey(keys, p); _remapRow = -1; ConfigManager.SaveGame(); }
                }

                ImGui.TableSetColumnIndex(2);
                bool awaitPad = _remapRow == i && !_remapIsKey;
                if (ImGui.Button(awaitPad ? "[press button...]" : $"{PadLabel(getPad(pad))}##p{i}", new Vector2(-1, 0)))
                {
                    _remapRow = i; _remapIsKey = false;
                }
                if (awaitPad)
                {
                    var p = InputManager.GetFirstPressedPadButton();
                    if (p.HasValue)
                    {
                        setPad(pad, p.Value); _remapRow = -1; ConfigManager.SaveGame();
                    }
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        if (ImGui.Button("Reset to Defaults"))
        {
            ConfigManager.Game.Keys = new KeyBindings();
            ConfigManager.Game.Pad = new GamepadBindings();
            ConfigManager.SaveGame();
        }

        IsOpen = open;
        ImGui.End();
    }

    static string? GetPressedKey()
    {
        foreach (Key k in Enum.GetValues<Key>())
        {
            if (k is Key.Unknown or Key.Menu) continue;
            if (InputManager.IsKeyDown(k)) return k.ToString();
        }
        return null;
    }

    static string PadLabel(int b) => b switch
    {
        0 => "Cross (A)",
        1 => "Circle (B)",
        2 => "Square (X)",
        3 => "Triangle (Y)",
        4 => "Select (Back)",
        5 => "Guide",
        6 => "Start",
        7 => "L3 (LStick)",
        8 => "R3 (RStick)",
        9 => "L1 (LBumper)",
        10 => "R1 (RBumper)",
        11 => "D-Up",
        12 => "D-Down",
        13 => "D-Left",
        14 => "D-Right",
        100 => "L2 (LTrigger)",
        101 => "R2 (RTrigger)",
        _ => $"Btn {b}",
    };
}
