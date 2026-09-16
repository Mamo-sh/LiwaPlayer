using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LiwaPlayer.Services
{
    // yt-dlp ile MP3 indirme. yt-dlp.exe ve ffmpeg.exe ilk kullanımda resmi
    // kaynaklardan (github.com/yt-dlp) indirilip LocalAppData\LiwaPlayer\Tools
    // altına konur; MP3'ler kullanıcının Müzik\LiwaPlayer klasörüne yazılır.
    public class DownloadService
    {
        private const string YtDlpUrl =
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        private const string FfmpegZipUrl =
            "https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip";

        private static readonly HttpClient Http = new();

        public static string ToolsFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiwaPlayer",
                "Tools");

        public static string MusicFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "LiwaPlayer");

        private static string YtDlpPath => Path.Combine(ToolsFolder, "yt-dlp.exe");
        private static string FfmpegPath => Path.Combine(ToolsFolder, "ffmpeg.exe");

        public bool ToolsReady => File.Exists(YtDlpPath) && File.Exists(FfmpegPath);

        // Eksik araçları indirir (tek seferlik); durum metni progress ile bildirilir
        public async Task EnsureToolsAsync(IProgress<string> status)
        {
            Directory.CreateDirectory(ToolsFolder);

            if (!File.Exists(YtDlpPath))
            {
                await DownloadFileAsync(YtDlpUrl, YtDlpPath,
                    p => status.Report($"yt-dlp indiriliyor (tek seferlik)... %{p}"));
            }

            if (!File.Exists(FfmpegPath))
            {
                var zipPath = Path.Combine(ToolsFolder, "ffmpeg.zip");

                await DownloadFileAsync(FfmpegZipUrl, zipPath,
                    p => status.Report($"ffmpeg indiriliyor (tek seferlik, ~180 MB)... %{p}"));

                status.Report("ffmpeg ayıklanıyor...");

                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var entry = zip.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("ffmpeg.exe arşivde bulunamadı.");

                    entry.ExtractToFile(FfmpegPath, overwrite: true);
                }

                try
                {
                    File.Delete(zipPath);
                }
                catch
                {
                }
            }
        }

        private static async Task DownloadFileAsync(string url, string target, Action<int> progress)
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? -1;
            long done = 0;
            int lastPercent = -1;

            await using var source = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(target);

            var buffer = new byte[81920];
            int read;

            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));

                done += read;

                if (total > 0)
                {
                    int percent = (int)(done * 100 / total);

                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress(percent);
                    }
                }
            }
        }

        // Videonun sesini MP3 olarak Müzik\LiwaPlayer klasörüne indirir.
        // Başarılıysa gerçek dosya yolunu da döndürür (İndirilen Şarkılar
        // listesine eklemek için) — yt-dlp'nin "[ExtractAudio] Destination: ..."
        // satırından ayrıştırılır.
        public async Task<(bool Success, string? FilePath)> DownloadMp3Async(
            string videoId, IProgress<int> progress)
        {
            Directory.CreateDirectory(MusicFolder);

            var psi = new ProcessStartInfo
            {
                FileName = YtDlpPath,
                Arguments =
                    "-x --audio-format mp3 --audio-quality 0 " +
                    $"--ffmpeg-location \"{ToolsFolder}\" " +
                    "--no-playlist --newline " +
                    $"-o \"{Path.Combine(MusicFolder, "%(title)s.%(ext)s")}\" " +
                    $"\"https://www.youtube.com/watch?v={videoId}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            var startTime = DateTime.Now;

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("yt-dlp başlatılamadı.");

            var progressRegex = new Regex(@"\[download\]\s+([\d\.]+)%");

            // yt-dlp sürümüne göre satır biçimi hafif değişebilir; sadece
            // ".mp3" ile biten bir "Destination:" satırı arıyoruz, ön ek serbest
            var destinationRegex = new Regex(
                @"Destination:\s*(.+\.mp3)\s*$", RegexOptions.IgnoreCase);

            string? destinationPath = null;
            var stderrLines = new System.Collections.Generic.List<string>();

            // Bazı yt-dlp sürümleri "Destination:" satırını stdout yerine
            // stderr'e yazıyor; ikisini de eş zamanlı okuyup tarıyoruz,
            // yoksa dosya diskte olsa bile yol hiç yakalanmıyordu
            var stdoutTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                {
                    var match = progressRegex.Match(line);

                    if (match.Success &&
                        double.TryParse(match.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double percent))
                    {
                        progress.Report((int)percent);
                    }

                    var destMatch = destinationRegex.Match(line);

                    if (destMatch.Success)
                        destinationPath = destMatch.Groups[1].Value.Trim();
                }
            });

            var stderrTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync()) != null)
                {
                    stderrLines.Add(line);

                    var destMatch = destinationRegex.Match(line);

                    if (destMatch.Success)
                        destinationPath ??= destMatch.Groups[1].Value.Trim();
                }
            });

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                LogService.Write($"yt-dlp hata (kod {process.ExitCode}, {videoId}): " +
                    stderrLines.LastOrDefault(l => l.Contains("ERROR"))?.Trim());

                return (false, null);
            }

            // Çıktıdan yol yakalanamadıysa (beklenmeyen bir yt-dlp biçimi),
            // klasördeki en yeni .mp3 dosyasına bakarak güvenlik ağı sağla
            if (string.IsNullOrWhiteSpace(destinationPath) || !File.Exists(destinationPath))
                destinationPath = FindNewestMp3Since(startTime);

            return (true, destinationPath);
        }

        private static string? FindNewestMp3Since(DateTime since)
        {
            try
            {
                return new DirectoryInfo(MusicFolder)
                    .GetFiles("*.mp3")
                    .Where(f => f.LastWriteTime >= since.AddSeconds(-2))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault()?.FullName;
            }
            catch
            {
                return null;
            }
        }
    }
}
