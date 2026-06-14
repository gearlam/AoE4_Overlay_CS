using System.Windows.Controls;
using System.Windows.Input;
using System.Text;
using System.Windows;
using AoE4OverlayCS.ViewModels;

namespace AoE4OverlayCS.Views
{
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private bool _isRecording = false;
        private bool _isRecordingPosition = false;

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
    }
}
