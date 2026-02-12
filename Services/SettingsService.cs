using AoE4OverlayCS.Models;
using Newtonsoft.Json;
using System;
using System.IO;

namespace AoE4OverlayCS.Services
{
    public class SettingsService
    {
        private readonly string _configPath;
        public AppSettings Current { get; private set; }

        public SettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "AoE4_Overlay_CS");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            _configPath = Path.Combine(folder, "config.json");
            
            Current = new AppSettings();
            Load();
        }

        public void Load()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings != null) Current = settings;
                }
                catch { /* Ignore load errors */ }
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Current, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch { /* Ignore save errors */ }
        }
    }
}
