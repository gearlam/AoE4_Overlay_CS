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

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _isRecording = true;
            HotkeyButton.Content = "Press any key...";
        }

        private void HotkeyButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isRecording) return;
            e.Handled = true;
            
            // Handle modifier keys
            var key = (e.Key == Key.System ? e.SystemKey : e.Key);
            
            // Allow modifiers as standalone only if user wants? Usually we wait for a non-modifier.
            // But we filter them out from being the "trigger" key.
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
                 // Clear hotkey or cancel
                 if (key == Key.Escape) 
                 {
                    _isRecording = false;
                    // Re-bind to show current value
                    HotkeyButton.GetBindingExpression(System.Windows.Controls.Button.ContentProperty)?.UpdateTarget();
                    return;
                 }
                 UpdateHotkey("");
                 _isRecording = false;
                 return;
            }

            // Fix for F-keys and others
            sb.Append(key.ToString());
            UpdateHotkey(sb.ToString());
            _isRecording = false;
        }
        
        private void UpdateHotkey(string hotkey)
        {
             // Update ViewModel
             if (DataContext is MainViewModel vm)
             {
                 vm.Settings.OverlayHotkey = hotkey;
                 vm.UpdateHotkeyRegistration(); 
                 // Force button update
                 HotkeyButton.Content = string.IsNullOrEmpty(hotkey) ? "Click to set" : hotkey;
             }
        }
    }
}
