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
using SixLabors.ImageSharp.Formats.Png;

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

        private const double RatingWidth = 70;
        private const double WinrateWidth = 70;
        private const double WinsWidth = 60;
        private const double LossesWidth = 70;
        private const double CountryFlagWidth = 30;
        
        // P/Invoke for resizing
        private const int WM_SYSCOMMAND = 0x112;
        private const int SC_SIZE = 0xF000;
        private const int WMSZ_BOTTOMRIGHT = 8;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;
        
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
            
            if (_settings.OverlayGeometry != null && _settings.OverlayGeometry.Length == 4)
            {
                this.Left = _settings.OverlayGeometry[0];
                this.Top = _settings.OverlayGeometry[1];
                this.Width = _settings.OverlayGeometry[2];
                this.Height = _settings.OverlayGeometry[3];
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Set NOACTIVATE to prevent stealing focus, which helps hotkeys work better
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

            WindowServices.SetWindowExTransparent(this);
            Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
            UnlockBorder.Visibility = Visibility.Collapsed;
            ResizeGripControl.Visibility = Visibility.Collapsed;
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
                 });
             }
             else if (e.PropertyName == nameof(AppSettings.TeamGap))
             {
                 Dispatcher.Invoke(() => {
                     TeamGapColumn.Width = new GridLength(Math.Clamp(_settings.TeamGap, 0, 40));
                 });
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
            });
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

            var nameBg = CreateNameBadge(p, margin: new Thickness(6, 0, 0, 0), textAlignment: TextAlignment.Left);
            nameBg.MaxWidth = 300;
            Grid.SetRow(nameBg, 0);
            contentGrid.Children.Add(nameBg);

            var statsGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RatingWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WinrateWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WinsWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LossesWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CountryFlagWidth) });

            var rankBadge = CreateRankBadge(p.rank?.ToString() ?? "", alignRight: false);
            Grid.SetColumn(rankBadge, 0);
            statsGrid.Children.Add(rankBadge);

            AddTextCell(statsGrid, row: 0, col: 1, text: p.rating.ToString(), colorCode: "#7ab6ff", bold: true, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: RatingWidth);
            AddTextCell(statsGrid, row: 0, col: 2, text: p.winrate.ToString(), colorCode: "#fffb78", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: WinrateWidth);
            AddTextCell(statsGrid, row: 0, col: 3, text: FormatWins(p.wins), colorCode: "#48bd21", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: WinsWidth);
            AddTextCell(statsGrid, row: 0, col: 4, text: FormatLosses(p.losses), colorCode: "Red", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: LossesWidth);
            AddCountryFlagCell(statsGrid, row: 0, col: 5, country: p.country?.ToString() ?? "", baseDir: baseDir, minWidth: CountryFlagWidth);

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

            var nameBg = CreateNameBadge(p, margin: new Thickness(0, 0, 6, 0), textAlignment: TextAlignment.Right);
            nameBg.MaxWidth = 300;
            Grid.SetRow(nameBg, 0);
            contentGrid.Children.Add(nameBg);

            var statsGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CountryFlagWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LossesWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WinsWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WinrateWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RatingWidth) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            AddCountryFlagCell(statsGrid, row: 0, col: 0, country: p.country?.ToString() ?? "", baseDir: baseDir, minWidth: CountryFlagWidth);
            AddTextCell(statsGrid, row: 0, col: 1, text: FormatLosses(p.losses), colorCode: "Red", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: LossesWidth);
            AddTextCell(statsGrid, row: 0, col: 2, text: FormatWins(p.wins), colorCode: "#48bd21", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: WinsWidth);
            AddTextCell(statsGrid, row: 0, col: 3, text: p.winrate.ToString(), colorCode: "#fffb78", bold: false, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: WinrateWidth);
            AddTextCell(statsGrid, row: 0, col: 4, text: p.rating.ToString(), colorCode: "#7ab6ff", bold: true, hAlign: HorizontalAlignment.Center, tAlign: TextAlignment.Center, minWidth: RatingWidth);

            var rankBadge = CreateRankBadge(p.rank?.ToString() ?? "", alignRight: true);
            Grid.SetColumn(rankBadge, 5);
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
                    try { countryImg.Source = new BitmapImage(new Uri(countryPath)); } catch { }
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
                    try { countryImg.Source = new BitmapImage(new Uri(countryPath)); } catch { }
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

        private Image CreateCivFlag(dynamic p, string baseDir, HorizontalAlignment hAlign)
        {
            var flagImg = new Image { Width = 72, Height = 36, Stretch = Stretch.UniformToFill, HorizontalAlignment = hAlign };
            string civ = p.civ;
            string civKey = civ.ToString().Replace(" ", "_").ToLower();
            string? resolvedPath = CivIconResolver.Resolve(baseDir, civ, civKey);
            if (resolvedPath != null)
            {
                var source = TryLoadImageSource(resolvedPath);
                if (source != null) flagImg.Source = source;
            }
            else
            {
                File.AppendAllText(LogPaths.Get("image_load_error.log"), $"Civ icon not found (Civ: {civ}, Key: {civKey}){Environment.NewLine}");
            }
            return flagImg;
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
                TextAlignment = textAlignment
            };
            nameTxt.Foreground = Brushes.White;

            var teamColor = GetTeamNameBrush(SafeGetTeam(p));
            var nameBg = new Border
            {
                Background = teamColor,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
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
                    try
                    {
                        var bitmap = new BitmapImage(new Uri(countryPath));
                        countryImg.Source = bitmap;
                    }
                    catch { }
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

        private ImageSource? TryLoadImageSource(string path)
        {
            if (_imageCache.TryGetValue(path, out var cached)) return cached;

            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();

                if (ext == ".webp")
                {
                    using var image = SixLabors.ImageSharp.Image.Load(path);
                    using var ms = new MemoryStream();
                    image.Save(ms, new PngEncoder());
                    ms.Position = 0;

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    _imageCache[path] = bmp;
                    return bmp;
                }

                using var fs = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
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
                // Unlock: Make it interactive and visible background (100% black)
                WindowServices.RemoveWindowExTransparent(this);
                Background = new SolidColorBrush(Colors.Black); 
                
                // Show resize controls
                UnlockBorder.Visibility = Visibility.Visible;
                ResizeGripControl.Visibility = Visibility.Visible;
                
                _isLocked = false;
            }
            else
            {
                // Lock: Make it click-through and transparent background (50% black)
                WindowServices.SetWindowExTransparent(this);
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
                
                // Hide resize controls
                UnlockBorder.Visibility = Visibility.Collapsed;
                ResizeGripControl.Visibility = Visibility.Collapsed;
                
                _isLocked = true;
            }
        }
    }
}
