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
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Linq;
using System.IO;

namespace AoE4OverlayCS.ViewModels
{
    public class PlayerDisplayInfo
    {
        public string Name { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public string ProfileIdDisplay { get; set; } = "";
        public string Civ { get; set; } = "";
        public string CivColor { get; set; } = "#BC8AEA";
        public string ProfileUrl => string.IsNullOrEmpty(ProfileId)
            ? ""
            : $"https://aoe4world.com/players/{ProfileId}";
    }

    public class MatchHistoryItem
    {
        public List<PlayerDisplayInfo> Team1Players { get; set; } = new List<PlayerDisplayInfo>();
        public List<PlayerDisplayInfo> Team2Players { get; set; } = new List<PlayerDisplayInfo>();
        public string Team1Display { get; set; } = "";
        public string Team2Display { get; set; } = "";
        public string Map { get; set; } = "";
        public string Started { get; set; } = "";
        public string Mode { get; set; } = "";
        public string Result { get; set; } = "";
        public string RatingDiff { get; set; } = "";
        public string MatchId { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public string GameUrl => string.IsNullOrEmpty(ProfileId) || string.IsNullOrEmpty(MatchId)
            ? ""
            : $"https://aoe4world.com/players/{ProfileId}/games/{MatchId}";
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;
        private readonly ApiCheckerService _apiChecker;
        private readonly WebSocketServerService _wsServer;
        private readonly GlobalHotkeyService _globalHotkey;
        private readonly GlobalHotkeyService _globalHotkeyPosition;
        private readonly GlobalHotkeyService _globalHotkeyMacro;
        private readonly MacroRunnerService _macroRunner;
        private readonly InputGateService _inputGate;
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

        private string _profileRankInfo = "";
        public string ProfileRankInfo
        {
            get => _profileRankInfo;
            set { _profileRankInfo = value; OnPropertyChanged(); }
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

        // 热键自动化状态展示
        private string _macroStatusText = "";
        public string MacroStatusText
        {
            get => _macroStatusText;
            set { _macroStatusText = value; OnPropertyChanged(); }
        }

        private System.Windows.Media.Brush _macroStatusBrush = System.Windows.Media.Brushes.Gray;
        public System.Windows.Media.Brush MacroStatusBrush
        {
            get => _macroStatusBrush;
            set { _macroStatusBrush = value; OnPropertyChanged(); }
        }

        /// <summary>已配置热键序列的可视化按键芯片（如 H、Q、Ctrl+A）。</summary>
        public ObservableCollection<string> MacroKeyChips { get; } = new();

        // Games Tab
        public ObservableCollection<MatchHistoryItem> Games { get; } = new ObservableCollection<MatchHistoryItem>();
        public ObservableCollection<string> SearchHistory { get; }

        // Commands
        public ICommand SearchPlayerCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ToggleOverlayCommand { get; }
        public ICommand ChangeOverlayPositionCommand { get; }
        public ICommand OpenLinkCommand { get; }
        public ICommand MacroTestCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _apiChecker = new ApiCheckerService(_settingsService);
            _wsServer = new WebSocketServerService(_settingsService.Current.WebsocketPort);
            _globalHotkey = new GlobalHotkeyService();
            _globalHotkeyPosition = new GlobalHotkeyService();
            // 输入闸门钩子不常驻：仅宏运行期间挂载（引擎 Start 挂载，结束/停止时经 OnMacroStatus 卸载）
            _inputGate = new InputGateService();
            _globalHotkeyMacro = new GlobalHotkeyService();
            _macroRunner = new MacroRunnerService(msg => {
                try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} macro {msg}{Environment.NewLine}"); } catch { }
            }, _inputGate);
            _macroRunner.StatusChanged += OnMacroStatus;
            SearchHistory = new ObservableCollection<string>(_settingsService.Current.SearchHistory ?? new List<string>());
            Settings.PropertyChanged += OnSettingsPropertyChanged;
            RefreshMacroKeyChips();

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
            MacroTestCommand = new RelayCommand(_ => TestMacro());

            UpdateProfileDisplay();
        }

