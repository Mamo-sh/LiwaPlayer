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

        // Videonun sesini MP3 olarak Müzik\LiwaPlayer klasörüne indirir
        public async Task<bool> DownloadMp3Async(string videoId, IProgress<int> progress)
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

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("yt-dlp başlatılamadı.");

            var progressRegex = new Regex(@"\[download\]\s+([\d\.]+)%");

            var stderrTask = process.StandardError.ReadToEndAsync();

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
            }

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var stderr = await stderrTask;

                LogService.Write($"yt-dlp hata (kod {process.ExitCode}, {videoId}): " +
                    stderr.Split('\n').LastOrDefault(l => l.Contains("ERROR"))?.Trim());

                return false;
            }

            return true;
        }
    }
}
