using AoE4OverlayCS.Models;
using AoE4OverlayCS.Services;
using System.Windows.Input;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static void Main()
    {
        ParseBasics();
        ParseModifierAliases();
        ParseRejections();
        Normalize();
        ConflictDetection();
        Console.WriteLine($"Passed {_checks} hotkey checks (no real hotkeys registered).");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }

    private static void ParseBasics()
    {
        Check(HotkeyCoordinator.TryParseHotkey("F9", out var key, out var mods) && key == Key.F9 && mods == ModifierKeys.None,
            "plain function key parses without modifiers");
        Check(HotkeyCoordinator.TryParseHotkey("Ctrl+Shift+F9", out key, out mods) && key == Key.F9
            && mods == (ModifierKeys.Control | ModifierKeys.Shift),
            "Ctrl+Shift combination parses with correct modifiers");
        Check(HotkeyCoordinator.TryParseHotkey("Ctrl+Alt+D1", out key, out mods) && key == Key.D1
            && mods == (ModifierKeys.Control | ModifierKeys.Alt),
            "number row key with Ctrl+Alt parses");
    }

    private static void ParseModifierAliases()
    {
        // 修饰键写左键名/别名也应归一化
        Check(HotkeyCoordinator.TryParseHotkey("LeftCtrl+A", out var key, out var mods) && key == Key.A
            && (mods & ModifierKeys.Control) != 0,
            "LeftCtrl key name counts as Control modifier");
        Check(HotkeyCoordinator.TryParseHotkey("Control+B", out key, out mods) && key == Key.B
            && (mods & ModifierKeys.Control) != 0,
            "Control alias is accepted like Ctrl");
        Check(HotkeyCoordinator.TryParseHotkey("F2+shift", out key, out mods) && key == Key.F2
            && (mods & ModifierKeys.Shift) != 0,
            "case-insensitive trailing Shift is accepted");
        Check(HotkeyCoordinator.TryParseHotkey("Ctrl + F3", out key, out _) && key == Key.F3,
            "whitespace around plus is trimmed");
    }

    private static void ParseRejections()
    {
        Check(!HotkeyCoordinator.TryParseHotkey("", out _, out _), "empty string is rejected");
        Check(!HotkeyCoordinator.TryParseHotkey("   ", out _, out _), "whitespace-only string is rejected");
        Check(!HotkeyCoordinator.TryParseHotkey("Ctrl+Shift", out _, out _), "modifiers without a main key are rejected");
        Check(!HotkeyCoordinator.TryParseHotkey("Ctrl+Ctrl", out _, out _), "repeated modifiers without a main key are rejected");
        Check(!HotkeyCoordinator.TryParseHotkey("NotAKey", out _, out _), "unknown key name is rejected");
    }

    private static void Normalize()
    {
        Check(HotkeyCoordinator.NormalizeHotkeyString(null) == "", "null normalizes to empty");
        Check(HotkeyCoordinator.NormalizeHotkeyString("") == "", "empty normalizes to empty");
        Check(HotkeyCoordinator.NormalizeHotkeyString("Ctrl") == "", "modifiers only normalize to empty");
        Check(HotkeyCoordinator.NormalizeHotkeyString("Ctrl+Shift+Alt+F9") == "Ctrl+Shift+Alt+F9",
            "already normalized string is stable");
        Check(HotkeyCoordinator.NormalizeHotkeyString("F9+ctrl+SHIFT") == "Ctrl+Shift+F9",
            "modifier order and casing are canonicalized");
        Check(HotkeyCoordinator.NormalizeHotkeyString("shift+a") == "Shift+A",
            "single modifier plus key is canonicalized");
    }

    private static void ConflictDetection()
    {
        static AppSettings Settings(string overlay, string position, string trigger) => new()
        {
            OverlayHotkey = overlay,
            OverlayPositionHotkey = position,
            MacroTriggerHotkey = trigger,
            MacroEnabled = true
        };

        Check(HotkeyCoordinator.HasConflict(Settings("Ctrl+F8", "Ctrl+F9", "Ctrl+F8")),
            "trigger equal to overlay hotkey conflicts");
        Check(HotkeyCoordinator.HasConflict(Settings("Ctrl+F8", "Ctrl+F9", "ctrl+f9")),
            "trigger equal to position hotkey conflicts regardless of case");
        Check(HotkeyCoordinator.HasConflict(Settings("Ctrl+F8", "Ctrl+F9", "F9+ctrl")),
            "conflict detection ignores modifier order");
        Check(!HotkeyCoordinator.HasConflict(Settings("Ctrl+F8", "Ctrl+F9", "Ctrl+F10")),
            "distinct trigger does not conflict");
        Check(!HotkeyCoordinator.HasConflict(Settings("Ctrl+F8", "Ctrl+F9", "")),
            "empty trigger never conflicts");
        Check(!HotkeyCoordinator.HasConflict(new AppSettings()),
            "default settings have no conflict");
    }
}
