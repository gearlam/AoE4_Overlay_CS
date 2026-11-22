using AoE4OverlayCS.ViewModels;
using System.Windows;
using System.Windows.Controls;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AoE4OverlayCS.Views
{
    public partial class TranslationWindow : Window
    {
        private readonly Action _settings;
        private int _historyWheelDelta;
        private Button? _pendingCopyButton;      // 最近一次点击的复制按钮（手动复制）
        private DateTime _pendingCopyAt;          // 点击时间，用于区分手动复制与翻译后自动复制
        private Button? _flashingButton;           // 当前正在展示“已复制”反馈的按钮
        private DispatcherTimer? _flashTimer;

        public TranslationWindow(TranslationViewModel model, Action settings)
        {
            InitializeComponent();
            DataContext = model;
            _settings = settings;
            model.CopySucceeded += OnCopySucceeded;
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

        /// <summary>记录手动点击的复制按钮；命令随后同步执行，触发 CopySucceeded 时对应该按钮做反馈。</summary>
        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            _pendingCopyButton = sender as Button;
            _pendingCopyAt = DateTime.Now;
        }

        /// <summary>复制成功反馈：手动复制闪对应按钮；翻译完成后自动复制闪主复制按钮，提示译文已进剪贴板。</summary>
        private void OnCopySucceeded()
        {
            var target = _pendingCopyButton != null && (DateTime.Now - _pendingCopyAt).TotalMilliseconds < 400
                ? _pendingCopyButton
                : MainCopyButton;
            if (target != null) FlashCopied(target);
        }

        /// <summary>按钮变绿显示“已复制 ✓”并做缩放脉冲，短暂停留后还原；期间再次复制会立即重置重新计时。</summary>
        private void FlashCopied(Button button)
        {
            RestoreFlash();
            _flashingButton = button;
            button.SetCurrentValue(ContentControl.ContentProperty, TryFindResource("Tr_CopiedShort") ?? "已复制 ✓");
            button.SetCurrentValue(System.Windows.Controls.Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x7D, 0x32)));
            button.SetCurrentValue(System.Windows.Controls.Control.ForegroundProperty, Brushes.White);
            var scale = new ScaleTransform(1, 1);
            button.RenderTransform = scale;
            var pulse = new DoubleAnimation(1, 1.15, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, EasingFunction = new QuadraticEase() };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            _flashTimer.Tick += (_, _) => RestoreFlash();
            _flashTimer.Start();
        }

        /// <summary>还原按钮的默认外观；内容恢复为跟随界面语言的动态资源。</summary>
        private void RestoreFlash()
        {
            _flashTimer?.Stop();
            _flashTimer = null;
            if (_flashingButton == null) return;
            var button = _flashingButton;
            _flashingButton = null;
            button.SetResourceReference(ContentControl.ContentProperty, "Tr_Copy");
            button.InvalidateProperty(System.Windows.Controls.Control.BackgroundProperty);
            button.InvalidateProperty(System.Windows.Controls.Control.ForegroundProperty);
            button.ClearValue(UIElement.RenderTransformProperty);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            RestoreFlash();
            ((TranslationViewModel)DataContext).CopySucceeded -= OnCopySucceeded;
            ((TranslationViewModel)DataContext).Cancel();
            base.OnClosing(e);
        }
    }
}
