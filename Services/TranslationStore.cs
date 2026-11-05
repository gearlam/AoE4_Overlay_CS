using AoE4OverlayCS.Models;
using Newtonsoft.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AoE4OverlayCS.Services
{
    public sealed class TranslationStore
    {
        public string DirectoryPath { get; }
        public string HistoryDirectoryPath { get; }
        public TranslationStore(string? directory = null, string? historyDirectory = null)
        {
            DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AoEOverlay", "translation");
            HistoryDirectoryPath = historyDirectory ?? Path.Combine(directory ?? AppContext.BaseDirectory, "translation_history");
        }

        public TranslationSettings LoadSettings()
        {
            var settings = Read<TranslationSettings>("settings.json") ?? new TranslationSettings();
            if (!string.IsNullOrEmpty(settings.ProtectedApiKey))
            {
                try
                {
                    settings.ApiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(settings.ProtectedApiKey), null, DataProtectionScope.CurrentUser));
                }
                catch (Exception ex) when (ex is CryptographicException or FormatException)
                {
                    throw new InvalidOperationException("Cannot decrypt the saved API key. Restore the settings file or remove settings.json and configure again.", ex);
                }
            }
            return settings;
        }

        public void SaveSettings(TranslationSettings settings)
        {
            settings.ProtectedApiKey = string.IsNullOrEmpty(settings.ApiKey) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.ApiKey), null, DataProtectionScope.CurrentUser));
            Write(Path.Combine(DirectoryPath, "settings.json"), settings);
        }

        public List<TranslationRecord> LoadHistory()
        {
            Directory.CreateDirectory(HistoryDirectoryPath);
            var path = Path.Combine(HistoryDirectoryPath, "history.json");
            if (File.Exists(path)) return JsonConvert.DeserializeObject<List<TranslationRecord>>(File.ReadAllText(path)) ?? new();
            var legacyPath = Path.Combine(DirectoryPath, "history.json");
            if (!File.Exists(legacyPath)) return new();
            var records = JsonConvert.DeserializeObject<List<TranslationRecord>>(File.ReadAllText(legacyPath)) ?? new();
            SaveHistory(records); // Remove the old copy only after the new file is safely written.
            File.Delete(legacyPath);
            return records;
        }
        public void SaveHistory(IEnumerable<TranslationRecord> records) => Write(Path.Combine(HistoryDirectoryPath, "history.json"), records);

        private T? Read<T>(string name)
        {
            var path = Path.Combine(DirectoryPath, name);
            if (!File.Exists(path)) return default;
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }

        private static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(value, Formatting.Indented));
            File.Move(temp, path, overwrite: true);
        }
    }
}
