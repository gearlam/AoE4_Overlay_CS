using Newtonsoft.Json;

namespace AoE4OverlayCS.Models
{
    public class TranslationSettings : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string Model { get; set; } = "";
        public string ProxyMode { get; set; } = "Direct";
        public string ProxyUrl { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 90;
        public string ProtectedApiKey { get; set; } = "";
        [JsonIgnore]
        public string ApiKey { get; set; } = "";
        public bool Enabled { get; set; } = false;
        public string Hotkey { get; set; } = "F9";
        private string _sourceLanguage = "Chinese";
        private string _targetLanguage = "English";
        public string SourceLanguage
        {
            get => _sourceLanguage;
            set { _sourceLanguage = value; PropertyChanged?.Invoke(this, new(nameof(SourceLanguage))); }
        }
        public string TargetLanguage
        {
            get => _targetLanguage;
            set { _targetLanguage = value; PropertyChanged?.Invoke(this, new(nameof(TargetLanguage))); }
        }
    }

    public class TranslationRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public string SourceLanguage { get; set; } = "";
        public string TargetLanguage { get; set; } = "";
        public string Game { get; set; } = "";
        public string Original { get; set; } = "";
        public string Translated { get; set; } = "";
        [JsonIgnore]
        public string Caption => $"{CreatedAt.ToLocalTime():g} · {Game} · {SourceLanguage} → {TargetLanguage}";
    }
}
