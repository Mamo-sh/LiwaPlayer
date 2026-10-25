using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiwaPlayer.Services
{
    public class UpdateInfo
    {
        public Version Version { get; set; } = new(0, 0);

        public string SetupUrl { get; set; } = "";

        public string Notes { get; set; } = "";

        public string TagName { get; set; } = "";
    }

    // GitHub Releases üzerinden güncelleme: en son release'in sürümü mevcut
    // sürümden yeniyse kurulum paketi (LiwaPlayer-Setup-*.exe) indirilir ve
    // sessiz kurulumla uygulanır; kurulum bitince uygulama kendini yeniden başlatır.
    public class UpdateService
    {
        // Güncellemelerin çekildiği GitHub deposu
        public const string RepoOwner = "Mamo-sh";
        public const string RepoName = "LiwaPlayer";

        public static bool IsConfigured => RepoOwner.Length > 0;

        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient();
            // GitHub API, User-Agent başlığı olmayan istekleri reddeder
            http.DefaultRequestHeaders.Add("User-Agent", "LiwaPlayer-Updater");
            return http;
        }

        // Güncelleme arayüzünü gerçek bir yayın çıkarmadan denemek için: yalnızca
        // yerel makineyi gösteren (localhost/127.0.0.1) bir adres LIWAPLAYER_UPDATE_API
        // ile verilirse GitHub yerine o kullanılır. Başka adresler yok sayılır.
        private static string ReleaseApiUrl
        {
            get
            {
                var custom = Environment.GetEnvironmentVariable("LIWAPLAYER_UPDATE_API");

                if (!string.IsNullOrWhiteSpace(custom) &&
                    Uri.TryCreate(custom, UriKind.Absolute, out var uri) &&
                    uri.IsLoopback)
                    return uri.ToString();

                return $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            }
        }

        public static Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

        // Yeni sürüm varsa bilgisini döndürür, güncelsek null
        public async Task<UpdateInfo?> CheckAsync()
        {
            if (!IsConfigured)
                return null;

            var json = await Http.GetStringAsync(ReleaseApiUrl);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                return null;

            if (version.CompareTo(CurrentVersion) <= 0)
                return null;

            // Release eklerinden kurulum paketini bul
            string? setupUrl = null;

            if (root.TryGetProperty("assets", out var assets))
            {
                setupUrl = assets.EnumerateArray()
                    .Select(a => a.TryGetProperty("browser_download_url", out var u)
                        ? u.GetString() ?? ""
                        : "")
                    .FirstOrDefault(u =>
                        u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        u.Contains("Setup", StringComparison.OrdinalIgnoreCase));
            }

            if (setupUrl == null)
                return null;

            return new UpdateInfo
            {
                Version = version,
                SetupUrl = setupUrl,
                TagName = tag,
                Notes = root.TryGetProperty("body", out var body)
                    ? body.GetString() ?? ""
                    : ""
            };
        }

        // Kurulum paketini indirir; (indirilen, toplam) bayt bilgisi progress ile bildirilir.
        // İptal edilirse ya da hata olursa yarım dosya silinir.
        public async Task<string> DownloadAsync(
            UpdateInfo update,
            IProgress<(long Done, long Total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var folder = Path.Combine(AppPaths.DataFolder, "..", "Update");

            Directory.CreateDirectory(folder);

            var file = Path.Combine(folder, $"LiwaPlayer-Setup-{update.Version}.exe");

            try
            {
                using var response = await Http.GetAsync(
                    update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? -1;
                long done = 0;

                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var target = File.Create(file))
                {
                    var buffer = new byte[81920];
                    int read;

                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                        done += read;

                        progress?.Report((done, total));
                    }
                }

                // Bağlantı yarıda kesilip dosya eksik kaldıysa kurulumu başlatma
                if (total > 0 && done != total)
                    throw new IOException("İndirme tamamlanamadı (dosya eksik).");

                return file;
            }
            catch
            {
                TryDelete(file);
                throw;
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
            }
        }

        // Kurulumu başlatır; çağıran taraf hemen uygulamadan çıkmalıdır.
        // /SILENT: sihirbaz sayfaları gösterilmez ama küçük bir "kuruluyor" ilerleme
        // penceresi görünür (/VERYSILENT'ta hiçbir şey görünmediğinden uygulama
        // kapanınca kullanıcı ne olduğunu anlayamıyordu). /RESTARTAPP ile kurulum
        // bitince LiwaPlayer otomatik yeniden açılır.
        public void ApplyUpdate(string setupFile)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = setupFile,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTAPP",
                UseShellExecute = true
            });
        }
    }
}
