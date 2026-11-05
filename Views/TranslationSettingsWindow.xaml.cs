using AoE4OverlayCS.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace AoE4OverlayCS.Views
{
    public partial class TranslationSettingsWindow : Window
    {
        private readonly Action _open;
        public TranslationSettingsWindow(TranslationViewModel model, Action open)
        {
            InitializeComponent();
            _open = open;
            DataContext = model;
            ApiKeyBox.Password = model.Settings.ApiKey;
        }
        private void ApiKey_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is TranslationViewModel model) model.Settings.ApiKey = ApiKeyBox.Password;
        }
        private void Hotkey_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
            if (key == Key.Escape) return;
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Windows) != 0) return;
            HotkeyBox.Text = ((mods & ModifierKeys.Control) != 0 ? "Ctrl+" : "") +
                ((mods & ModifierKeys.Shift) != 0 ? "Shift+" : "") + ((mods & ModifierKeys.Alt) != 0 ? "Alt+" : "") + key;
        }
        private void Open_Click(object sender, RoutedEventArgs e) => _open();
    }
}
