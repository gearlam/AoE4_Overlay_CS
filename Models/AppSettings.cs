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

        private int _interval = 15;
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

        private double[]? _overlayGeometry;
        public double[]? OverlayGeometry { get => _overlayGeometry; set { _overlayGeometry = value; OnPropertyChanged(); } } // x, y, w, h

        private int _fontSize = 12;
        public int FontSize { get => _fontSize; set { _fontSize = value; OnPropertyChanged(); } }

        private int _maxGamesHistory = 100;
        public int MaxGamesHistory { get => _maxGamesHistory; set { _maxGamesHistory = value; OnPropertyChanged(); } }

        private string _civStatsColor = "#BC8AEA";
        public string CivStatsColor { get => _civStatsColor; set { _civStatsColor = value; OnPropertyChanged(); } }

        private bool _openOverlayOnNewGame = true;
        public bool OpenOverlayOnNewGame { get => _openOverlayOnNewGame; set { _openOverlayOnNewGame = value; OnPropertyChanged(); } }

        public Dictionary<string, bool> ShowGraph { get; set; } = new() { { "1", true }, { "2", true }, { "3", true }, { "4", true } };
        
        public List<List<object>> TeamColors { get; set; } = new() {
            new List<object> { 74, 255, 2, 0.35 },
            new List<object> { 3, 179, 255, 0.35 },
            new List<object> { 255, 0, 0, 0.35 },
            new List<object> { 255, 0, 255, 0.35 },
            new List<object> { 255, 255, 0, 0.35 }
        };
    }
}
