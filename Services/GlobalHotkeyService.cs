using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AoE4OverlayCS.Services
{
    public sealed class GlobalHotkeyService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;

        private Key _triggerKey = Key.None;
        private ModifierKeys _triggerModifiers = ModifierKeys.None;
        private Action? _onTriggered;

        private bool _isTriggerKeyDown;

        public bool IsActive => _hookId != IntPtr.Zero;

        public void Configure(string hotkey, Action onTriggered)
        {
            _onTriggered = onTriggered;
            ParseHotkey(hotkey, out _triggerKey, out _triggerModifiers);
        }

        public void Start()
        {
            Stop();

            if (_onTriggered == null) return;
            if (_triggerKey == Key.None) return;

            _proc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);
        }

        public void Stop()
        {
            _isTriggerKeyDown = false;
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _proc = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private static void ParseHotkey(string hotkey, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;

            if (string.IsNullOrWhiteSpace(hotkey)) return;

            var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Control;
                    continue;
                }
                if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Shift;
                    continue;
                }
                if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Alt;
                    continue;
                }

                if (Enum.TryParse(part, true, out Key parsed))
                {
                    key = parsed;
                }
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _onTriggered != null && _triggerKey != Key.None)
            {
                var msg = wParam.ToInt32();
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                int vkCode = Marshal.ReadInt32(lParam);
                var key = KeyInterop.KeyFromVirtualKey(vkCode);

                if (key == _triggerKey)
                {
                    if (isDown && !_isTriggerKeyDown)
                    {
                        _isTriggerKeyDown = true;
                        var mods = GetCurrentModifiers();
                        if (mods == _triggerModifiers)
                        {
                            _onTriggered();
                        }
                    }
                    else if (isUp)
                    {
                        _isTriggerKeyDown = false;
                    }
                }
                else if (isUp && _isTriggerKeyDown)
                {
                    _isTriggerKeyDown = false;
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static ModifierKeys GetCurrentModifiers()
        {
            ModifierKeys mods = ModifierKeys.None;
            if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) mods |= ModifierKeys.Control;
            if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) mods |= ModifierKeys.Shift;
            if ((GetKeyState(VK_MENU) & 0x8000) != 0) mods |= ModifierKeys.Alt;
            return mods;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);
    }
}
