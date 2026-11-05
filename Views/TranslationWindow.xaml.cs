using AoE4OverlayCS.ViewModels;
using System.Windows;
using System.Windows.Controls;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using System.Windows.Input;
using System.Windows.Media;

namespace AoE4OverlayCS.Views
{
    public partial class TranslationWindow : Window
    {
        private readonly Action _settings;
        private int _historyWheelDelta;
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
        private static ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer scroll) return scroll;
                if (FindScrollViewer(child) is { } nested) return nested;
            }
            return null;
        }
        private void History_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true; // Also intercept wheel input over the read-only history text boxes.
            if (FindScrollViewer(HistoryList) is not { } scroll) return;
            if (Math.Sign(_historyWheelDelta) != Math.Sign(e.Delta)) _historyWheelDelta = 0;
            _historyWheelDelta += e.Delta;
            var records = _historyWheelDelta / Mouse.MouseWheelDeltaForOneLine;
            _historyWheelDelta %= Mouse.MouseWheelDeltaForOneLine;
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - records);
        }
        private void History_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            // Track clicks normally page through several records; use the same single-item step as the arrows.
            if (e.Command != ScrollBar.PageDownCommand && e.Command != ScrollBar.PageUpCommand) return;
            if (FindScrollViewer(HistoryList) is not { } scroll) return;
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + (e.Command == ScrollBar.PageDownCommand ? 1 : -1));
            e.Handled = true;
        }
        private void ClearInput_Click(object sender, RoutedEventArgs e) => InputBox.Focus();
        private void Settings_Click(object sender, RoutedEventArgs e) => _settings();
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            ((TranslationViewModel)DataContext).Cancel();
            base.OnClosing(e);
        }
    }
}