        public void Start()
        {
            _wsServer.Start();
            _apiChecker.Start();
            
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                _overlayWindow = new OverlayWindow(_settingsService.Current);
                UpdateHotkeyRegistration();

                // Load history & rank if profile exists
                if (!string.IsNullOrEmpty(Settings.ProfileId))
                {
                   Task.Run(async () => { await RefreshHistory(); await RefreshProfileRank(); });
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

                // Position hotkey
                HotkeyManager.Current.Remove("ToggleOverlayPosition");
                _globalHotkeyPosition.Stop();
                if (!string.IsNullOrEmpty(Settings.OverlayPositionHotkey))
                {
                    var posParts = Settings.OverlayPositionHotkey.Split('+');
                    ModifierKeys posModifiers = ModifierKeys.None;
                    Key posKey = Key.None;

                    foreach (var part in posParts)
                    {
                        if (Enum.TryParse(part, true, out Key k))
                        {
                             if (k == Key.LeftCtrl || k == Key.RightCtrl) posModifiers |= ModifierKeys.Control;
                             else if (k == Key.LeftShift || k == Key.RightShift) posModifiers |= ModifierKeys.Shift;
                             else if (k == Key.LeftAlt || k == Key.RightAlt) posModifiers |= ModifierKeys.Alt;
                             else posKey = k;
                        }
                        else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) posModifiers |= ModifierKeys.Control;
                        else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) posModifiers |= ModifierKeys.Shift;
                        else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) posModifiers |= ModifierKeys.Alt;
                    }

