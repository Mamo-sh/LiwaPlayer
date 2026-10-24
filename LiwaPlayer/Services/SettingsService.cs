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

                Migrate(Current);
            }
            catch
            {
                // Bozuk ayar dosyası uygulamayı düşürmesin, varsayılanlarla devam et
                Current = new Settings();
            }
        }

        // Eski ayar dosyalarını yeni alanlara taşır
        private static void Migrate(Settings s)
        {
            // Eski varsayılan "Normal" ağ önbelleği 3 sn idi ve şarkıların geç
            // açılmasına yol açıyordu; yeni "Normal" 1,5 sn
            if (s.NetworkCachingMs == 3000)
                s.NetworkCachingMs = 1500;

            // Tekrar modu daha önce sadece aç/kapa idi
            if (s.RepeatModeValue < 0)
                s.RepeatModeValue = s.Repeat ? (int)RepeatMode.All : (int)RepeatMode.Off;

            // Elle bozulmuş değerler uygulamayı bozmasın
            s.ConnectionCheckSeconds = Math.Clamp(s.ConnectionCheckSeconds, 15, 3600);

            if (s.AutoLightStartMinutes is < 0 or >= 1440)
                s.AutoLightStartMinutes = 8 * 60;

            if (s.AutoDarkStartMinutes is < 0 or >= 1440 ||
                s.AutoDarkStartMinutes == s.AutoLightStartMinutes)
                s.AutoDarkStartMinutes = (s.AutoLightStartMinutes + 12 * 60) % 1440;
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
