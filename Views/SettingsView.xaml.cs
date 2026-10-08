using System.Windows.Controls;
using System.Windows.Input;
using System.Text;
using System.Windows;
using AoE4OverlayCS.ViewModels;
using AoE4OverlayCS.Services;

namespace AoE4OverlayCS.Views
{
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += (_, _) => { RefreshBackgroundColorUi(); RefreshMacroUi(); };
        }

        private bool _isRecording = false;
        private bool _isRecordingPosition = false;
        private bool _isRecordingMacroTrigger = false;
        private bool _isRecordingMacroSequence = false;
        private int _lastFiniteRepeat = 1; // 取消无限循环时恢复的重复次数

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _isRecording = true;
            HotkeyButton.Content = "Press any key...";
        }

        private void HotkeyButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isRecording) return;
            e.Handled = true;
            
            var key = (e.Key == Key.System ? e.SystemKey : e.Key);
            
            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            var sb = new StringBuilder();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");
            
            if (key == Key.Back || key == Key.Delete || key == Key.Escape)
            {
                 if (key == Key.Escape) 
                 {
                    _isRecording = false;
                    HotkeyButton.GetBindingExpression(System.Windows.Controls.Button.ContentProperty)?.UpdateTarget();
                    return;
                 }
                 UpdateHotkey("");
                 _isRecording = false;
                 return;
            }

            sb.Append(key.ToString());
            UpdateHotkey(sb.ToString());
            _isRecording = false;
        }

        private void PositionHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _isRecordingPosition = true;
            PositionHotkeyButton.Content = "Press any key...";
        }

        private void PositionHotkeyButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isRecordingPosition) return;
            e.Handled = true;
            
            var key = (e.Key == Key.System ? e.SystemKey : e.Key);
            
            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            var sb = new StringBuilder();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");
            
            if (key == Key.Back || key == Key.Delete || key == Key.Escape)
            {
                 if (key == Key.Escape) 
                 {
                    _isRecordingPosition = false;
                    PositionHotkeyButton.GetBindingExpression(System.Windows.Controls.Button.ContentProperty)?.UpdateTarget();
                    return;
                 }
                 UpdatePositionHotkey("");
                 _isRecordingPosition = false;
                 return;
            }

            sb.Append(key.ToString());
            UpdatePositionHotkey(sb.ToString());
            _isRecordingPosition = false;
        }
        
        private void UpdateHotkey(string hotkey)
        {
             if (DataContext is MainViewModel vm)
             {
                 vm.Settings.OverlayHotkey = hotkey;
                 vm.UpdateHotkeyRegistration(); 
                 HotkeyButton.Content = string.IsNullOrEmpty(hotkey) ? "Click to set" : hotkey;
             }
        }

        private void UpdatePositionHotkey(string hotkey)
        {
             if (DataContext is MainViewModel vm)
             {
                 vm.Settings.OverlayPositionHotkey = hotkey;
                 vm.UpdateHotkeyRegistration(); 
                 PositionHotkeyButton.Content = string.IsNullOrEmpty(hotkey) ? "Click to set" : hotkey;
             }
        }

        private void SearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (DataContext is MainViewModel vm && vm.SearchPlayerCommand.CanExecute(null))
            {
                vm.SearchPlayerCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void SearchHistoryComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox comboBox && !comboBox.IsDropDownOpen)
            {
                comboBox.IsDropDownOpen = true;
            }
        }

        private void DeleteSearchHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not string query) return;
            if (DataContext is not MainViewModel vm) return;

            vm.RemoveSearchHistory(query);
            SearchHistoryComboBox.IsDropDownOpen = true;
            e.Handled = true;
        }

        private void BackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = ToDrawingColor(vm.Settings.OverlayBackgroundColor)
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                vm.Settings.OverlayBackgroundColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                RefreshBackgroundColorUi();
            }
        }

        private void RefreshBackgroundColorUi()
        {
            if (DataContext is not MainViewModel vm) return;

            var hex = vm.Settings.OverlayBackgroundColor;
            BackgroundColorSwatch.Background = new System.Windows.Media.SolidColorBrush(ParseColor(hex));
            BackgroundColorText.Text = hex;
        }

        private static System.Windows.Media.Color ParseColor(string? hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color color)
                    return color;
            }
            catch { }
            return System.Windows.Media.Colors.Black;
        }

        private static System.Drawing.Color ToDrawingColor(string? hex)
        {
            var color = ParseColor(hex);
            return System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        }

        // ---------------- 热键自动化（按键精灵式宏） ----------------

        /// <summary>初始化宏面板各控件显示（加载时与重置录制状态时调用）。</summary>
        private void RefreshMacroUi()
        {
            if (DataContext is not MainViewModel vm) return;

            MacroTriggerHotkeyButton.Content = string.IsNullOrEmpty(vm.Settings.MacroTriggerHotkey)
                ? "Click to set" : vm.Settings.MacroTriggerHotkey;
            MacroIntervalTextBox.Text = Math.Clamp(vm.Settings.MacroIntervalSeconds,
                MacroRunnerService.MinIntervalSeconds, MacroRunnerService.MaxIntervalSeconds).ToString();
            MacroIdleTextBox.Text = Math.Clamp(vm.Settings.MacroIdleWaitMs,
                0, MacroRunnerService.MaxIdleWaitMs).ToString();

            var infinite = vm.Settings.MacroRepeatCount <= 0;
            MacroInfiniteCheckBox.IsChecked = infinite;
            MacroRepeatTextBox.IsEnabled = !infinite;
            MacroRepeatTextBox.Text = infinite ? "∞" : vm.Settings.MacroRepeatCount.ToString();

            StopMacroSequenceRecording();
        }

        private void MacroEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // 开关变化直接生效：重新注册热键；关闭时 VM 会停止正在执行的宏
            if (DataContext is MainViewModel vm)
            {
                vm.UpdateHotkeyRegistration();
            }
        }

        private void MacroTriggerHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _isRecordingMacroTrigger = true;
            MacroTriggerHotkeyButton.Content = "Press any key...";
        }

        private void MacroTriggerHotkeyButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isRecordingMacroTrigger) return;
            e.Handled = true;

            var key = (e.Key == Key.System ? e.SystemKey : e.Key);

            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            var sb = new StringBuilder();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");

            if (key == Key.Back || key == Key.Delete || key == Key.Escape)
            {
                if (key == Key.Escape)
                {
                    _isRecordingMacroTrigger = false;
                    MacroTriggerHotkeyButton.GetBindingExpression(System.Windows.Controls.Button.ContentProperty)?.UpdateTarget();
                    RefreshMacroUi();
                    return;
                }
                UpdateMacroTriggerHotkey("");
                _isRecordingMacroTrigger = false;
                return;
            }

            sb.Append(key.ToString());
            UpdateMacroTriggerHotkey(sb.ToString());
            _isRecordingMacroTrigger = false;
        }

        private void UpdateMacroTriggerHotkey(string hotkey)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Settings.MacroTriggerHotkey = hotkey;
                vm.UpdateHotkeyRegistration(); // 注册路径内置冲突检测，冲突时状态栏提示
                MacroTriggerHotkeyButton.Content = string.IsNullOrEmpty(hotkey) ? "Click to set" : hotkey;
            }
        }

        private void MacroRecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecordingMacroSequence)
            {
                StopMacroSequenceRecording();
                return;
            }
            _isRecordingMacroSequence = true;
            MacroRecordButton.Content = "Press keys... (Esc to finish)";
            MacroSequenceTextBox.Focus();
        }

        private void StopMacroSequenceRecording()
        {
            _isRecordingMacroSequence = false;
            MacroRecordButton.Content = TryFindResource("Settings_MacroRecordButton") as string ?? "Record";
        }

        /// <summary>序列录制：录制期间捕获整个设置页按键，逐键追加；Esc 结束，Backspace 删除末位。</summary>
        private void SettingsView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isRecordingMacroSequence) return;
            // 其他热键按钮自身的录制流程优先，不在此捕获
            if (_isRecording || _isRecordingPosition || _isRecordingMacroTrigger) return;

            e.Handled = true;
            var key = (e.Key == Key.System ? e.SystemKey : e.Key);

            if (key == Key.Escape)
            {
                StopMacroSequenceRecording();
                return;
            }
            if (key == Key.Back)
            {
                RemoveLastMacroKey();
                return;
            }

            if (DataContext is not MainViewModel vm) return;
            if (IsModifierKey(key) || key == Key.LWin || key == Key.RWin)
            {
                // 修饰键等待后续主键组合，不单独记录
                return;
            }

            var sb = new StringBuilder();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");
            // 人性化显示：主键盘数字为 1-9，小键盘数字为 R1-R9（解析端对应还原）
            sb.Append(MacroRunnerService.KeyToDisplay(key));

            var steps = SplitSequenceTokens(vm);
            if (steps.Count >= MacroRunnerService.MaxKeys) return; // 达到上限忽略多余按键
            steps.Add(sb.ToString());
            vm.Settings.MacroSequence = string.Join("+", steps);
        }

        private static bool IsModifierKey(Key key) =>
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt;

        private static List<string> SplitSequenceTokens(MainViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(vm.Settings.MacroSequence))
            {
                return new List<string>();
            }
            return vm.Settings.MacroSequence
                .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        private void RemoveLastMacroKey()
        {
            if (DataContext is not MainViewModel vm) return;
            var steps = SplitSequenceTokens(vm);
            if (steps.Count == 0) return;
            steps.RemoveAt(steps.Count - 1);
            vm.Settings.MacroSequence = string.Join("+", steps);
        }

        private void MacroClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Settings.MacroSequence = "";
            }
        }

        private void MacroIntervalTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (int.TryParse(MacroIntervalTextBox.Text, out var v))
            {
                vm.Settings.MacroIntervalSeconds = Math.Clamp(v,
                    MacroRunnerService.MinIntervalSeconds, MacroRunnerService.MaxIntervalSeconds);
            }
            MacroIntervalTextBox.Text = vm.Settings.MacroIntervalSeconds.ToString();
        }

        private void MacroIdleTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (int.TryParse(MacroIdleTextBox.Text, out var v))
            {
                vm.Settings.MacroIdleWaitMs = Math.Clamp(v, 0, MacroRunnerService.MaxIdleWaitMs);
            }
            MacroIdleTextBox.Text = vm.Settings.MacroIdleWaitMs.ToString();
        }

        private void MacroRepeatTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (MacroRepeatTextBox.IsEnabled && int.TryParse(MacroRepeatTextBox.Text, out var v))
            {
                vm.Settings.MacroRepeatCount = Math.Clamp(v, 1, MacroRunnerService.MaxRepeatCount);
            }
            if (MacroRepeatTextBox.IsEnabled)
            {
                MacroRepeatTextBox.Text = vm.Settings.MacroRepeatCount.ToString();
            }
        }

        private void MacroInfiniteCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (MacroInfiniteCheckBox.IsChecked == true)
            {
                if (vm.Settings.MacroRepeatCount > 0)
                {
                    _lastFiniteRepeat = vm.Settings.MacroRepeatCount;
                }
                vm.Settings.MacroRepeatCount = 0; // 0 = 无限循环
                MacroRepeatTextBox.IsEnabled = false;
                MacroRepeatTextBox.Text = "∞";
            }
            else
            {
                vm.Settings.MacroRepeatCount = _lastFiniteRepeat;
                MacroRepeatTextBox.IsEnabled = true;
                MacroRepeatTextBox.Text = _lastFiniteRepeat.ToString();
            }
        }
    }
}
