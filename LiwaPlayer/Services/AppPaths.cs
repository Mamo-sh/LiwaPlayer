using System;
using System.IO;

namespace LiwaPlayer.Services
{
    // Tüm kalıcı veriler (playlist, ayarlar, oturum, günlük) LocalAppData'da tutulur.
    // Exe'nin yanındaki klasör salt okunur olabilir (Program Files, USB) veya sürüm
    // güncellemesinde değişebilir; LocalAppData her ikisinden de etkilenmez.
    public static class AppPaths
    {
        public static string DataFolder { get; }

        static AppPaths()
        {
            // LIWAPLAYER_DATA ortam değişkeni verilmişse veri o klasörde tutulur.
            // Gerçek kullanıcı verisine dokunmadan deneme/test yapmak ve uygulamayı
            // taşınabilir (USB) kullanmak için; verilmezse normal LocalAppData yolu.
            var custom = Environment.GetEnvironmentVariable("LIWAPLAYER_DATA");

            if (!string.IsNullOrWhiteSpace(custom))
            {
                DataFolder = Path.GetFullPath(custom);

                Directory.CreateDirectory(DataFolder);
                return;
            }

            DataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiwaPlayer",
                "Data");

            Directory.CreateDirectory(DataFolder);

            MigrateFromExeFolder();
        }

        // Eski sürümler veriyi exe yanında tutuyordu; ilk çalıştırmada
        // oradaki dosyalar (üzerine yazmadan) yeni konuma taşınır
        private static void MigrateFromExeFolder()
        {
            try
            {
                var oldFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

                if (!Directory.Exists(oldFolder))
                    return;

                foreach (var name in new[] { "Playlists.json", "Playlist.json", "Settings.json", "auth.bin" })
                {
                    var source = Path.Combine(oldFolder, name);
                    var target = Path.Combine(DataFolder, name);

                    if (File.Exists(source) && !File.Exists(target))
                        File.Copy(source, target);
                }
            }
            catch
            {
                // Göç başarısız olsa bile uygulama boş veriyle açılabilmeli
            }
        }
    }
}
