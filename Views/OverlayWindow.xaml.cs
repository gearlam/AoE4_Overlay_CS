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
                     foreach (var child in TeamLeftPanel.Children)
                     {
                         if (child is Grid grid)
                         {
                             foreach (var item in grid.Children)
                             {
                                 if (item is TextBlock tb) tb.FontSize = _settings.FontSize;
                                 else if (item is Border b && b.Child is TextBlock tbb) tbb.FontSize = _settings.FontSize;
                             }
                         }
                     }

                     foreach (var child in TeamRightPanel.Children)
                     {
                         if (child is Grid grid)
                         {
                             foreach (var item in grid.Children)
                             {
                                 if (item is TextBlock tb) tb.FontSize = _settings.FontSize;
                                 else if (item is Border b && b.Child is TextBlock tbb) tbb.FontSize = _settings.FontSize;
                             }
                         }
                     }
                 });
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var flagImg = CreateCivFlag(p, baseDir, HorizontalAlignment.Left);
            Grid.SetColumn(flagImg, 0);
            grid.Children.Add(flagImg);

            var nameBg = CreateNameBadge(p, margin: new Thickness(0, 0, 6, 0), textAlignment: TextAlignment.Left);
            Grid.SetColumn(nameBg, 1);
            grid.Children.Add(nameBg);

            var countryImg = CreateCountryFlag(p, baseDir, HorizontalAlignment.Left);
            Grid.SetColumn(countryImg, 2);
            grid.Children.Add(countryImg);

            AddTextCell(grid, 3, p.rating.ToString(), "#7ab6ff", true, HorizontalAlignment.Center, TextAlignment.Center, 56);
            AddTextCell(grid, 4, p.rank.ToString(), "White", false, HorizontalAlignment.Center, TextAlignment.Center, 86);
            AddTextCell(grid, 5, p.winrate.ToString(), "#fffb78", false, HorizontalAlignment.Center, TextAlignment.Center, 62);
            AddTextCell(grid, 6, p.wins.ToString(), "#48bd21", false, HorizontalAlignment.Center, TextAlignment.Center, 44);
            AddTextCell(grid, 7, p.losses.ToString(), "Red", false, HorizontalAlignment.Center, TextAlignment.Center, 44);
            AddTextCell(grid, 8, p.civ_games.ToString(), _settings.CivStatsColor, false, HorizontalAlignment.Center, TextAlignment.Center, 64);
            AddTextCell(grid, 9, p.civ_winrate.ToString(), _settings.CivStatsColor, false, HorizontalAlignment.Center, TextAlignment.Center, 64);
            AddTextCell(grid, 10, p.civ_win_length_median.ToString(), _settings.CivStatsColor, false, HorizontalAlignment.Center, TextAlignment.Center, 64);
            return grid;
        }

        private Grid CreatePlayerRowRightMirrored(dynamic p)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Right };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });

            AddTextCell(grid, 0, p.civ_win_length_median.ToString(), _settings.CivStatsColor, false, HorizontalAlignment.Center, TextAlignment.Center, 64);
            AddTextCell(grid, 1, p.civ_winrate.ToString(), _settings.CivStatsColor, false, HorizontalAlignment.Center, TextAlignment.Center, 64);
            AddTextCell(grid, 2, p.civ_games.ToString(), _settings.CivStatsColor, false, HorizontalAlignment.Center, TextAlignment.Center, 64);
            AddTextCell(grid, 3, p.losses.ToString(), "Red", false, HorizontalAlignment.Center, TextAlignment.Center, 44);
            AddTextCell(grid, 4, p.wins.ToString(), "#48bd21", false, HorizontalAlignment.Center, TextAlignment.Center, 44);
            AddTextCell(grid, 5, p.winrate.ToString(), "#fffb78", false, HorizontalAlignment.Center, TextAlignment.Center, 62);
            AddTextCell(grid, 6, p.rank.ToString(), "White", false, HorizontalAlignment.Center, TextAlignment.Center, 86);
            AddTextCell(grid, 7, p.rating.ToString(), "#7ab6ff", true, HorizontalAlignment.Center, TextAlignment.Center, 56);

            var countryImg = CreateCountryFlag(p, baseDir, HorizontalAlignment.Right);
            Grid.SetColumn(countryImg, 8);
            grid.Children.Add(countryImg);

            var nameBg = CreateNameBadge(p, margin: new Thickness(6, 0, 0, 0), textAlignment: TextAlignment.Right);
            Grid.SetColumn(nameBg, 9);
            grid.Children.Add(nameBg);

            var flagImg = CreateCivFlag(p, baseDir, HorizontalAlignment.Right);
            Grid.SetColumn(flagImg, 10);
            grid.Children.Add(flagImg);
            return grid;
        }

        private Image CreateCivFlag(dynamic p, string baseDir, HorizontalAlignment hAlign)
        {
            var flagImg = new Image { Width = 60, Height = 30, Stretch = Stretch.UniformToFill, HorizontalAlignment = hAlign };
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
                VerticalAlignment = VerticalAlignment.Center
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

        private void AddTextCell(Grid grid, int col, string text, string colorCode = "White", bool bold = false, HorizontalAlignment hAlign = HorizontalAlignment.Center, TextAlignment tAlign = TextAlignment.Center, double minWidth = 0)
        {
            var txt = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = hAlign,
                TextAlignment = tAlign,
                FontSize = _settings.FontSize,
                Margin = new Thickness(6, 0, 6, 0)
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