                    if (posKey != Key.None)
                    {
                        try
                        {
                            HotkeyManager.Current.Remove("ToggleOverlayPosition");
                            HotkeyManager.Current.AddOrReplace("ToggleOverlayPosition", posKey, posModifiers, (s, e) =>
                            {
                                try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} pressed position {Settings.OverlayPositionHotkey}{Environment.NewLine}"); } catch { }
                                ChangeOverlayPosition();
                            });
                            try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} registered position {Settings.OverlayPositionHotkey}{Environment.NewLine}"); } catch { }
                        }
                        catch (Exception ex)
                        {
                            try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} register-failed position {Settings.OverlayPositionHotkey} {ex}{Environment.NewLine}"); } catch { }
                            _globalHotkeyPosition.Configure(Settings.OverlayPositionHotkey, () =>
                            {
                                try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} hook-pressed position {Settings.OverlayPositionHotkey}{Environment.NewLine}"); } catch { }
                                ChangeOverlayPosition();
                            });
                            _globalHotkeyPosition.Start();
                        }
                    }
                }
                // Macro trigger hotkey（热键自动化触发热键）
                HotkeyManager.Current.Remove("ToggleMacro");
                _globalHotkeyMacro.Stop();
                if (!Settings.MacroEnabled)
                {
                    // 总开关关闭：停止正在执行的宏，不注册触发热键
                    _macroRunner.Stop(Settings);
                }
                else if (!string.IsNullOrEmpty(Settings.MacroTriggerHotkey))
                {
                    if (MacroTriggerHasConflict())
                    {
                        bool zh = string.Equals(Settings.Language, "zh-CN", StringComparison.OrdinalIgnoreCase);
                        MacroStatusText = zh ? "触发热键与覆盖层热键冲突，未注册" : "Trigger hotkey conflicts with overlay hotkeys, not registered";
                        MacroStatusBrush = System.Windows.Media.Brushes.OrangeRed;
                    }
                    else if (TryParseHotkey(Settings.MacroTriggerHotkey, out var macroKey, out var macroModifiers) && macroKey != Key.None)
                    {
                        try
                        {
                            HotkeyManager.Current.AddOrReplace("ToggleMacro", macroKey, macroModifiers, (s, e) =>
                            {
                                try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} pressed macro {Settings.MacroTriggerHotkey}{Environment.NewLine}"); } catch { }
                                ToggleMacro();
                            });
                            try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} registered macro {Settings.MacroTriggerHotkey}{Environment.NewLine}"); } catch { }
                        }
                        catch (Exception ex)
                        {
                            try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} register-failed macro {Settings.MacroTriggerHotkey} {ex}{Environment.NewLine}"); } catch { }
                            _globalHotkeyMacro.Configure(Settings.MacroTriggerHotkey, () =>
                            {
                                try { File.AppendAllText(LogPaths.Get("hotkey.log"), $"{DateTime.Now:O} hook-pressed macro {Settings.MacroTriggerHotkey}{Environment.NewLine}"); } catch { }
                                ToggleMacro();
                            });
                            _globalHotkeyMacro.Start();
                        }
                    }
                }
            }
            catch { /* Ignore invalid hotkeys */ }
        }

        /// <summary>触发热键切换宏的启动/停止。</summary>
        public void ToggleMacro()
        {
            if (_macroRunner.IsRunning)
            {
                _macroRunner.Stop(Settings);
            }
            else
            {
                _macroRunner.Start(Settings);
            }
        }

        /// <summary>测试按钮：立即执行一轮热键序列（发送到当前焦点窗口）。</summary>
        private void TestMacro()
        {
            if (_macroRunner.IsRunning) return;
            _macroRunner.RunOnce(Settings);
        }

        /// <summary>宏引擎状态回报，统一调度回 UI 线程刷新。</summary>
        private void OnMacroStatus(MacroRunStatus status)
        {
            try
            {
                // BeginInvoke：宏循环线程永不因 UI 繁忙而阻塞
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    MacroStatusText = status.Message;
                    MacroStatusBrush = status.IsError ? System.Windows.Media.Brushes.OrangeRed
                                   : status.IsRunning ? System.Windows.Media.Brushes.LimeGreen
                                   : System.Windows.Media.Brushes.Gray;
                    if (!status.IsRunning) _inputGate.Stop(); // 宏结束即卸载输入闸门钩子，恢复全系统零开销
                });
            }
            catch { /* Dispatcher 可能正在关闭 */ }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.MacroSequence))
            {
                RefreshMacroKeyChips();
            }
        }

        private void RefreshMacroKeyChips()
        {
            MacroKeyChips.Clear();
            foreach (var step in MacroRunnerService.ParseSequence(Settings.MacroSequence))
            {
                MacroKeyChips.Add(MacroRunnerService.StepToDisplay(step));
            }
        }

        /// <summary>解析热键字符串为（主键, 修饰键）；无法解析出主键时返回 false。</summary>
        private static bool TryParseHotkey(string hotkey, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;
            if (string.IsNullOrWhiteSpace(hotkey)) return false;

            foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Control;
                    continue;
                }
                if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Shift;
                    continue;
                }
                if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Alt;
                    continue;
                }
                if (Enum.TryParse(part, true, out Key k))
                {
                    if (k == Key.LeftCtrl || k == Key.RightCtrl) { modifiers |= ModifierKeys.Control; continue; }
                    if (k == Key.LeftShift || k == Key.RightShift) { modifiers |= ModifierKeys.Shift; continue; }
                    if (k == Key.LeftAlt || k == Key.RightAlt) { modifiers |= ModifierKeys.Alt; continue; }
                    key = k;
                }
            }
            return key != Key.None;
        }

        /// <summary>热键字符串归一化为 "Ctrl+Shift+Alt+主键" 形式，用于冲突比较。</summary>
        public static string NormalizeHotkeyString(string? hotkey)
        {
            if (string.IsNullOrWhiteSpace(hotkey)) return "";
            if (!TryParseHotkey(hotkey, out var key, out var modifiers) || key == Key.None) return "";

            var sb = new StringBuilder();
            if ((modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");
            sb.Append(key.ToString());
            return sb.ToString();
        }

        /// <summary>宏触发热键是否与覆盖层显示/位置热键冲突。</summary>
        public bool MacroTriggerHasConflict()
        {
            var trigger = NormalizeHotkeyString(Settings.MacroTriggerHotkey);
            if (trigger.Length == 0) return false;
            return trigger == NormalizeHotkeyString(Settings.OverlayHotkey)
                || trigger == NormalizeHotkeyString(Settings.OverlayPositionHotkey);
        }

        public void Stop()
        {
            _apiChecker.Stop();
            _wsServer.Stop();
            _globalHotkey.Stop();
            _globalHotkeyPosition.Stop();
            _globalHotkeyMacro.Stop();
            _macroRunner.Stop(Settings); // 应用退出策略：程序退出时自动停止热键自动化
            _inputGate.Stop(); // 卸载输入闸门钩子
            _overlayWindow?.SaveState();
            _overlayWindow?.Close();
            _settingsService.Save();
        }

        private async Task SearchPlayer()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                SearchStatusText = "请输入用户ID";
                SearchStatusBrush = System.Windows.Media.Brushes.OrangeRed;
                return;
            }
            
            var query = SearchQuery.Trim();
            var player = await _apiChecker.FindPlayer(query);
            if (player != null)
            {
                AddSearchHistory(query);
                Settings.ProfileId = player["profile_id"]?.ToString();
                Settings.PlayerName = player["name"]?.ToString();
                Settings.SteamId = player["steam_id"]?.ToString();
                
                UpdateProfileDisplay();
                _settingsService.Save();

                SearchStatusText = "ID Found";
                SearchStatusBrush = System.Windows.Media.Brushes.LimeGreen;
                 
                // Refresh history
                await RefreshHistory();
                await RefreshProfileRank();
                await UpdateOverlayWithLastGame();

                // 每次搜索都回到“字体大小基准”尺寸并贴合内容（1px 边距），避免多次搜索后覆盖层逐次变小
                _overlayWindow?.ResetToBaseSize();
            }
            else if (!string.IsNullOrEmpty(_apiChecker.LastError))
            {
                SearchStatusText = string.Equals(Settings.Language, "zh-CN", StringComparison.OrdinalIgnoreCase)
                    ? "网络错误，请检查网络连接"
                    : "Network error, please check your connection";
                SearchStatusBrush = System.Windows.Media.Brushes.OrangeRed;
            }
            else
            {
                SearchStatusText = "ID not found";
                SearchStatusBrush = System.Windows.Media.Brushes.OrangeRed;
            }
        }

        private void AddSearchHistory(string query)
        {
            var normalized = query.Trim();
            if (string.IsNullOrEmpty(normalized)) return;

            var existing = SearchHistory.FirstOrDefault(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                SearchHistory.Remove(existing);
            }

            SearchHistory.Insert(0, normalized);

            while (SearchHistory.Count > 20)
            {
                SearchHistory.RemoveAt(SearchHistory.Count - 1);
            }

            Settings.SearchHistory = SearchHistory.ToList();
        }

        public void RemoveSearchHistory(string query)
        {
            var existing = SearchHistory.FirstOrDefault(x => string.Equals(x, query, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;

            SearchHistory.Remove(existing);
            Settings.SearchHistory = SearchHistory.ToList();
            _settingsService.Save();

            if (string.Equals(SearchQuery, existing, StringComparison.OrdinalIgnoreCase))
            {
                SearchQuery = "";
            }
        }

        private async Task UpdateOverlayWithLastGame()
        {
            var lastGame = await _apiChecker.GetLastGame();
            if (lastGame == null) return;

            var processed = GameProcessor.ProcessGame(lastGame, _settingsService.Current);
            _wsServer.Send("player_data", processed);

            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                _overlayWindow?.UpdateData(processed);
                if (_settingsService.Current.OpenOverlayOnNewGame)
                {
                    _overlayWindow?.Show();
                }
            });
        }

        private void UpdateProfileDisplay()
        {
            ProfileRankInfo = "";

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
                                item.Map = MapNameTranslator.Translate(gameObject["map"]?.ToString(), Settings.Language);
                                if (string.IsNullOrEmpty(item.Map)) item.Map = "?";
                                item.MatchId = gameObject["game_id"]?.ToString() ?? "";
                                item.ProfileId = Settings.ProfileId ?? "";
                                var rawMode = gameObject["kind"]?.ToString() ?? "?";
                                item.Mode = TranslateMode(rawMode, Settings.Language);
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
                                        var civ = CivNameTranslator.Translate(player?["civilization"]?.ToString(), Settings.Language);
                                        return new PlayerDisplayInfo { Name = name, ProfileId = profileId, Civ = civ, CivColor = Settings.CivStatsColor };
                                    }).ToList() ?? new List<PlayerDisplayInfo>();

                                    item.Team2Players = t2?.Select(p => {
                                        var player = p["player"] as JObject;
                                        var name = player?["name"]?.ToString() ?? "?";
                                        var profileId = player?["profile_id"]?.ToString() ?? "";
                                        var civ = CivNameTranslator.Translate(player?["civilization"]?.ToString(), Settings.Language);
                                        return new PlayerDisplayInfo { Name = name, ProfileId = profileId, Civ = civ, CivColor = Settings.CivStatsColor };
                                    }).ToList() ?? new List<PlayerDisplayInfo>();

                                    item.Team1Display = string.Join(Environment.NewLine, item.Team1Players.Select(p => $"{p.Name} ({p.Civ})"));
                                    item.Team2Display = string.Join(Environment.NewLine, item.Team2Players.Select(p => $"{p.Name} ({p.Civ})"));
                                    
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
                                                    item.Result = TranslateResult(player?["result"]?.ToString() ?? "unknown", Settings.Language);
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

        /// <summary>刷新信息栏中的排位段位分显示。</summary>
        private async Task RefreshProfileRank()
        {
            var profile = await _apiChecker.GetPlayerProfile();
            var text = BuildRankText(profile?["modes"] as JObject);
            System.Windows.Application.Current.Dispatcher.Invoke(() => ProfileRankInfo = text);
        }

        /// <summary>从玩家档案 modes 中提取当前排位（1v1 / 团队）段位分并拼接显示文本。</summary>
        private string BuildRankText(JObject? modes)
        {
            if (modes == null) return "";

            bool zh = string.Equals(Settings.Language, "zh-CN", StringComparison.OrdinalIgnoreCase);
            var parts = new List<string>();
            AddRankPart(parts, modes, "1v1", zh, "rm_solo", "rm_1v1");
            AddRankPart(parts, modes, zh ? "团队" : "Team", zh, "rm_team");

            string label = zh ? "段位分：" : "Ranked: ";
            return parts.Count == 0 ? label + (zh ? "未定级" : "Unranked") : label + string.Join(" · ", parts);
        }

        private static void AddRankPart(List<string> parts, JObject modes, string modeName, bool zh, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (modes[key] is not JObject mode) continue;
                var rating = mode["rating"];
                if (rating == null || rating.Type == JTokenType.Null) continue;

                var level = FormatRankLevel(mode["rank_level"]?.ToString(), zh);
                parts.Add(string.IsNullOrEmpty(level)
                    ? $"{modeName} {rating}"
                    : zh ? $"{modeName} {rating}（{level}）" : $"{modeName} {rating} ({level})");
                return;
            }
        }

        private static string FormatRankLevel(string? level, bool zh)
        {
            if (string.IsNullOrEmpty(level) || string.Equals(level, "unranked", StringComparison.OrdinalIgnoreCase))
                return "";

            var segments = level.Split('_');
            string tierName = segments[0].ToLowerInvariant() switch
            {
                "bronze" => zh ? "青铜" : "Bronze",
                "silver" => zh ? "白银" : "Silver",
                "gold" => zh ? "黄金" : "Gold",
                "platinum" => zh ? "白金" : "Platinum",
                "diamond" => zh ? "钻石" : "Diamond",
                "conqueror" => zh ? "征服者" : "Conqueror",
                _ => segments[0]
            };

            string roman = segments.Length > 1
                ? segments[1] switch { "1" => "I", "2" => "II", "3" => "III", _ => segments[1] }
                : "";

            if (string.IsNullOrEmpty(roman)) return tierName;
            return zh ? tierName + roman : $"{tierName} {roman}";
        }

        private static string TranslateMode(string mode, string? language)
        {
            if (!string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase))
                return mode;
            if (mode.StartsWith("rm", StringComparison.OrdinalIgnoreCase))
                return "排位赛" + mode.Substring(2);
            return mode;
        }

        private static string TranslateResult(string result, string? language)
        {
            if (!string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase))
                return result;
            if (string.Equals(result, "win", StringComparison.OrdinalIgnoreCase)) return "赢";
            if (string.Equals(result, "loss", StringComparison.OrdinalIgnoreCase)) return "输";
            return result;
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

        public void SaveCurrentSettings()
        {
            _settingsService.Save();
        }

        public async Task RefreshLocalizedDataAfterLanguageChange()
        {
            await RefreshHistory();
            await RefreshProfileRank();
            await UpdateOverlayWithLastGame();
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
