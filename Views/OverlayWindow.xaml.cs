using AoE4OverlayCS.Models;
using AoE4OverlayCS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

using Image = System.Windows.Controls.Image;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ColorConverter = System.Windows.Media.ColorConverter;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AoE4OverlayCS.Views
{
    public partial class OverlayWindow : Window
    {
        private AppSettings _settings;
        private bool _isLocked = true;
        private readonly Dictionary<string, ImageSource> _imageCache = new();

        private readonly ScaleTransform _contentScale = new ScaleTransform(1, 1);
        private double _baseContentWidth;
        private double _baseContentHeight;
        private bool _hasBaseSize;
        private bool _hasSavedGeometry;
        private double _userScale = 1.0;   // 用户拖拽倍率：默认 1.0 = 纯字体大小基准

        private const double RatingWidth = 70;
        private const double WinrateWidth = 70;
        private const double WinsWidth = 60;
        private const double LossesWidth = 70;
        private const double CountryFlagWidth = 30;

        // P/Invoke for resizing
        private const int WM_SYSCOMMAND = 0x112;
        private const int WM_SIZING = 0x0214;
        private const int SC_SIZE = 0xF000;
        private const int WMSZ_LEFT = 1;
        private const int WMSZ_RIGHT = 2;
        private const int WMSZ_TOP = 3;
        private const int WMSZ_TOPLEFT = 4;
        private const int WMSZ_TOPRIGHT = 5;
        private const int WMSZ_BOTTOM = 6;
        private const int WMSZ_BOTTOMLEFT = 7;
        private const int WMSZ_BOTTOMRIGHT = 8;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public OverlayWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            _settings.PropertyChanged += Settings_PropertyChanged;

            MapLabel.FontSize = Math.Max(10, _settings.FontSize - 2);
            TeamGapColumn.Width = new GridLength(Math.Clamp(_settings.TeamGap, 0, 40));
            ApplyBackground();
            
            if (_settings.OverlayGeometry != null && _settings.OverlayGeometry.Length == 4)
            {
                this.Left = _settings.OverlayGeometry[0];
                this.Top = _settings.OverlayGeometry[1];
                this.Width = _settings.OverlayGeometry[2];
                this.Height = _settings.OverlayGeometry[3];
            }

            _hasSavedGeometry = _settings.OverlayGeometry != null && _settings.OverlayGeometry.Length == 4;
            SizeChanged += OnWindowSizeChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Set NOACTIVATE to prevent stealing focus, which helps hotkeys work better
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

            WindowServices.SetWindowExTransparent(this);
            Background = Brushes.Transparent;
            LockedBorder.Visibility = Visibility.Visible;
            UnlockBorder.Visibility = Visibility.Collapsed;
            ResizeGripControl.Visibility = Visibility.Collapsed;

            // 拖拽缩放时锁定为内容比例（手柄 = 等比缩放内容大小）
            HwndSource.FromHwnd(helper.Handle)?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SIZING && _hasBaseSize)
            {
                var rect = Marshal.PtrToStructure<RECT>(lParam);
                ApplyContentAspect(ref rect, wParam.ToInt32());
                Marshal.StructureToPtr(rect, lParam, false);
                handled = true;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 把拖拽中的窗口矩形约束为内容比例：任意方向拖动都会等比缩放内容，
        /// 缩放限制在 [MinScale, MaxScale]，保证内容始终完整显示、不被裁切。
        /// </summary>
        private void ApplyContentAspect(ref RECT rect, int edge)
        {
            if (_baseContentWidth <= 0 || _baseContentHeight <= 0) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            double widthDip = (rect.Right - rect.Left) / dpi.DpiScaleX;
            double heightDip = (rect.Bottom - rect.Top) / dpi.DpiScaleY;

            // Margin 不随 LayoutTransform 缩放：倍率只作用于内容部分，边距单独保留（恒定 1px 间隙）
            double marginWidth = ContentRoot.Margin.Left + ContentRoot.Margin.Right;
            double marginHeight = ContentRoot.Margin.Top + ContentRoot.Margin.Bottom;
            double contentBaseWidth = Math.Max(1e-6, _baseContentWidth - marginWidth);
            double contentBaseHeight = Math.Max(1e-6, _baseContentHeight - marginHeight);

            bool horizontal = edge == WMSZ_LEFT || edge == WMSZ_RIGHT;
            bool vertical = edge == WMSZ_TOP || edge == WMSZ_BOTTOM;

            // 以拖动幅度较大的方向为准，另一方向按内容比例跟随
            bool driveByWidth = horizontal || (!vertical &&
                Math.Abs(widthDip - ActualWidth) >= Math.Abs(heightDip - ActualHeight));

            double scale = driveByWidth
                ? (widthDip - marginWidth) / contentBaseWidth
                : (heightDip - marginHeight) / contentBaseHeight;
            scale = Math.Clamp(scale, OverlayScaleCalculator.MinScale, OverlayScaleCalculator.MaxScale);
            _userScale = scale; // 记录用户选择的倍率，窗口贴合尺寸始终以此为准

            int w = Math.Max(1, (int)Math.Round((contentBaseWidth * scale + marginWidth) * dpi.DpiScaleX));
            int h = Math.Max(1, (int)Math.Round((contentBaseHeight * scale + marginHeight) * dpi.DpiScaleY));

            // 按拖动边锚定：对边（或对角）保持不动
            switch (edge)
            {
                case WMSZ_LEFT:
                    rect.Left = rect.Right - w;
                    CenterVertically(ref rect, h);
                    break;
                case WMSZ_RIGHT:
                    rect.Right = rect.Left + w;
                    CenterVertically(ref rect, h);
                    break;
                case WMSZ_TOP:
                    rect.Top = rect.Bottom - h;
                    CenterHorizontally(ref rect, w);
                    break;
                case WMSZ_BOTTOM:
                    rect.Bottom = rect.Top + h;
                    CenterHorizontally(ref rect, w);
                    break;
                case WMSZ_TOPLEFT:
                    rect.Left = rect.Right - w;
                    rect.Top = rect.Bottom - h;
                    break;
                case WMSZ_TOPRIGHT:
                    rect.Right = rect.Left + w;
                    rect.Top = rect.Bottom - h;
                    break;
                case WMSZ_BOTTOMLEFT:
                    rect.Left = rect.Right - w;
                    rect.Bottom = rect.Top + h;
                    break;
                default:
                    rect.Right = rect.Left + w;
                    rect.Bottom = rect.Top + h;
                    break;
            }
        }

        private static void CenterVertically(ref RECT rect, int height)
        {
            int center = (rect.Top + rect.Bottom) / 2;
            rect.Top = center - height / 2;
            rect.Bottom = rect.Top + height;
        }

        private static void CenterHorizontally(ref RECT rect, int width)
        {
            int center = (rect.Left + rect.Right) / 2;
            rect.Left = center - width / 2;
            rect.Right = rect.Left + width;
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
             if (e.ButtonState == MouseButtonState.Pressed)
             {
                 WindowInteropHelper helper = new WindowInteropHelper(this);
                 SendMessage(helper.Handle, WM_SYSCOMMAND, (IntPtr)(SC_SIZE + WMSZ_BOTTOMRIGHT), IntPtr.Zero);
             }
        }

        private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
             if (e.PropertyName == nameof(AppSettings.FontSize))
             {
                 Dispatcher.Invoke(() => {
                     MapLabel.FontSize = Math.Max(10, _settings.FontSize - 2);
                     UpdateFontSizeRecursive(TeamLeftPanel);
                     UpdateFontSizeRecursive(TeamRightPanel);
                     RefreshBaseSize();
                 });
             }
             else if (e.PropertyName == nameof(AppSettings.TeamGap))
             {
                 Dispatcher.Invoke(() => {
                     TeamGapColumn.Width = new GridLength(Math.Clamp(_settings.TeamGap, 0, 40));
                     RefreshBaseSize();
                 });
             }
             else if (e.PropertyName == nameof(AppSettings.OverlayBackgroundColor) ||
                      e.PropertyName == nameof(AppSettings.OverlayBackgroundOpacity))
             {
                 Dispatcher.Invoke(ApplyBackground);
             }
        }

        private void UpdateFontSizeRecursive(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb) tb.FontSize = _settings.FontSize;
                UpdateFontSizeRecursive(child);
            }
        }

        private void ApplyBackground()
        {
            var color = ParseColor(_settings.OverlayBackgroundColor);
            ContentRoot.Background = new SolidColorBrush(color)
            {
                Opacity = Math.Clamp(_settings.OverlayBackgroundOpacity, 0, 1)
            };
        }

        private static Color ParseColor(string? hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                    return color;
            }
            catch { }
            return Colors.Black;
        }

        protected override void OnClosed(EventArgs e)
        {
             _settings.PropertyChanged -= Settings_PropertyChanged;
             base.OnClosed(e);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isLocked && e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        public void UpdateData(dynamic data)
        {
            Dispatcher.Invoke(() => {
                MapLabel.Text = data["map"]?.ToString() ?? "";
                
                TeamLeftPanel.Children.Clear();
                TeamRightPanel.Children.Clear();

                var players = data["players"] as IEnumerable<dynamic>;
                if (players == null) return;

                var playerList = players.ToList();
                if (playerList.Count == 0) return;

                int firstTeam = SafeGetTeam(playerList[0]);
                int? secondTeam = null;
                foreach (var pl in playerList)
                {
                    var t = SafeGetTeam(pl);
                    if (t != firstTeam)
                    {
                        secondTeam = t;
                        break;
                    }
                }

                IEnumerable<dynamic> leftPlayers = playerList.Where(p => SafeGetTeam(p) == firstTeam);
                IEnumerable<dynamic> rightPlayers = secondTeam.HasValue
                    ? playerList.Where(p => SafeGetTeam(p) == secondTeam.Value)
                    : playerList.Where(p => SafeGetTeam(p) != firstTeam);

                foreach (var p in leftPlayers)
                {
                    TeamLeftPanel.Children.Add(CreatePlayerRowLeft(p));
                }

                foreach (var p in rightPlayers)
                {
                    TeamRightPanel.Children.Add(CreatePlayerRowRightMirrored(p));
                }

                TryEstablishBaseSize();
            });
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_hasBaseSize) return;
            // 内容缩放只跟随用户倍率；窗口尺寸由 FitWindowToBase/拖拽驱动，
            // 绝不从窗口尺寸反推缩放（否则内容反复变化时会逐次变小）
            SetContentScale(_userScale);
        }

        private void TryEstablishBaseSize()
        {
            // 已有基准：内容可能因搜索/新对局/语言切换而变化，重测基准并按当前倍率重新贴合窗口
            if (_hasBaseSize)
            {
                RefreshBaseSize();
                return;
            }

            // 手动测量内容自然尺寸（未显示时也可测量；此时 LayoutTransform 尚未设置，不受缩放影响）
            InvalidateMeasureTree(ContentRoot);
            ContentRoot.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            if (ContentRoot.DesiredSize.Width <= 0 || ContentRoot.DesiredSize.Height <= 0) return;

            _baseContentWidth = ContentRoot.DesiredSize.Width;
            _baseContentHeight = ContentRoot.DesiredSize.Height;
            _hasBaseSize = true;

            // 默认以内容自然尺寸（由字体大小决定）为基准；仅当存在历史几何时，按历史窗口尺寸恢复用户倍率
            _userScale = 1.0;
            if (_hasSavedGeometry && _settings.OverlayGeometry is { Length: 4 })
            {
                double savedWidth = _settings.OverlayGeometry[2];
                double savedHeight = _settings.OverlayGeometry[3];
                if (savedWidth > 0 && savedHeight > 0)
                {
                    _userScale = Math.Clamp(
                        Math.Min(savedWidth / _baseContentWidth, savedHeight / _baseContentHeight),
                        OverlayScaleCalculator.MinScale,
                        OverlayScaleCalculator.MaxScale);
                }
            }

            FitWindowToBase();
        }

        private void RefreshBaseSize()
        {
            // 字体/内容变化只会标记部分节点失效，中间容器可能仍为 measure-valid，
            // 导致 Measure 直接返回旧缓存（实测调大字体后尺寸不变、内容被裁切）。
            // 因此先递归失效整棵可视子树，再摘除 LayoutTransform 测量未缩放的自然尺寸。
            var savedTransform = ContentRoot.LayoutTransform;
            ContentRoot.LayoutTransform = null;
            InvalidateMeasureTree(ContentRoot);
            ContentRoot.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = ContentRoot.DesiredSize.Width;
            double h = ContentRoot.DesiredSize.Height;
            ContentRoot.LayoutTransform = savedTransform;

            if (w <= 0 || h <= 0) return;

            _baseContentWidth = w;
            _baseContentHeight = h;
            FitWindowToBase();
        }

        /// <summary>递归失效整棵可视子树的测量缓存，确保字体/内容变化后测量拿到的是最新尺寸。</summary>
        private static void InvalidateMeasureTree(DependencyObject root)
        {
            if (root is UIElement element) element.InvalidateMeasure();
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                InvalidateMeasureTree(VisualTreeHelper.GetChild(root, i));
            }
        }

        /// <summary>
        /// 重置为“字体大小基准”：缩放回到 1.0，窗口尺寸贴合内容自然尺寸（四周 1px 边距）。
        /// 每次搜索玩家时调用，保证覆盖层尺寸只由字体大小决定，不会随多次搜索逐次变小。
        /// </summary>
        public void ResetToBaseSize()
        {
            Dispatcher.Invoke(() =>
            {
                _userScale = 1.0;
                if (_hasBaseSize) RefreshBaseSize();
                else TryEstablishBaseSize();
            });
        }

        private void SetContentScale(double scale)
        {
            _contentScale.ScaleX = scale;
            _contentScale.ScaleY = scale;
            ContentRoot.LayoutTransform = _contentScale;
        }

        /// <summary>
        /// 窗口尺寸贴合为“内容自然尺寸 × 用户倍率 + 固定 1px 边距”。
        /// Margin 不随 LayoutTransform 缩放（实测），必须减出去再单独加回，
        /// 才能在任意倍率下保持上下左右恒定 1px 间隙；向上取整避免贴边裁切。
        /// </summary>
        private void FitWindowToBase()
        {
            SetContentScale(_userScale);
            Width = Math.Ceiling(ScaleWithMargin(_baseContentWidth, ContentRoot.Margin.Left + ContentRoot.Margin.Right));
            Height = Math.Ceiling(ScaleWithMargin(_baseContentHeight, ContentRoot.Margin.Top + ContentRoot.Margin.Bottom));
        }

        /// <summary>把“含边距的自然长度”换算为缩放后的窗口长度（边距恒定不缩放）。</summary>
        private double ScaleWithMargin(double baseLength, double marginTotal)
        {
            double content = Math.Max(0, baseLength - marginTotal);
            return content * _userScale + marginTotal;
        }

        private Grid CreatePlayerRowLeft(dynamic p)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var civFlag = CreateCivFlag(p, baseDir, HorizontalAlignment.Left);
            civFlag.Margin = new Thickness(0, 0, 0, 0);
            Grid.SetColumn(civFlag, 0);
            Grid.SetRowSpan(civFlag, 2);
            grid.Children.Add(civFlag);

            var contentGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var nameBg = CreateNameBadge(p, margin: new Thickness(2, 0, 0, 0), textAlignment: TextAlignment.Center);
            Grid.SetRow(nameBg, 0);
            contentGrid.Children.Add(nameBg);

            var statsGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RatingWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WinrateWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WinsWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LossesWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CountryFlagWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rankBadge = CreateRankBadge(p.rank?.ToString() ?? "", alignRight: false);
            Grid.SetColumn(rankBadge, 0);
            statsGrid.Children.Add(rankBadge);

            AddTextCell(statsGrid, row: 0, col: 1, text: p.rating.ToString(), colorCode: "#7ab6ff", bold: true, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: RatingWidth);
            AddTextCell(statsGrid, row: 0, col: 2, text: p.winrate.ToString(), colorCode: "#fffb78", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: WinrateWidth);
            AddTextCell(statsGrid, row: 0, col: 3, text: FormatWins(p.wins), colorCode: "#48bd21", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: WinsWidth);
            AddTextCell(statsGrid, row: 0, col: 4, text: FormatLosses(p.losses), colorCode: "Red", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: LossesWidth);
            AddCountryFlagCell(statsGrid, row: 0, col: 5, country: p.country?.ToString() ?? "", baseDir: baseDir, minWidth: CountryFlagWidth);
            AddCountryNameCell(statsGrid, row: 0, col: 6, country: p.country?.ToString() ?? "", hAlign: HorizontalAlignment.Left, tAlign: TextAlignment.Left);

            Grid.SetRow(statsGrid, 1);
            contentGrid.Children.Add(statsGrid);

            Grid.SetColumn(contentGrid, 1);
            grid.Children.Add(contentGrid);
            return grid;
        }

        private Grid CreatePlayerRowRightMirrored(dynamic p)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Right };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var civFlag = CreateCivFlag(p, baseDir, HorizontalAlignment.Right);
            civFlag.Margin = new Thickness(0, 0, 0, 0);
            Grid.SetColumn(civFlag, 1);
            Grid.SetRowSpan(civFlag, 2);
            grid.Children.Add(civFlag);

            var contentGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Right };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var nameBg = CreateNameBadge(p, margin: new Thickness(0, 0, 2, 0), textAlignment: TextAlignment.Center);
            Grid.SetRow(nameBg, 0);
            contentGrid.Children.Add(nameBg);

            var statsGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CountryFlagWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LossesWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WinsWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WinrateWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RatingWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            AddCountryNameCell(statsGrid, row: 0, col: 0, country: p.country?.ToString() ?? "", hAlign: HorizontalAlignment.Right, tAlign: TextAlignment.Right);
            AddCountryFlagCell(statsGrid, row: 0, col: 1, country: p.country?.ToString() ?? "", baseDir: baseDir, minWidth: CountryFlagWidth);
            AddTextCell(statsGrid, row: 0, col: 2, text: FormatLosses(p.losses), colorCode: "Red", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: LossesWidth);
            AddTextCell(statsGrid, row: 0, col: 3, text: FormatWins(p.wins), colorCode: "#48bd21", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: WinsWidth);
            AddTextCell(statsGrid, row: 0, col: 4, text: p.winrate.ToString(), colorCode: "#fffb78", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: WinrateWidth);
            AddTextCell(statsGrid, row: 0, col: 5, text: p.rating.ToString(), colorCode: "#7ab6ff", bold: true, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: RatingWidth);

            var rankBadge = CreateRankBadge(p.rank?.ToString() ?? "", alignRight: true);
            Grid.SetColumn(rankBadge, 6);
            statsGrid.Children.Add(rankBadge);

            Grid.SetRow(statsGrid, 1);
            contentGrid.Children.Add(statsGrid);

            Grid.SetColumn(contentGrid, 0);
            grid.Children.Add(contentGrid);
            return grid;
        }

        private Border CreateRankBadge(string rank, bool alignRight, string? country = null, string? baseDir = null)
        {
            var inner = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (alignRight && !string.IsNullOrEmpty(country) && !string.IsNullOrEmpty(baseDir))
            {
                var flag = CreateCountryFlag(new { country }, baseDir, HorizontalAlignment.Right);
                flag.Margin = new Thickness(0, 0, 6, 0);
                inner.Children.Add(flag);
            }

            var tb = new TextBlock
            {
                Text = rank,
                FontSize = _settings.FontSize,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ddd")),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            inner.Children.Add(tb);

            var badge = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Child = inner
            };

            if (string.IsNullOrWhiteSpace(rank)) badge.Visibility = Visibility.Collapsed;
            return badge;
        }

        private string FormatWins(object? wins)
        {
            var s = wins?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(s) || s == "0") return "";
            return $"{s}W";
        }

        private string FormatLosses(object? losses)
        {
            var s = losses?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(s) || s == "0") return "";
            return $"{s}L";
        }

        private void AddTextCellWithCountry(Grid grid, int row, int col, string text, string colorCode, bool alignRight, string country, string baseDir, double minWidth)
        {
            var host = new Grid { MinWidth = minWidth };
            if (alignRight)
            {
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }
            else
            {
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            var txt = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left,
                FontSize = _settings.FontSize,
                Margin = new Thickness(3, 0, 3, 0)
            };
            try { txt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorCode)); } catch { txt.Foreground = Brushes.White; }
            var countryImg = new Image { Width = 25, Height = 14, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(country))
            {
                string countryPath = Path.Combine(baseDir, "img", "countries", $"{country}.png");
                if (!File.Exists(countryPath))
                {
                    countryPath = Path.Combine(baseDir, "Resources", "img", "countries", $"{country}.png");
                }
                if (File.Exists(countryPath))
                {
                    try { countryImg.Source = TryLoadImageSource(countryPath, 50); } catch { }
                }
            }

            if (alignRight)
            {
                Grid.SetColumn(txt, 0);
                host.Children.Add(txt);

                Grid.SetColumn(countryImg, 1);
                countryImg.Margin = new Thickness(0, 0, 4, 0);
                host.Children.Add(countryImg);
            }
            else
            {
                Grid.SetColumn(countryImg, 0);
                countryImg.Margin = new Thickness(4, 0, 0, 0);
                host.Children.Add(countryImg);

                Grid.SetColumn(txt, 1);
                txt.Margin = new Thickness(6, 0, 6, 0);
                host.Children.Add(txt);
            }

            var cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0, 0, 2, 0),
                Child = host
            };
            if (string.IsNullOrWhiteSpace(text)) cell.Visibility = Visibility.Collapsed;
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
        }

        private void AddCountryFlagCell(Grid grid, int row, int col, string country, string baseDir, double minWidth)
        {
            var countryImg = new Image { Width = 25, Height = 14, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            if (!string.IsNullOrEmpty(country))
            {
                string countryPath = Path.Combine(baseDir, "img", "countries", $"{country}.png");
                if (!File.Exists(countryPath))
                {
                    countryPath = Path.Combine(baseDir, "Resources", "img", "countries", $"{country}.png");
                }
                if (File.Exists(countryPath))
                {
                    try { countryImg.Source = TryLoadImageSource(countryPath, 50); } catch { }
                }
            }

            var cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0, 0, 2, 0),
                Child = countryImg,
                MinWidth = minWidth
            };
            if (countryImg.Source == null) cell.Visibility = Visibility.Collapsed;
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
        }

        /// <summary>
        /// 国家名列：仅中文界面且能翻译出国家名时显示，作为独立一栏与国旗相邻。
        /// </summary>
        private void AddCountryNameCell(Grid grid, int row, int col, string country, HorizontalAlignment hAlign, TextAlignment tAlign)
        {
            bool isZhCn = string.Equals(_settings.Language, "zh-CN", StringComparison.OrdinalIgnoreCase);
            string? countryName = isZhCn && !string.IsNullOrEmpty(country)
                ? CountryNameTranslator.Translate(country, _settings.Language)
                : null;
            if (string.IsNullOrEmpty(countryName) || countryName == country) return;

            var txt = new TextBlock
            {
                Text = countryName,
                FontSize = _settings.FontSize,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ddd")),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = hAlign,
                TextAlignment = tAlign,
                Margin = new Thickness(4, 0, 4, 0)
            };

            var cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0, 0, 2, 0),
                Child = txt
            };
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
        }

        private Image CreateCivFlag(dynamic p, string baseDir, HorizontalAlignment hAlign)
        {
            const double targetHeight = 36;
            var flagImg = new Image { Width = 72, Height = targetHeight, Stretch = Stretch.Uniform, HorizontalAlignment = hAlign };
            string civ = p.civ;
            string civKey = civ.ToString().Replace(" ", "_").ToLower();
            string? resolvedPath = CivIconResolver.Resolve(baseDir, civ, civKey);
            if (resolvedPath != null)
            {
                var source = TryLoadImageSource(resolvedPath);
                if (source != null)
                {
                    flagImg.Source = source;
                    ApplyImageAspectWidth(flagImg, source, targetHeight);
                }
            }
            else
            {
                File.AppendAllText(LogPaths.Get("image_load_error.log"), $"Civ icon not found (Civ: {civ}, Key: {civKey}){Environment.NewLine}");
            }
            return flagImg;
        }

        private static void ApplyImageAspectWidth(Image image, ImageSource source, double targetHeight)
        {
            if (source.Width <= 0 || source.Height <= 0)
            {
                return;
            }

            image.Height = targetHeight;
            image.Width = Math.Max(1, source.Width / source.Height * targetHeight);
        }

        private Border CreateNameBadge(dynamic p, Thickness margin, TextAlignment textAlignment)
        {
            var nameTxt = new TextBlock
            {
                Text = p.name,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = _settings.FontSize,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = textAlignment,
                MaxWidth = 300
            };
            nameTxt.Foreground = Brushes.White;

            var teamColor = GetTeamNameBrush(SafeGetTeam(p));
            var nameBg = new Border
            {
                Background = teamColor,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2, 1, 2, 1),
                Margin = margin,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            nameBg.Child = nameTxt;
            return nameBg;
        }

        private Image CreateCountryFlag(dynamic p, string baseDir, HorizontalAlignment hAlign)
        {
            var countryImg = new Image { Width = 25, Height = 14, Stretch = Stretch.Uniform, HorizontalAlignment = hAlign };
            string country = p.country;
            if (!string.IsNullOrEmpty(country))
            {
                string countryPath = Path.Combine(baseDir, "img", "countries", $"{country}.png");
                if (!File.Exists(countryPath))
                {
                    countryPath = Path.Combine(baseDir, "Resources", "img", "countries", $"{country}.png");
                }
                if (File.Exists(countryPath))
                {
                    // TryLoadImageSource 内部吞异常并缓存/降采样，无需再包 try
                    countryImg.Source = TryLoadImageSource(countryPath, 50);
                }
            }
            return countryImg;
        }

        private int SafeGetTeam(dynamic p)
        {
            try
            {
                return Convert.ToInt32(p.team);
            }
            catch
            {
                return 1;
            }
        }

        private System.Windows.Media.Brush GetTeamNameBrush(int team)
        {
            try
            {
                if (_settings.TeamColors == null || _settings.TeamColors.Count == 0)
                {
                    return new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
                }

                int idx = Math.Max(1, team) - 1;
                idx %= _settings.TeamColors.Count;
                var row = _settings.TeamColors[idx];
                if (row == null || row.Count < 4)
                {
                    return new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
                }

                int r = Convert.ToInt32(row[0]);
                int g = Convert.ToInt32(row[1]);
                int b = Convert.ToInt32(row[2]);
                double a = Convert.ToDouble(row[3]);
                int alphaInt = (int)Math.Round(a * 255);
                byte alpha = (byte)Math.Clamp(alphaInt, 0, 255);
                return new SolidColorBrush(Color.FromArgb(alpha, (byte)r, (byte)g, (byte)b));
            }
            catch
            {
                return new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            }
        }

        private void AddTextCell(Grid grid, int row, int col, string text, string colorCode = "White", bool bold = false, HorizontalAlignment hAlign = HorizontalAlignment.Center, TextAlignment tAlign = TextAlignment.Center, double minWidth = 0)
        {
            var txt = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = hAlign,
                TextAlignment = tAlign,
                FontSize = _settings.FontSize,
                Margin = new Thickness(3, 0, 3, 0)
            };
            if (string.IsNullOrWhiteSpace(text)) txt.Visibility = Visibility.Collapsed;
            if (minWidth > 0) txt.MinWidth = minWidth;
            try {
                txt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorCode));
            } catch { txt.Foreground = Brushes.White; }
            
            if (bold) txt.FontWeight = FontWeights.Bold;
            var cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0, 0, 2, 0),
                Child = txt
            };
            if (txt.Visibility == Visibility.Collapsed) cell.Visibility = Visibility.Collapsed;
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
        }

        private ImageSource? TryLoadImageSource(string path, int decodePixelWidth = 0)
        {
            if (_imageCache.TryGetValue(path, out var cached)) return cached;

            try
            {
                using var fs = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.StreamSource = fs;
                bitmap.EndInit();
                bitmap.Freeze();
                _imageCache[path] = bitmap;
                return bitmap;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(LogPaths.Get("image_load_error.log"), $"Failed to load {path}: {ex.Message}{Environment.NewLine}"); } catch { }
                return null;
            }
        }

        public void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible) Hide();
            else Show();
        }
        
        public void SaveState()
        {
             _settings.OverlayGeometry = new[] { Left, Top, Width, Height };
        }
        
        public void ToggleLock()
        {
            if (_isLocked)
            {
                // Unlock: Make it interactive, background stays fully transparent
                WindowServices.RemoveWindowExTransparent(this);
                Background = Brushes.Transparent;

                // Show resize controls, hide locked border
                LockedBorder.Visibility = Visibility.Collapsed;
                UnlockBorder.Visibility = Visibility.Visible;
                ResizeGripControl.Visibility = Visibility.Visible;

                _isLocked = false;
            }
            else
            {
                // Lock: Make it click-through, background stays fully transparent
                WindowServices.SetWindowExTransparent(this);
                Background = Brushes.Transparent;
                
                // Show locked border, hide resize controls
                LockedBorder.Visibility = Visibility.Visible;
                UnlockBorder.Visibility = Visibility.Collapsed;
                ResizeGripControl.Visibility = Visibility.Collapsed;
                
                _isLocked = true;
            }
        }
    }
}
