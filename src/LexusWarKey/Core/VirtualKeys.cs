namespace LexusWarKey.Core;

/// <summary>Windows virtual-key codes used by the remapper and UI.</summary>
public static class VirtualKeys
{
    public const int Back = 0x08, Tab = 0x09, Enter = 0x0D, Escape = 0x1B, Space = 0x20;
    public const int Shift = 0x10, Control = 0x11, Alt = 0x12;
    public const int LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, LAlt = 0xA4, RAlt = 0xA5;

    public const int NumPad0 = 0x60, NumPad1 = 0x61, NumPad2 = 0x62, NumPad3 = 0x63, NumPad4 = 0x64;
    public const int NumPad5 = 0x65, NumPad6 = 0x66, NumPad7 = 0x67, NumPad8 = 0x68, NumPad9 = 0x69;

    public const int F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74, F6 = 0x75;
    public const int F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79, F11 = 0x7A, F12 = 0x7B;

    // Mouse triggers. Middle/X1/X2 are the standard VK codes; the wheel has no VK, so two unused
    // slots (0x0E/0x0F) stand in for a scroll notch up/down.
    public const int MouseMiddle = 0x04, MouseX1 = 0x05, MouseX2 = 0x06;
    public const int WheelUp = 0x0E, WheelDown = 0x0F;

    /// <summary>True for the mouse-based triggers, which the mouse hook feeds instead of the keyboard.</summary>
    public static bool IsMouse(int vk) => vk is MouseMiddle or MouseX1 or MouseX2 or WheelUp or WheelDown;

    /// <summary>Wheel notches arrive as a single event with no release, so they fire as one tap.</summary>
    public static bool IsWheel(int vk) => vk is WheelUp or WheelDown;

    private static readonly Dictionary<int, string> Names = BuildNames();

    public static string NameOf(int vk) => Names.TryGetValue(vk, out var n) ? n : $"0x{vk:X2}";

    private static Dictionary<int, string> BuildNames()
    {
        var map = new Dictionary<int, string>
        {
            [Back] = "Backspace", [Tab] = "Tab", [Enter] = "Enter", [Escape] = "Esc", [Space] = "Space",
            [Shift] = "Shift", [Control] = "Ctrl", [Alt] = "Alt",
            [LShift] = "LShift", [RShift] = "RShift", [LControl] = "LCtrl", [RControl] = "RCtrl",
            [LAlt] = "LAlt", [RAlt] = "RAlt",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/",
            [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
            [MouseMiddle] = "Mouse3", [MouseX1] = "Mouse4", [MouseX2] = "Mouse5",
            [WheelUp] = "Wheel ↑", [WheelDown] = "Wheel ↓",
        };
        for (var vk = 'A'; vk <= 'Z'; vk++) map[vk] = vk.ToString();
        for (var vk = '0'; vk <= '9'; vk++) map[vk] = vk.ToString();
        for (var i = 0; i <= 9; i++) map[NumPad0 + i] = $"Num{i}";
        for (var i = 0; i < 12; i++) map[F1 + i] = $"F{i + 1}";
        return map;
    }
}
