using AoE4OverlayCS.Models;
using AoE4OverlayCS.Services;
using AoE4OverlayCS.Views;
using Newtonsoft.Json.Linq;
using NHotkey.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Linq;
using System.IO;

namespace AoE4OverlayCS.ViewModels
{
    public class MatchHistoryItem
    {
        public List<string> Team1Players { get; set; } = new List<string>();
        public List<string> Team2Players { get; set; } = new List<string>();
        public string Team1Display { get; set; } = "";
        public string Team2Display { get; set; } = "";
        public string Map { get; set; } = "";
        public string Started { get; set; } = "";
        public string Mode { get; set; } = "";
        public string Result { get; set; } = "";
        public string RatingDiff { get; set; } = "";
        public string MatchId { get; set; } = "";
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;
        private readonly ApiCheckerService _apiChecker;
        private readonly WebSocketServerService _wsServer;
        private readonly GlobalHotkeyService _globalHotkey;
        private OverlayWindow? _overlayWindow;

        public AppSettings Settings => _settingsService.Current;

        // Settings Tab
        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(); }
        }

        private string _profileInfo = "No player identified";
        public string ProfileInfo
        {
            get => _profileInfo;
            set { _profileInfo = value; OnPropertyChanged(); }
        }

        private string _profileLink = "";
        public string ProfileLink
        {
            get => _profileLink;
            set { _profileLink = value; OnPropertyChanged(); }
        }

        private string _searchStatusText = "";
        public string SearchStatusText
        {
            get => _searchStatusText;
            set { _searchStatusText = value; OnPropertyChanged(); }
        }

        private System.Windows.Media.Brush _searchStatusBrush = System.Windows.Media.Brushes.Transparent;
        public System.Windows.Media.Brush SearchStatusBrush
        {
            get => _searchStatusBrush;
            set { _searchStatusBrush = value; OnPropertyChanged(); }
        }

        // Games Tab
        public ObservableCollection<MatchHistoryItem> Games { get; } = new ObservableCollection<MatchHistoryItem>();

        // Commands
        public ICommand SearchPlayerCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ToggleOverlayCommand { get; }
        public ICommand ChangeOverlayPositionCommand { get; }
        public ICommand OpenLinkCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _apiChecker = new ApiCheckerService(_settingsService);
            _wsServer = new WebSocketServerService(_settingsService.Current.WebsocketPort);
            _globalHotkey = new GlobalHotkeyService();

            _apiChecker.OnNewGame += OnNewGame;
            _apiChecker.OnError += OnApiError;

            SearchPlayerCommand = new RelayCommand(async _ => await SearchPlayer());
            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            ToggleOverlayCommand = new RelayCommand(_ => ToggleOverlay());
            ChangeOverlayPositionCommand = new RelayCommand(_ => ChangeOverlayPosition());
            OpenLinkCommand = new RelayCommand(url => {
                if (url is string s && !string.IsNullOrEmpty(s))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(s) { UseShellExecute = true });
            });

            UpdateProfileDisplay();
        }

        public void Start()
        {
            _wsServer.Start();
            _apiChecker.Start();
            
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                _overlayWindow = new OverlayWindow(_settingsService.Current);
                UpdateHotkeyRegistration();

                // Load history if profile exists
                if (!string.IsNullOrEmpty(Settings.ProfileId))
                {
                   Task.Run(() => RefreshHistory());
                }
            });
        }
        
        public void UpdateHotkeyRegistration()
        {
            try
            {
                HotkeyManager.Current.Remove("ToggleOverlay");
                _globalHotkey.Stop();
                if (!string.IsNullOrEmpty(Settings.OverlayHotkey))
                {
                    // Parse hotkey string
                    var parts = Settings.OverlayHotkey.Split('+');
                    ModifierKeys modifiers = ModifierKeys.None;
                    Key key = Key.None;

                    foreach (var part in parts)
                    {
                        if (Enum.TryParse(part, true, out Key k))
                        {
                             if (k == Key.LeftCtrl || k == Key.RightCtrl) modifiers |= ModifierKeys.Control;
                             else if (k == Key.LeftShift || k == Key.RightShift) modifiers |= ModifierKeys.Shift;
                             else if (k == Key.LeftAlt || k == Key.RightAlt) modifiers |= ModifierKeys.Alt;
                             else key = k;
                        }
                        else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Control;
                        else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Shift;
                        else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Alt;
                    }

                    if (key != Key.None)
                    {
                        try
                        {
                            HotkeyManager.Current.Remove("ToggleOverlay");
                            HotkeyManager.Current.AddOrReplace("ToggleOverlay", key, modifiers, (s, e) =>
                            {
                                try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} pressed {Settings.OverlayHotkey}{Environment.NewLine}"); } catch { }
                                ToggleOverlay();
                            });
                            try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} registered {Settings.OverlayHotkey}{Environment.NewLine}"); } catch { }
                        }
                        catch (Exception ex)
                        {
                            try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} register-failed {Settings.OverlayHotkey} {ex}{Environment.NewLine}"); } catch { }
                            _globalHotkey.Configure(Settings.OverlayHotkey, () =>
                            {
                                try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} hook-pressed {Settings.OverlayHotkey}{Environment.NewLine}"); } catch { }
                                ToggleOverlay();
                            });
                            _globalHotkey.Start();
                        }
                    }
                }
            }
            catch { /* Ignore invalid hotkeys */ }
        }
        
        public void Stop()
        {
            _apiChecker.Stop();
            _wsServer.Stop();
            _globalHotkey.Stop();
            _overlayWindow?.SaveState();
            _overlayWindow?.Close();
            _settingsService.Save();
        }

        private async Task SearchPlayer()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;
            
            var query = SearchQuery.Trim();
            var player = await _apiChecker.FindPlayer(query);
            if (player != null)
            {
                Settings.ProfileId = player["profile_id"]?.ToString();
                Settings.PlayerName = player["name"]?.ToString();
                Settings.SteamId = player["steam_id"]?.ToString();
                
                UpdateProfileDisplay();
                _settingsService.Save();

                SearchStatusText = "ID Found";
                SearchStatusBrush = System.Windows.Media.Brushes.LimeGreen;
                
                // Refresh history
                await RefreshHistory();
            }
            else
            {
                SearchStatusText = "ID not found";
                SearchStatusBrush = System.Windows.Media.Brushes.OrangeRed;
            }
        }

        private void UpdateProfileDisplay()
        {
            if (string.IsNullOrEmpty(Settings.ProfileId))
            {
                ProfileInfo = "No player identified";
                ProfileLink = "";
            }
            else
            {
                ProfileInfo = $"{Settings.PlayerName}\nSteam_id: {Settings.SteamId}\nProfile_id: {Settings.ProfileId}";
                ProfileLink = $"https://aoe4world.com/players/{Settings.ProfileId}";
            }
        }

        private async Task RefreshHistory()
        {
            var history = await _apiChecker.GetMatchHistory(Settings.MaxGamesHistory);
            if (history != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    Games.Clear();
                    foreach (var game in history)
                    {
                        if (game is JObject gameObject)
                        {
                            try
                            {
                                var item = new MatchHistoryItem();
                                item.Map = gameObject["map"]?.ToString() ?? "?";
                                item.MatchId = gameObject["game_id"]?.ToString() ?? "";
                                item.Mode = gameObject["kind"]?.ToString() ?? "?";
                                // Format started time
                                if (DateTime.TryParse(gameObject["started_at"]?.ToString(), out DateTime dt))
                                {
                                    item.Started = dt.ToLocalTime().ToString("g");
                                }
                                else
                                {
                                    item.Started = gameObject["started_at"]?.ToString() ?? "";
                                }

                                var teams = gameObject["teams"] as JArray;
                                if (teams != null && teams.Count >= 2)
                                {
                                    var t1 = teams[0] as JArray;
                                    var t2 = teams[1] as JArray;
                                    
                                    item.Team1Players = t1?.Select(p => {
                                        var player = p["player"] as JObject;
                                        var name = player?["name"]?.ToString() ?? "?";
                                        var profileId = player?["profile_id"]?.ToString() ?? "";
                                        var civ = player?["civilization"]?.ToString() ?? "";
                                        return string.IsNullOrEmpty(profileId) ? $"{name} ({civ})" : $"{name} [{profileId}] ({civ})";
                                    }).ToList() ?? new List<string>();

                                    item.Team2Players = t2?.Select(p => {
                                        var player = p["player"] as JObject;
                                        var name = player?["name"]?.ToString() ?? "?";
                                        var profileId = player?["profile_id"]?.ToString() ?? "";
                                        var civ = player?["civilization"]?.ToString() ?? "";
                                        return string.IsNullOrEmpty(profileId) ? $"{name} ({civ})" : $"{name} [{profileId}] ({civ})";
                                    }).ToList() ?? new List<string>();

                                    item.Team1Display = string.Join(Environment.NewLine, item.Team1Players);
                                    item.Team2Display = string.Join(Environment.NewLine, item.Team2Players);
                                    
                                    // Check result for current profile
                                    bool found = false;
                                    foreach (var team in teams)
                                    {
                                        if (team is JArray t)
                                        {
                                            foreach (var p in t)
                                            {
                                                var player = p["player"] as JObject;
                                                if (player?["profile_id"]?.ToString() == Settings.ProfileId)
                                                {
                                                    item.Result = player?["result"]?.ToString() ?? "unknown";
                                                    var diff = player?["rating_diff"]?.ToString();
                                                    if (!string.IsNullOrEmpty(diff)) item.RatingDiff = diff;
                                                    found = true;
                                                    break;
                                                }
                                            }
                                        }
                                        if (found) break;
                                    }
                                }
                                Games.Add(item);
                            }
                            catch { /* Ignore parse errors */ }
                        }
                    }
                });
            }
        }

        private void OnNewGame(JObject gameData)
        {
            var processed = GameProcessor.ProcessGame(gameData, _settingsService.Current);
            
            // Send to WS
            _wsServer.Send("player_data", processed);
            
            // Update Overlay
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                _overlayWindow?.UpdateData(processed);
                if (_settingsService.Current.OpenOverlayOnNewGame)
                {
                    _overlayWindow?.Show();
                }
                
            });
            
            // Refresh history
            Task.Run(() => RefreshHistory());
        }
        
        private void OnApiError(string error)
        {
            System.Diagnostics.Debug.WriteLine($"API Error: {error}");
        }

        public void ToggleOverlay()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                if (_overlayWindow != null)
                {
                    _overlayWindow.ToggleVisibility();
                }
            });
        }

        public void ChangeOverlayPosition()
        {
            _overlayWindow?.ToggleLock();
        }

        public void SaveSettings()
        {
            Stop();
            Start();
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
