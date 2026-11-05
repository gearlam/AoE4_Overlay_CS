using AoE4OverlayCS.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace AoE4OverlayCS.Views
{
    public partial class TranslationWindow : Window
    {
        private readonly Action _settings;
        public TranslationWindow(TranslationViewModel model, Action settings)
        {
            InitializeComponent();
            DataContext = model;
            _settings = settings;
            Activated += (_, _) => InputBox.Focus();
            Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                // Wait for activation to settle; owned settings/confirmation dialogs are part of this UI.
                if (IsVisible && IsEnabled && !IsActive && !OwnedWindows.Cast<Window>().Any(w => w.IsActive)) Hide();
            });
        }
        private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Let the IME consume candidate-confirmation keys; translate only a plain Enter.
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                var model = (TranslationViewModel)DataContext;
                if (model.TranslateCommand.CanExecute(null)) model.TranslateCommand.Execute(null);
            }
        }
        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ((TranslationViewModel)DataContext).Cancel();
                Hide();
            }
        }
        private void Settings_Click(object sender, RoutedEventArgs e) => _settings();
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            ((TranslationViewModel)DataContext).Cancel();
            base.OnClosing(e);
        }
    }
}
