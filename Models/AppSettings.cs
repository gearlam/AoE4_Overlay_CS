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
        
        public List<List<object>> TeamColors { get; set; } = new() {
            new List<object> { 74, 255, 2, 0.35 },
            new List<object> { 3, 179, 255, 0.35 },
            new List<object> { 255, 0, 0, 0.35 },
            new List<object> { 255, 0, 255, 0.35 },
            new List<object> { 255, 255, 0, 0.35 }
        };
    }
}
