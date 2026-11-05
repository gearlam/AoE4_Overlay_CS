using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AoE4OverlayCS.Models
{
    public class AppSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private int _websocketPort = 7307;
        public int WebsocketPort { get => _websocketPort; set { _websocketPort = value; OnPropertyChanged(); } }

        private bool _logMatches = true;
        public bool LogMatches { get => _logMatches; set { _logMatches = value; OnPropertyChanged(); } }

        private int _interval = 60;
        public int Interval { get => _interval; set { _interval = value; OnPropertyChanged(); } }

        private double _appWidth = 900;
        public double AppWidth { get => _appWidth; set { _appWidth = value; OnPropertyChanged(); } }

        private double _appHeight = 600;
        public double AppHeight { get => _appHeight; set { _appHeight = value; OnPropertyChanged(); } }

        private string? _steamId;
        public string? SteamId { get => _steamId; set { _steamId = value; OnPropertyChanged(); } }

        private string? _profileId;
        public string? ProfileId { get => _profileId; set { _profileId = value; OnPropertyChanged(); } }

        private string? _playerName;
        public string? PlayerName { get => _playerName; set { _playerName = value; OnPropertyChanged(); } }

        private string _game = "aoe4";
        public string Game
        {
            get => _game;
            set
            {
                var next = string.Equals(value, "aoe2", StringComparison.OrdinalIgnoreCase) ? "aoe2" : "aoe4";
                if (_game == next) return;
                _game = next;
                OnPropertyChanged();
            }
        }

        public bool IsAoe2Game() => string.Equals(Game, "aoe2", StringComparison.OrdinalIgnoreCase);

        private string? _aoe4ProfileId;
        public string? Aoe4ProfileId { get => _aoe4ProfileId; set { _aoe4ProfileId = value; OnPropertyChanged(); } }

        private string? _aoe4SteamId;
        public string? Aoe4SteamId { get => _aoe4SteamId; set { _aoe4SteamId = value; OnPropertyChanged(); } }

        private string? _aoe4PlayerName;
        public string? Aoe4PlayerName { get => _aoe4PlayerName; set { _aoe4PlayerName = value; OnPropertyChanged(); } }

        private string? _aoe2ProfileId;
        public string? Aoe2ProfileId { get => _aoe2ProfileId; set { _aoe2ProfileId = value; OnPropertyChanged(); } }

        private string? _aoe2SteamId;
        public string? Aoe2SteamId { get => _aoe2SteamId; set { _aoe2SteamId = value; OnPropertyChanged(); } }

        private string? _aoe2PlayerName;
        public string? Aoe2PlayerName { get => _aoe2PlayerName; set { _aoe2PlayerName = value; OnPropertyChanged(); } }

        public void RememberActiveProfile()
        {
            RememberSearchIndex();
            if (IsAoe2Game())
            {
                Aoe2ProfileId = ProfileId;
                Aoe2SteamId = SteamId;
                Aoe2PlayerName = PlayerName;
            }
            else
            {
                Aoe4ProfileId = ProfileId;
                Aoe4SteamId = SteamId;
                Aoe4PlayerName = PlayerName;
            }
        }

        public void LoadActiveProfile()
        {
            if (IsAoe2Game())
            {
                ProfileId = Aoe2ProfileId;
                SteamId = Aoe2SteamId;
                PlayerName = Aoe2PlayerName;
                SearchHistory = CopyList(Aoe2SearchHistory);
                ActiveSearchQuery = Aoe2SearchQuery ?? "";
            }
            else
            {
                ProfileId = Aoe4ProfileId;
                SteamId = Aoe4SteamId;
                PlayerName = Aoe4PlayerName;
                SearchHistory = CopyList(Aoe4SearchHistory);
                ActiveSearchQuery = Aoe4SearchQuery ?? "";
            }
        }

        /// <summary>把当前搜索框和历史写进正在轮询的那一代，不碰到另一代。</summary>
        public void RememberSearchIndex()
        {
            var copy = CopyList(SearchHistory);
            SearchHistory = copy;
            if (IsAoe2Game())
            {
                Aoe2SearchHistory = CopyList(copy);
                Aoe2SearchQuery = ActiveSearchQuery ?? "";
            }
            else
            {
                Aoe4SearchHistory = CopyList(copy);
                Aoe4SearchQuery = ActiveSearchQuery ?? "";
            }
        }

        public void EnsureProfileSlots()
        {
            Aoe4SearchHistory ??= new List<string>();
            Aoe2SearchHistory ??= new List<string>();
            SearchHistory ??= new List<string>();

            bool indexesEmpty = Aoe4SearchHistory.Count == 0 && Aoe2SearchHistory.Count == 0;
            if (indexesEmpty && SearchHistory.Count > 0)
            {
                if (IsAoe2Game())
                    Aoe2SearchHistory = CopyList(SearchHistory);
                else
                    Aoe4SearchHistory = CopyList(SearchHistory);
            }

            if (!IsAoe2Game() && string.IsNullOrEmpty(Aoe4ProfileId) && !string.IsNullOrEmpty(ProfileId))
                RememberActiveProfile();
            else
                LoadActiveProfile();
        }

        private static List<string> CopyList(List<string>? source)
        {
            var copy = new List<string>();
            if (source == null) return copy;
            foreach (var item in source)
            {
                var text = item?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                if (copy.Exists(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase))) continue;
                copy.Add(text);
                if (copy.Count >= 20) break;
            }
            return copy;
        }

        private string _overlayHotkey = "";
        public string OverlayHotkey { get => _overlayHotkey; set { _overlayHotkey = value; OnPropertyChanged(); } }

        private string _overlayPositionHotkey = "";
        public string OverlayPositionHotkey { get => _overlayPositionHotkey; set { _overlayPositionHotkey = value; OnPropertyChanged(); } }

        // 热键自动化（按键精灵式宏）配置：MacroRepeatCount = 0 表示无限循环
        private bool _macroEnabled = false;
        public bool MacroEnabled { get => _macroEnabled; set { _macroEnabled = value; OnPropertyChanged(); } }

        private string _macroTriggerHotkey = "";
        public string MacroTriggerHotkey { get => _macroTriggerHotkey; set { _macroTriggerHotkey = value; OnPropertyChanged(); } }

        private string _macroSequence = "";
        public string MacroSequence { get => _macroSequence; set { _macroSequence = value; OnPropertyChanged(); } }

        private int _macroIntervalSeconds = 5;
        public int MacroIntervalSeconds { get => _macroIntervalSeconds; set { _macroIntervalSeconds = value; OnPropertyChanged(); } }

        private int _macroRepeatCount = 0; // 默认无限循环，符合“热键启动后循环直到手动停止”的使用预期
        public int MacroRepeatCount { get => _macroRepeatCount; set { _macroRepeatCount = value; OnPropertyChanged(); } }

        // 操作避让：注入前等待系统键鼠空闲该毫秒数，避免打断玩家正在进行的游戏操作；0 = 不避让
        private int _macroIdleWaitMs = 800;
        public int MacroIdleWaitMs { get => _macroIdleWaitMs; set { _macroIdleWaitMs = value; OnPropertyChanged(); } }

        // Shift 监控：勾选后，无限循环只在间隔结束前 1 秒检测物理 Shift；按住则等到松开再发送
        private bool _macroMonitorShift = false;
        public bool MacroMonitorShift { get => _macroMonitorShift; set { _macroMonitorShift = value; OnPropertyChanged(); } }

        private double[]? _overlayGeometry;
        public double[]? OverlayGeometry { get => _overlayGeometry; set { _overlayGeometry = value; OnPropertyChanged(); } } // x, y, w, h

        private int _fontSize = 12;
        public int FontSize { get => _fontSize; set { _fontSize = value; OnPropertyChanged(); } }

        private double _teamGap = 12;
        public double TeamGap { get => _teamGap; set { _teamGap = value; OnPropertyChanged(); } }

        private string _overlayBackgroundColor = "#000000";
        public string OverlayBackgroundColor { get => _overlayBackgroundColor; set { _overlayBackgroundColor = value; OnPropertyChanged(); } }

        private double _overlayBackgroundOpacity = 0.5;
        public double OverlayBackgroundOpacity { get => _overlayBackgroundOpacity; set { _overlayBackgroundOpacity = value; OnPropertyChanged(); } }

        private int _maxGamesHistory = 20;
        public int MaxGamesHistory { get => _maxGamesHistory; set { _maxGamesHistory = value; OnPropertyChanged(); } }

        private string _civStatsColor = "#BC8AEA";
        public string CivStatsColor { get => _civStatsColor; set { _civStatsColor = value; OnPropertyChanged(); } }

        private bool _openOverlayOnNewGame = true;
        public bool OpenOverlayOnNewGame { get => _openOverlayOnNewGame; set { _openOverlayOnNewGame = value; OnPropertyChanged(); } }

        private string _language = "en-US";
        public string Language { get => _language; set { _language = value; OnPropertyChanged(); } }

        public List<string> SearchHistory { get; set; } = new();

        public List<string> Aoe4SearchHistory { get; set; } = new();

        public List<string> Aoe2SearchHistory { get; set; } = new();

        public string ActiveSearchQuery { get; set; } = "";

        public string Aoe4SearchQuery { get; set; } = "";

        public string Aoe2SearchQuery { get; set; } = "";
        
        public List<List<object>> TeamColors { get; set; } = new() {
            new List<object> { 74, 255, 2, 0.35 },
            new List<object> { 3, 179, 255, 0.35 },
            new List<object> { 255, 0, 0, 0.35 },
            new List<object> { 255, 0, 255, 0.35 },
            new List<object> { 255, 255, 0, 0.35 }
        };
    }
}
