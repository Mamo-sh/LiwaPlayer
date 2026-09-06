using LiwaPlayer.Models;
using System;
using System.IO;
using System.Text.Json;

namespace LiwaPlayer.Services
{
    public class SettingsService
    {
        private readonly string _settingsFile;

        public Settings Current { get; private set; } = new();

        public SettingsService()
        {
            _settingsFile = Path.Combine(AppPaths.DataFolder, "Settings.json");

            Load();
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_settingsFile))
                    return;

                var json = File.ReadAllText(_settingsFile);

                if (string.IsNullOrWhiteSpace(json))
                    return;

                Current = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
            catch
            {
                // Bozuk ayar dosyası uygulamayı düşürmesin, varsayılanlarla devam et
                Current = new Settings();
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(
                    Current,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_settingsFile, json);
            }
            catch
            {
                // Ayar kaydedilemezse sessizce geç
            }
        }
    }
}
