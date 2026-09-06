using System;
using System.IO;

namespace LiwaPlayer.Services
{
    // Basit dosya günlüğü: hata ayıklama için Data\log.txt'ye yazar.
    // POS makinelerinde sorun bildirilirken bu dosyanın içeriği yeterlidir.
    public static class LogService
    {
        private static readonly object Lock = new();

        private static readonly string LogFile =
            Path.Combine(AppPaths.DataFolder, "log.txt");

        public static void Write(string context, Exception ex) =>
            Write($"{context}: {ex.GetType().Name}: {ex.Message}" +
                  (ex.InnerException != null ? $" | İç: {ex.InnerException.Message}" : ""));

        public static void Write(string message)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);

                    // Dosya büyürse sıfırla (POS diskini doldurmasın)
                    if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 512 * 1024)
                        File.Delete(LogFile);

                    File.AppendAllText(LogFile,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Günlük yazılamazsa uygulama etkilenmesin
            }
        }
    }
}
