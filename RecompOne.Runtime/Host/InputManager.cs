using Silk.NET.Input;
using Silk.NET.SDL;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Hardware;

namespace RecompOne.Runtime.Host;

internal static unsafe class InputManager
{
    static IKeyboard?_keyboard;
    static Sdl?_sdl;
    static GameController* _controller;

    const int AxisThreshold = 8000;
    const int LeftTrigger = 100;
    const int RightTrigger = 101;
    static bool _topBarToggle;
    static bool _fullscreenToggle;

    
    public static bool ConsumeTopBarToggle() { var v = _topBarToggle; _topBarToggle = false; return v; }
    public static bool ConsumeFullscreenToggle(){ var v = _fullscreenToggle; _fullscreenToggle = false; return v; }

    public static void Initialize(IInputContext input)
    {
        if (input.Keyboards.Count > 0)
        {
            _keyboard = input.Keyboards[0];
            _keyboard.KeyDown += OnKeyDown;
        }
        
        
        try
        {
            _sdl = Sdl.GetApi();
            _sdl.InitSubSystem(Sdl.InitGamecontroller);
            TryOpenController();
        }
        catch { _sdl = null; }
    }

    public static bool IsConnected => _controller != null;

    public static bool IsKeyDown(Key k) => _keyboard?.IsKeyPressed(k) ?? false;

    public static void Poll()
    {
        PollGamepadEvents();
        PollKeyboard();
        PollGamepad();
    }

    public static int? GetFirstPressedPadButton()
    {
        if (_sdl == null || _controller == null) return null;
        for (int b = 0; b < (int)GameControllerButton.Max; b++)
            if (_sdl.GameControllerGetButton(_controller, (GameControllerButton)b) != 0)
                return b;
        if (_sdl.GameControllerGetAxis(_controller, GameControllerAxis.Triggerleft)  > AxisThreshold) return LeftTrigger;
        if (_sdl.GameControllerGetAxis(_controller, GameControllerAxis.Triggerright) > AxisThreshold) return RightTrigger;
        return null;
    }

    public static void Shutdown()
    {
        if (_controller != null) { _sdl?.GameControllerClose(_controller); _controller = null; }
        _sdl?.QuitSubSystem(Sdl.InitGamecontroller);
        _sdl?.Dispose();
        _sdl = null;
    }

    static void PollGamepadEvents()
    {
        if (_sdl == null) return;
        Event ev;
        while (_sdl.PollEvent(&ev) != 0)
        {
            if (ev.Type == (uint)EventType.Controllerdeviceadded && _controller == null)
                TryOpenController();
            if (ev.Type == (uint)EventType.Controllerdeviceremoved)
            {
                _sdl.GameControllerClose(_controller);
                _controller = null;
            }
        }
    }

    static void PollKeyboard()
    {
        var kb = _keyboard;
        var cfg = ConfigManager.Game.Keys;
        if (kb == null) return;

        ushort s = 0xFFFF;
        void B(string keyName, ushort bit)
        {
            if (Enum.TryParse<Key>(keyName, out var k) && kb.IsKeyPressed(k))
                s &= (ushort)~bit;
        }

        B(cfg.Cross,    Controller.Cross);
        B(cfg.Circle,   Controller.Circle);
        B(cfg.Square,   Controller.Square);
        B(cfg.Triangle, Controller.Triangle);
        B(cfg.L1,       Controller.L1);
        B(cfg.R1,       Controller.R1);
        B(cfg.L2,       Controller.L2);
        B(cfg.R2,       Controller.R2);
        B(cfg.L3,       Controller.L3);
        B(cfg.R3,       Controller.R3);
        B(cfg.Start,    Controller.Start);
        B(cfg.Select,   Controller.Select);
        B(cfg.Up,       Controller.Up);
        B(cfg.Down,     Controller.Down);
        B(cfg.Left,     Controller.Left);
        B(cfg.Right,    Controller.Right);

        Controller.State = s;
    }

    static void PollGamepad()
    {
        if (_sdl == null || _controller == null) return;
        var pad = ConfigManager.Game.Pad;
        ushort s = Controller.State;

        void B(int binding, ushort bit)
        {
            if (binding == LeftTrigger)
            {
                if (_sdl.GameControllerGetAxis(_controller, GameControllerAxis.Triggerleft) > AxisThreshold)
                    s &= (ushort)~bit;
            }
            else if (binding == RightTrigger)
            {
                if (_sdl.GameControllerGetAxis(_controller, GameControllerAxis.Triggerright) > AxisThreshold)
                    s &= (ushort)~bit;
            }
            else if (_sdl.GameControllerGetButton(_controller, (GameControllerButton)binding) != 0)
                s &= (ushort)~bit;
        }

        B(pad.Cross,    Controller.Cross);
        B(pad.Circle,   Controller.Circle);
        B(pad.Square,   Controller.Square);
        B(pad.Triangle, Controller.Triangle);
        B(pad.L1,       Controller.L1);
        B(pad.R1,       Controller.R1);
        B(pad.L2,       Controller.L2);
        B(pad.R2,       Controller.R2);
        B(pad.L3,       Controller.L3);
        B(pad.R3,       Controller.R3);
        B(pad.Start,    Controller.Start);
        B(pad.Select,   Controller.Select);
        B(pad.Up,       Controller.Up);
        B(pad.Down,     Controller.Down);
        B(pad.Left,     Controller.Left);
        B(pad.Right,    Controller.Right);

        Controller.State = s;
        Controller.LeftX = AxisToByte(_sdl.GameControllerGetAxis(_controller, GameControllerAxis.Leftx));
        Controller.LeftY = AxisToByte(_sdl.GameControllerGetAxis(_controller, GameControllerAxis.Lefty));
        Controller.RightX = AxisToByte(_sdl.GameControllerGetAxis(_controller, GameControllerAxis.Rightx));
        Controller.RightY = AxisToByte(_sdl.GameControllerGetAxis(_controller, GameControllerAxis.Righty));
    }

    static byte AxisToByte(short axis)
    {
        float f = Math.Clamp(axis * 1.3f / 32768.0f, -1.0f, 1.0f);
        return (byte)Math.Clamp((int)MathF.Round((f + 1.0f) * 127.5f), 0, 255);
    }

    public static void SetRumble(byte large, byte small)
    {
        if (_sdl == null || _controller == null) return;
        ushort lo = (ushort)(large * 257);
        ushort hi = small != 0 ? (ushort)65535 : (ushort)0;
        uint duration = large == 0 && small == 0 ? 0u : 500u;
        _sdl.GameControllerRumble(_controller, lo, hi, duration);
    }

    static void OnKeyDown(IKeyboard kb, Key key, int _)
    {
        if (key == Key.F1)  _topBarToggle = true;
        if (key == Key.F11) _fullscreenToggle = true;
    }

    static void TryOpenController()
    {
        if (_sdl == null) return;
        int n = _sdl.NumJoysticks();
        for (int i = 0; i < n; i++)
        {
            if (_sdl.IsGameController(i) != SdlBool.True) continue;
            _controller = _sdl.GameControllerOpen(i);
            if (_controller != null) break;
        }
    }
}
