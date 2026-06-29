
namespace RecompOne.Runtime.Config;

public class KeyBindings
{
    public string Cross { get; set; } = "Z";
    public string Circle { get; set; } = "X";
    public string Square { get; set; } = "A";
    public string Triangle { get; set; } = "S";
    public string L1 { get; set; } = "Q";
    public string R1 { get; set; } = "W";
    public string L2 { get; set; } = "E";
    public string R2 { get; set; } = "R";
    public string L3 { get; set; } = "F";
    public string R3 { get; set; } = "G";
    public string Start { get; set; } = "Enter";
    public string Select { get; set; } = "ShiftRight";
    public string Up { get; set; } = "Up";
    public string Down { get; set; } = "Down";
    public string Left { get; set; } = "Left";
    public string Right { get; set; } = "Right";
}

public class GamepadBindings
{
    public int Cross { get; set; } = 0;
    public int Circle { get; set; } = 1;
    public int Square { get; set; } = 2;
    public int Triangle { get; set; } = 3;
    public int L1 { get; set; } = 9;
    public int R1 { get; set; } = 10;
    public int L2 { get; set; } = 100;
    public int R2 { get; set; } = 101;
    public int L3 { get; set; } = 7;
    public int R3 { get; set; } = 8;
    public int Start { get; set; } = 6;
    public int Select { get; set; } = 4;
    public int Up { get; set; } = 11;
    public int Down { get; set; } = 12;
    public int Left { get; set; } = 13;
    public int Right { get; set; } = 14;
}

public class GameConfig
{
    public string CdPath { get; set; } = "";
    public bool Fullscreen { get; set; } = false;
    public KeyBindings Keys { get; set; } = new();
    public GamepadBindings Pad { get; set; } = new();
    public List<string> ActiveMods { get; set; } = [];
}
