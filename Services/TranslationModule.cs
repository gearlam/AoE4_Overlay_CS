using AoE4OverlayCS.Models;
using AoE4OverlayCS.ViewModels;
using AoE4OverlayCS.Views;
using NHotkey.Wpf;
using System.Windows;
using System.Windows.Input;

namespace AoE4OverlayCS.Services
{
    // Owns translation UI and hotkeys; no dependency on match polling or WebSocket output.
    public sealed class TranslationModule
    {
        private readonly AppSettings _appSettings;
        private readonly GlobalHotkeyService _fallback = new();
        private readonly TranslationViewModel _model;
        private TranslationSettings _appliedHotkey;
        private bool _stopping;
        private TranslationWindow? _window;
        private TranslationSettingsWindow? _settingsWindow;
        public TranslationModule(AppSettings settings, TranslationStore? store = null)
        {
            _appSettings = settings;
            _model = new TranslationViewModel(() => settings.Game, Register, store);
            _appliedHotkey = SnapshotHotkey();
            _model.SettingsSaved += () =>
            {
                _appliedHotkey = SnapshotHotkey();
                if (_settingsWindow == null) RefreshHotkey();
            };
        }
        private static string T(string key) => System.Windows.Application.Current.TryFindResource(key)?.ToString() ?? key;
        private TranslationSettings SnapshotHotkey() => new() { Enabled = _model.Settings.Enabled, Hotkey = _model.Settings.Hotkey };
        public void RefreshHotkey()
        {
            if (_settingsWindow != null) { StopHotkey(); return; }
            var error = Register(_appliedHotkey);
            if (error != null) _model.Status = error;
        }
        public void StopHotkey()
        {
            HotkeyManager.Current.Remove("Translation");
            _fallback.Stop();
        }
        private string? Register(TranslationSettings settings)
        {
            if (!settings.Enabled) { StopHotkey(); return null; }
            var tokens = settings.Hotkey.Split('+', StringSplitOptions.TrimEntries);
            ModifierKeys modifiers = ModifierKeys.None;
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                switch (tokens[i].ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= ModifierKeys.Control; break;
                    case "shift": modifiers |= ModifierKeys.Shift; break;
                    case "alt": modifiers |= ModifierKeys.Alt; break;
                    default: return T("Tr_InvalidHotkey");
                }
            }
            if (!Enum.TryParse<Key>(tokens[^1], true, out var key) || !Enum.IsDefined(key) ||
                key == Key.None || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin ||
                (modifiers == ModifierKeys.None && (key < Key.F1 || key > Key.F24)))
                return T("Tr_InvalidHotkey");
            var normalized = MainViewModel.NormalizeHotkeyString(settings.Hotkey);
            var conflicts = new[] { _appSettings.OverlayHotkey, _appSettings.OverlayPositionHotkey,
                _appSettings.MacroEnabled ? _appSettings.MacroTriggerHotkey : "" };
            if (conflicts.Any(h => MainViewModel.NormalizeHotkeyString(h) == normalized))
            {
                StopHotkey();
                return T("Tr_Conflict");
            }
            StopHotkey();
            if (_settingsWindow != null) return null; // Validate drafts, but never arm a hotkey while settings are open.
            try
            {
                HotkeyManager.Current.AddOrReplace("Translation", key, modifiers, (_, e) => { e.Handled = true; OnHotkey(); });
            }
            catch
            {
                _fallback.Configure(normalized, () => System.Windows.Application.Current.Dispatcher.BeginInvoke(OnHotkey));
                _fallback.Start();
                if (!_fallback.IsActive) return T("Tr_HotkeyFailed");
            }
            return null;
        }
        private void OnHotkey()
        {
            // Also ignore callbacks queued before the settings window suspended registration.
            if (_settingsWindow == null) Toggle();
        }
        public void Toggle()
        {
            if (_window?.IsVisible == true) { _window.Hide(); return; }
            if (_window == null)
            {
                _window = new TranslationWindow(_model, OpenSettings);
                _window.Closed += (_, _) => _window = null;
            }
            _window.WindowState = WindowState.Normal;
            _window.Show();
            _window.Activate();
        }
        public void OpenSettings()
        {
            if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
            StopHotkey();
            _settingsWindow = new TranslationSettingsWindow(_model, () =>
            {
                if (_window?.IsVisible != true) Toggle();
                else _window.Activate();
            });
            _settingsWindow.Owner = _window ?? System.Windows.Application.Current.MainWindow;
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                if (!_stopping) RefreshHotkey(); // Unsaved hotkey edits must not replace the last applied binding.
            };
            _settingsWindow.Show();
        }
        public void Stop()
        {
            _stopping = true;
            try
            {
                StopHotkey();
                _model.Cancel();
                _settingsWindow?.Close();
                _window?.Close();
            }
            finally { _stopping = false; }
        }
    }
}
