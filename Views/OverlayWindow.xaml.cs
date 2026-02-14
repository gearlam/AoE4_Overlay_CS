using AoE4OverlayCS.Models;
using AoE4OverlayCS.Services;
using System;
using System.Collections.Generic;
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
                     MapLabel.FontSize = _settings.FontSize + 1;
                     foreach (var child in PlayersPanel.Children)
                     {
                         if (child is Grid grid)
                         {
                             foreach (var item in grid.Children)
                             {
                                 if (item is TextBlock tb)
                                 {
                                     tb.FontSize = _settings.FontSize;
                                 }
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
                
                PlayersPanel.Children.Clear();

                var players = data["players"] as IEnumerable<dynamic>;
                if (players == null) return;

                foreach (var p in players)
                {
                    var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
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

                    var flagImg = new Image { Width = 60, Height = 30, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Left };
                    string civ = p.civ;
                    string civKey = civ.ToString().Replace(" ", "_").ToLower();
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
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
                    Grid.SetColumn(flagImg, 0);
                    grid.Children.Add(flagImg);

                    var nameTxt = new TextBlock { Text = p.name, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, FontSize = _settings.FontSize, TextTrimming = TextTrimming.CharacterEllipsis };
                    nameTxt.Foreground = Brushes.White;
                    var teamColor = GetTeamNameBrush(SafeGetTeam(p));
                    var nameBg = new Border
                    {
                        Background = teamColor,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    nameBg.Child = nameTxt;
                    Grid.SetColumn(nameBg, 1);
                    grid.Children.Add(nameBg);

                    var countryImg = new Image { Width = 25, Height = 14, Stretch = Stretch.Uniform };
                    string country = p.country;
                    if (!string.IsNullOrEmpty(country)) {
                        string countryPath = Path.Combine(baseDir, "img", "countries", $"{country}.png");
                        if (!File.Exists(countryPath))
                        {
                            countryPath = Path.Combine(baseDir, "Resources", "img", "countries", $"{country}.png");
                        }
                        if (File.Exists(countryPath)) {
                            try {
                                var bitmap = new BitmapImage(new Uri(countryPath));
                                countryImg.Source = bitmap;
                            } catch {}
                         }
                    }
                    Grid.SetColumn(countryImg, 2);
                    grid.Children.Add(countryImg);

                    AddText(grid, 3, p.rating.ToString(), "#7ab6ff", true);
                    AddText(grid, 4, p.rank.ToString());
                    AddText(grid, 5, p.winrate.ToString(), "#fffb78");
                    AddText(grid, 6, p.wins.ToString(), "#48bd21");
                    AddText(grid, 7, p.losses.ToString(), "Red");
                    AddText(grid, 8, p.civ_games.ToString(), _settings.CivStatsColor);
                    AddText(grid, 9, p.civ_winrate.ToString(), _settings.CivStatsColor);
                    AddText(grid, 10, p.civ_win_length_median.ToString(), _settings.CivStatsColor);

                    PlayersPanel.Children.Add(grid);
                }
            });
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

        private void AddText(Grid grid, int col, string text, string colorCode = "White", bool bold = false)
        {
            var txt = new TextBlock { 
                Text = text, 
                VerticalAlignment = VerticalAlignment.Center, 
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = _settings.FontSize
            };
            if (string.IsNullOrWhiteSpace(text))
            {
                txt.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (col == 3) txt.MinWidth = 56;
                else if (col == 4) { txt.MinWidth = 86; txt.TextTrimming = TextTrimming.None; }
                else if (col == 5) txt.MinWidth = 62;
                else if (col == 6 || col == 7) txt.MinWidth = 44;
                else if (col == 8 || col == 9 || col == 10) txt.MinWidth = 64;
            }
            try {
                txt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorCode));
            } catch { txt.Foreground = Brushes.White; }
            
            if (bold) txt.FontWeight = FontWeights.Bold;
            Grid.SetColumn(txt, col);
            grid.Children.Add(txt);
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
