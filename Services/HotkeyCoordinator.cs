using AoE4OverlayCS.Models;
using NHotkey.Wpf;
using System.IO;
using System.Text;
using System.Windows.Input;

namespace AoE4OverlayCS.Services
{
    /// <summary>
    /// 统一管理覆盖层显示/位置与宏触发热键：
    /// 解析热键字符串、注册 NHotkey、注册失败时降级为全局钩子，并检测宏触发热键与覆盖层热键的冲突。
    /// 必须在 UI 线程调用（NHotkey 依赖 WPF 组件派发）。
    /// </summary>
    public sealed class HotkeyCoordinator : IDisposable
    {
        private readonly GlobalHotkeyService _overlayHook = new();
        private readonly GlobalHotkeyService _positionHook = new();
        private readonly GlobalHotkeyService _macroHook = new();

        /// <summary>最近一次 Update 后，宏触发热键是否与覆盖层热键冲突（冲突时不注册触发热键）。</summary>
        public bool HasMacroConflict { get; private set; }

        /// <summary>按当前设置重新注册全部热键；注册失败的热键自动降级为全局钩子实现。</summary>
        public void Update(AppSettings settings, Action toggleOverlay, Action changeOverlayPosition, Action toggleMacro)
        {
            try
            {
                RegisterCore("ToggleOverlay", _overlayHook, settings.OverlayHotkey, "overlay", toggleOverlay);
                RegisterCore("ToggleOverlayPosition", _positionHook, settings.OverlayPositionHotkey, "position", changeOverlayPosition);
                RegisterMacroHotkey(settings, toggleMacro);
            }
            catch { /* Ignore invalid hotkeys */ }
        }

        /// <summary>宏触发热键分支：需先通过冲突检测才注册；总开关关闭时仅清掉旧注册。</summary>
        private void RegisterMacroHotkey(AppSettings settings, Action toggleMacro)
        {
            HasMacroConflict = false;
            HotkeyManager.Current.Remove("ToggleMacro");
            _macroHook.Stop();
            if (!settings.MacroEnabled || string.IsNullOrEmpty(settings.MacroTriggerHotkey)) return;
            if (HasConflict(settings))
            {
                HasMacroConflict = true;
                return;
            }
            RegisterCore("ToggleMacro", _macroHook, settings.MacroTriggerHotkey, "macro", toggleMacro);
        }

        /// <summary>注册单个热键：优先 NHotkey，失败降级为全局钩子；空字符串或解析失败时只清掉旧注册。</summary>
        private static void RegisterCore(string name, GlobalHotkeyService hook, string? hotkey, string label, Action action)
        {
            HotkeyManager.Current.Remove(name);
            hook.Stop();
            if (string.IsNullOrEmpty(hotkey)) return;
            if (!TryParseHotkey(hotkey, out var key, out var modifiers) || key == Key.None) return;

            try
            {
                HotkeyManager.Current.AddOrReplace(name, key, modifiers, (s, e) =>
                {
                    Log(hotkey, label, "pressed");
                    action();
                });
                Log(hotkey, label, "registered");
            }
            catch (Exception ex)
            {
                Log(hotkey, label, $"register-failed {ex}");
                hook.Configure(hotkey, () =>
                {
                    Log(hotkey, label, "hook-pressed");
                    action();
                });
                hook.Start();
            }
        }

        private static void Log(string hotkey, string label, string @event)
        {
            try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} {@event} {label} {hotkey}{Environment.NewLine}"); }
            catch { }
        }

        /// <summary>宏触发热键是否与覆盖层显示/位置热键冲突。</summary>
        public static bool HasConflict(AppSettings settings)
        {
            var trigger = NormalizeHotkeyString(settings.MacroTriggerHotkey);
            if (trigger.Length == 0) return false;
            return trigger == NormalizeHotkeyString(settings.OverlayHotkey)
                || trigger == NormalizeHotkeyString(settings.OverlayPositionHotkey);
        }

        /// <summary>解析热键字符串为（主键, 修饰键）；无法解析出主键时返回 false。</summary>
        public static bool TryParseHotkey(string hotkey, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;
            if (string.IsNullOrWhiteSpace(hotkey)) return false;

            foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
                if (Enum.TryParse(part, true, out Key k))
                {
                    if (k == Key.LeftCtrl || k == Key.RightCtrl) { modifiers |= ModifierKeys.Control; continue; }
                    if (k == Key.LeftShift || k == Key.RightShift) { modifiers |= ModifierKeys.Shift; continue; }
                    if (k == Key.LeftAlt || k == Key.RightAlt) { modifiers |= ModifierKeys.Alt; continue; }
                    key = k;
                }
            }
            return key != Key.None;
        }

        /// <summary>热键字符串归一化为 "Ctrl+Shift+Alt+主键" 形式，用于冲突比较。</summary>
        public static string NormalizeHotkeyString(string? hotkey)
        {
            if (string.IsNullOrWhiteSpace(hotkey)) return "";
            if (!TryParseHotkey(hotkey, out var key, out var modifiers) || key == Key.None) return "";

            var sb = new StringBuilder();
            if ((modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");
            sb.Append(key.ToString());
            return sb.ToString();
        }

        public void Dispose()
        {
            _overlayHook.Dispose();
            _positionHook.Dispose();
            _macroHook.Dispose();
        }
    }
}
