using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace LiwaPlayer.Services
{
    public class YouTubeSearchResult
    {
        public string VideoId { get; set; } = "";

        public string Title { get; set; } = "";

        public string Author { get; set; } = "";

        public TimeSpan Duration { get; set; }

        public string ThumbnailUrl { get; set; } = "";

        public bool IsLive { get; set; }

        public string DurationText =>
            IsLive
                ? "🔴 CANLI"
                : Duration.TotalHours >= 1
                    ? Duration.ToString(@"h\:mm\:ss")
                    : Duration.ToString(@"mm\:ss");
    }

    public class YouTubeAccountInfo
    {
        public string ChannelName { get; set; } = "";

        public string AvatarUrl { get; set; } = "";

        // Best-effort: YouTube'un belgelenmemiş hesap menüsü yanıtından
        // ayrıştırılır. Kesin garanti değildir; yanlış sonuç verebilir.
        public bool IsPremium { get; set; }
    }

    public class YouTubeService
    {
        // Optimizasyon ayarı: kapak resimleri kapalıysa URL hiç doldurulmaz,
        // arayüz de indirmez (düşük RAM/bant genişliği)
        public static bool LoadThumbnails = true;

        private static readonly HttpClient Http = CreateHttpClient();

        // Girişsiz istemci her zaman durur; giriş yapılınca ikinci (çerezli)
        // istemci kurulur ve öncelik ona geçer. Biri başarısız olursa diğeri denenir.
        private readonly YoutubeClient _anonClient = new();
        private YoutubeClient? _authClient;

        private YoutubeClient PreferredClient => _authClient ?? _anonClient;

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            return http;
        }

        // Giriş yapılınca istemci oturum çerezleriyle yeniden kurulur:
        // Premium hesapta reklamsız akış + özel/kendi listelere erişim sağlar
        public void SetAuthCookies(IReadOnlyList<Cookie>? cookies)
        {
            _authClient = cookies is { Count: > 0 }
                ? new YoutubeClient(cookies)
                : null;
        }

        // ═══════════ Normal YouTube araması ═══════════

        public async Task<List<YouTubeSearchResult>> SearchAsync(string query, int maxResults = 20)
        {
            var results = new List<YouTubeSearchResult>();

            await foreach (var video in PreferredClient.Search.GetVideosAsync(query))
            {
                results.Add(new YouTubeSearchResult
                {
                    VideoId = video.Id,
                    Title = video.Title,
                    Author = video.Author.ChannelTitle,
                    Duration = video.Duration ?? TimeSpan.Zero,
                    ThumbnailUrl = PickThumbnail(video.Thumbnails
                        .Select(t => (t.Url, t.Resolution.Width))
                        .ToList())
                });

                if (results.Count >= maxResults)
                    break;
            }

            return results;
        }

        // ═══════════ Canlı yayın araması (radyo kanalları) ═══════════

        // InnerTube WEB istemcisiyle "canlı" filtreli arama: yalnızca o an
        // yayında olan kanallar döner (7/24 radyo kanalları dahil)
        public async Task<List<YouTubeSearchResult>> SearchLiveAsync(string query, int maxResults = 20)
        {
            var body = JsonSerializer.Serialize(new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion = "2.20250312.04.00",
                        hl = "tr",
                        gl = "TR"
                    }
                },
                query,
                // "Canlı" filtresi
                @params = "EgJAAQ%3D%3D"
            });

            var response = await Http.PostAsync(
                "https://www.youtube.com/youtubei/v1/search?prettyPrint=false",
                new StringContent(body, Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var results = new List<YouTubeSearchResult>();

            using var doc = JsonDocument.Parse(json);
            WalkVideoRendererResults(doc.RootElement, results, maxResults, isLive: true);

            return results;
        }

        // videoRenderer düğümlerini toplar; hem canlı arama hem abonelik akışı
        // aynı düğüm şeklini kullanır, isLive sadece etiketleme için verilir
        private static void WalkVideoRendererResults(
            JsonElement element, List<YouTubeSearchResult> results, int max, bool isLive)
        {
            if (results.Count >= max)
                return;

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("videoRenderer", out var video) &&
                    video.TryGetProperty("videoId", out var id))
                {
                    var result = new YouTubeSearchResult
                    {
                        VideoId = id.GetString() ?? "",
                        IsLive = isLive
                    };

                    if (video.TryGetProperty("title", out var t) &&
                        t.TryGetProperty("runs", out var runs) && runs.GetArrayLength() > 0)
                        result.Title = runs[0].GetProperty("text").GetString() ?? "";

                    if (video.TryGetProperty("ownerText", out var o) &&
                        o.TryGetProperty("runs", out var oruns) && oruns.GetArrayLength() > 0)
                        result.Author = oruns[0].GetProperty("text").GetString() ?? "";

                    if (video.TryGetProperty("thumbnail", out var th) &&
                        th.TryGetProperty("thumbnails", out var thumbs))
                    {
                        result.ThumbnailUrl = PickThumbnail(thumbs.EnumerateArray()
                            .Select(x => (
                                Url: x.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                                Width: x.TryGetProperty("width", out var w) ? w.GetInt32() : 0))
                            .Where(x => x.Url.Length > 0)
                            .ToList());
                    }

                    if (result.VideoId.Length > 0 && !string.IsNullOrWhiteSpace(result.Title))
                        results.Add(result);
                }

                foreach (var property in element.EnumerateObject())
                    WalkVideoRendererResults(property.Value, results, max, isLive);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                    WalkVideoRendererResults(child, results, max, isLive);
            }
        }

        // ═══════════ YouTube Music araması (InnerTube WEB_REMIX) ═══════════

        public async Task<List<YouTubeSearchResult>> SearchMusicAsync(string query, int maxResults = 20)
        {
            var body = JsonSerializer.Serialize(new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB_REMIX",
                        clientVersion = "1.20250310.01.00",
                        hl = "tr",
                        gl = "TR"
                    }
                },
                query,
                // "Şarkılar" filtresi: sonuçlar sadece müzik parçaları olur
                @params = "EgWKAQIIAWoQEAMQBBAJEAoQBRAREBAQFQ%3D%3D"
            });

            var response = await Http.PostAsync(
                "https://music.youtube.com/youtubei/v1/search?prettyPrint=false",
                new StringContent(body, Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var results = new List<YouTubeSearchResult>();

            using var doc = JsonDocument.Parse(json);
            WalkMusicResults(doc.RootElement, results, maxResults);

            return results;
        }

        private static void WalkMusicResults(JsonElement element, List<YouTubeSearchResult> results, int max)
        {
            if (results.Count >= max)
                return;

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("musicResponsiveListItemRenderer", out var item))
                {
                    var parsed = ParseMusicItem(item);

                    if (parsed != null)
                        results.Add(parsed);
                }

                foreach (var property in element.EnumerateObject())
                    WalkMusicResults(property.Value, results, max);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                    WalkMusicResults(child, results, max);
            }
        }

        private static YouTubeSearchResult? ParseMusicItem(JsonElement item)
        {
            // Video ID'si olmayan öğeler (albüm, sanatçı kartları) atlanır
            if (!item.TryGetProperty("playlistItemData", out var pid) ||
                !pid.TryGetProperty("videoId", out var vid))
                return null;

            var videoId = vid.GetString();

            if (string.IsNullOrEmpty(videoId))
                return null;

            var result = new YouTubeSearchResult { VideoId = videoId };

            if (item.TryGetProperty("flexColumns", out var cols) && cols.GetArrayLength() > 0)
            {
                var titleRuns = cols[0]
                    .GetProperty("musicResponsiveListItemFlexColumnRenderer")
                    .GetProperty("text");

                if (titleRuns.TryGetProperty("runs", out var runs0) && runs0.GetArrayLength() > 0)
                    result.Title = runs0[0].GetProperty("text").GetString() ?? "";

                // İkinci kolon: "Sanatçı • Albüm • 3:54" şeklinde parçalar
                if (cols.GetArrayLength() > 1)
                {
                    var subText = cols[1]
                        .GetProperty("musicResponsiveListItemFlexColumnRenderer")
                        .GetProperty("text");

                    if (subText.TryGetProperty("runs", out var runs1))
                    {
                        var parts = runs1.EnumerateArray()
                            .Select(r => r.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "")
                            .Where(t => t.Trim() != "•" && !string.IsNullOrWhiteSpace(t))
                            .ToList();

                        if (parts.Count > 0)
                            result.Author = parts[0];

                        var durationText = parts.LastOrDefault(p =>
                            Regex.IsMatch(p.Trim(), @"^\d+:\d{2}(:\d{2})?$"));

                        if (durationText != null)
                            result.Duration = ParseDuration(durationText.Trim());
                    }
                }
            }

            if (item.TryGetProperty("thumbnail", out var t) &&
                t.TryGetProperty("musicThumbnailRenderer", out var mtr) &&
                mtr.TryGetProperty("thumbnail", out var th) &&
                th.TryGetProperty("thumbnails", out var thumbs))
            {
                var candidates = thumbs.EnumerateArray()
                    .Select(x => (
                        Url: x.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                        Width: x.TryGetProperty("width", out var w) ? w.GetInt32() : 0))
                    .Where(x => x.Url.Length > 0)
                    .ToList();

                result.ThumbnailUrl = PickThumbnail(candidates);
            }

            return string.IsNullOrWhiteSpace(result.Title) ? null : result;
        }

        private static TimeSpan ParseDuration(string text)
        {
            var parts = text.Split(':').Select(int.Parse).ToArray();

            return parts.Length switch
            {
                3 => new TimeSpan(parts[0], parts[1], parts[2]),
                2 => new TimeSpan(0, parts[0], parts[1]),
                _ => TimeSpan.Zero
            };
        }

        // Liste görünümü için küçük boyutlu kapak yeterli; büyükleri indirip RAM harcamayalım
        private static string PickThumbnail(List<(string Url, int Width)> thumbnails)
        {
            if (!LoadThumbnails || thumbnails.Count == 0)
                return "";

            var small = thumbnails
                .Where(t => t.Width >= 120)
                .OrderBy(t => t.Width)
                .FirstOrDefault();

            return small.Url ?? thumbnails.OrderByDescending(t => t.Width).First().Url;
        }

        // ═══════════ Playlist içe aktarma ═══════════

        // youtube.com veya music.youtube.com liste bağlantısını çözer.
        // Giriş yapılmışsa kullanıcının kendi özel listeleri de çekilebilir.
        public async Task<(string Title, List<YouTubeSearchResult> Videos)> GetPlaylistAsync(
            string url, int maxVideos = 500)
        {
            var playlist = await PreferredClient.Playlists.GetAsync(url);

            var videos = new List<YouTubeSearchResult>();

            await foreach (var video in PreferredClient.Playlists.GetVideosAsync(playlist.Id))
            {
                videos.Add(new YouTubeSearchResult
                {
                    VideoId = video.Id,
                    Title = video.Title,
                    Author = video.Author.ChannelTitle,
                    Duration = video.Duration ?? TimeSpan.Zero,
                    ThumbnailUrl = PickThumbnail(video.Thumbnails
                        .Select(t => (t.Url, t.Resolution.Width))
                        .ToList())
                });

                if (videos.Count >= maxVideos)
                    break;
            }

            return (playlist.Title, videos);
        }

        // Liste/albüm başlığını çözer (albümler de list= bağlantısıyla gelir)
        public async Task<string> GetPlaylistTitleAsync(string url)
        {
            var playlist = await PreferredClient.Playlists.GetAsync(url);

            return playlist.Title;
        }

        // Videoları tek tek akıtır; içe aktarma penceresi canlı ilerleme gösterir
        public async IAsyncEnumerable<YouTubeSearchResult> EnumeratePlaylistVideosAsync(string url)
        {
            await foreach (var video in PreferredClient.Playlists.GetVideosAsync(url))
            {
                yield return new YouTubeSearchResult
                {
                    VideoId = video.Id,
                    Title = video.Title,
                    Author = video.Author.ChannelTitle,
                    Duration = video.Duration ?? TimeSpan.Zero,
                    ThumbnailUrl = PickThumbnail(video.Thumbnails
                        .Select(t => (t.Url, t.Resolution.Width))
                        .ToList())
                };
            }
        }

        // ═══════════ Hesap bilgisi (avatar, Premium — en iyi çaba) ═══════════

        // Google'ın kendi web istemcisinin kimlik doğrulamalı isteklerde
        // kullandığı imza yöntemi: SHA1(zaman SAPISID origin). Belgelenmemiş
        // ama yaygın bilinen bir tekniktir (cookie'nin ötesinde ek doğrulama ister).
        private static string BuildSapisidHash(string sapisid, string origin)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string raw = $"{timestamp} {sapisid} {origin}";

            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
            string hex = Convert.ToHexString(hash).ToLowerInvariant();

            return $"SAPISIDHASH {timestamp}_{hex}";
        }

        private static HttpRequestMessage BuildAuthenticatedRequest(
            string url, string body, IReadOnlyList<Cookie> cookies)
        {
            var sapisid = cookies.FirstOrDefault(c => c.Name == "SAPISID")?.Value
                ?? cookies.FirstOrDefault(c => c.Name == "__Secure-3PAPISID")?.Value
                ?? throw new InvalidOperationException("SAPISID çerezi bulunamadı.");

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
            request.Headers.Add("Authorization", BuildSapisidHash(sapisid, "https://www.youtube.com"));
            request.Headers.Add("X-Origin", "https://www.youtube.com");
            request.Headers.Add("X-Goog-AuthUser", "0");

            return request;
        }

        // Profil fotoğrafı, kanal adı ve Premium durumunu YouTube'un hesap
        // menüsü uç noktasından çeker. Bu uç nokta belgelenmemiştir; YouTube
        // yapıyı değiştirirse tespit bozulabilir — böyle bir durumda null döner,
        // uygulama sessizce varsayılan (giriş simgesi) görünüme düşer.
        public async Task<YouTubeAccountInfo?> FetchAccountInfoAsync(IReadOnlyList<Cookie>? cookies)
        {
            if (cookies == null || cookies.Count == 0)
                return null;

            try
            {
                var body = JsonSerializer.Serialize(new
                {
                    context = new
                    {
                        client = new
                        {
                            clientName = "WEB",
                            clientVersion = "2.20250312.04.00",
                            hl = "tr",
                            gl = "TR"
                        }
                    }
                });

                using var request = BuildAuthenticatedRequest(
                    "https://www.youtube.com/youtubei/v1/account/account_menu?prettyPrint=false",
                    body, cookies);

                using var response = await Http.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);

                var info = new YouTubeAccountInfo();
                WalkAccountMenu(doc.RootElement, info);

                return string.IsNullOrEmpty(info.AvatarUrl) ? null : info;
            }
            catch (Exception ex)
            {
                LogService.Write("Hesap bilgisi alınamadı (en iyi çaba)", ex);
                return null;
            }
        }

        private static void WalkAccountMenu(JsonElement element, YouTubeAccountInfo info)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("accountName", out var name) &&
                    name.TryGetProperty("simpleText", out var nameText))
                    info.ChannelName = nameText.GetString() ?? info.ChannelName;

                if (element.TryGetProperty("accountPhoto", out var photo) &&
                    photo.TryGetProperty("thumbnails", out var thumbs))
                {
                    var best = thumbs.EnumerateArray()
                        .Select(t => (
                            Url: t.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                            Width: t.TryGetProperty("width", out var w) ? w.GetInt32() : 0))
                        .Where(t => t.Url.Length > 0)
                        .OrderByDescending(t => t.Width)
                        .FirstOrDefault();

                    if (!string.IsNullOrEmpty(best.Url))
                        info.AvatarUrl = best.Url;
                }

                foreach (var property in element.EnumerateObject())
                {
                    // "YouTube Premium" tek başına bir metin değeri olarak
                    // (uzun bir tanıtım cümlesinin parçası değil) Premium
                    // üyelerin hesap menüsünde rozet/bölüm başlığı olarak geçer
                    if (property.Value.ValueKind == JsonValueKind.String &&
                        string.Equals(property.Value.GetString()?.Trim(), "YouTube Premium",
                            StringComparison.OrdinalIgnoreCase))
                        info.IsPremium = true;

                    WalkAccountMenu(property.Value, info);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                    WalkAccountMenu(child, info);
            }
        }

        // ═══════════ Abonelik akışı (öneriler için — en iyi çaba) ═══════════

        // Abone olunan kanalların son videolarını YouTube'un "Abonelikler"
        // besleme sayfasından çeker. Belgelenmemiş bir uç nokta kullanır;
        // başarısız olursa boş liste döner ve öneriler kitaplık tabanlı kalır.
        public async Task<List<YouTubeSearchResult>> FetchSubscriptionVideosAsync(
            IReadOnlyList<Cookie>? cookies, int maxResults = 20)
        {
            if (cookies == null || cookies.Count == 0)
                return new List<YouTubeSearchResult>();

            try
            {
                var body = JsonSerializer.Serialize(new
                {
                    context = new
                    {
                        client = new
                        {
                            clientName = "WEB",
                            clientVersion = "2.20250312.04.00",
                            hl = "tr",
                            gl = "TR"
                        }
                    },
                    browseId = "FEsubscriptions"
                });

                using var request = BuildAuthenticatedRequest(
                    "https://www.youtube.com/youtubei/v1/browse?prettyPrint=false",
                    body, cookies);

                using var response = await Http.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return new List<YouTubeSearchResult>();

                var json = await response.Content.ReadAsStringAsync();

                var results = new List<YouTubeSearchResult>();

                using var doc = JsonDocument.Parse(json);
                WalkVideoRendererResults(doc.RootElement, results, maxResults, isLive: false);

                return results;
            }
            catch (Exception ex)
            {
                LogService.Write("Abonelik akışı alınamadı (en iyi çaba)", ex);
                return new List<YouTubeSearchResult>();
            }
        }

        // ═══════════ Akış çözümleme ═══════════

        // Video indirilmez; sadece ses akışının URL'si çözülür ve LibVLC'ye verilir.
        // Önce tercih edilen istemciyle (girişliyse girişli) denenir; YouTube'un
        // istemciye/hesaba özel red durumlarında diğer istemciyle tekrar denenir.
        public async Task<string> GetAudioStreamUrlAsync(string videoId)
        {
            try
            {
                return await ResolveStreamUrlAsync(PreferredClient, videoId);
            }
            catch (Exception firstError)
            {
                LogService.Write(
                    $"Akış çözümleme ({(_authClient != null ? "girişli" : "girişsiz")}, {videoId})",
                    firstError);

                // Yedek istemci yoksa hatayı olduğu gibi bildir
                if (_authClient == null)
                    throw;

                LogService.Write($"Akış çözümleme girişsiz istemciyle tekrar deneniyor ({videoId})");

                try
                {
                    return await ResolveStreamUrlAsync(_anonClient, videoId);
                }
                catch (Exception secondError)
                {
                    LogService.Write($"Akış çözümleme (girişsiz, {videoId})", secondError);
                    throw;
                }
            }
        }

        private static async Task<string> ResolveStreamUrlAsync(YoutubeClient client, string videoId)
        {
            var manifest = await client.Videos.Streams.GetManifestAsync(videoId);

            var stream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            return stream.Url;
        }

        // Canlı yayın (radyo): HLS bağlantısı döner; LibVLC doğrudan çalar.
        // ":no-video" seçeneğiyle sadece sesi çözülür, görselleştiricide video da açılabilir.
        public async Task<string> GetLiveStreamUrlAsync(string videoId)
        {
            try
            {
                return await PreferredClient.Videos.Streams.GetHttpLiveStreamUrlAsync(videoId);
            }
            catch (Exception ex) when (_authClient != null)
            {
                LogService.Write($"Canlı akış (girişli, {videoId})", ex);

                return await _anonClient.Videos.Streams.GetHttpLiveStreamUrlAsync(videoId);
            }
        }

        // Klip modu: YouTube muxed akış vermediği için video-only (≤480p, CPU dostu)
        // ve ses akışı ayrı çözülür; LibVLC input-slave ile birleştirir
        public async Task<(string VideoUrl, string AudioUrl)> GetVideoStreamsAsync(string videoId)
        {
            var manifest = await PreferredClient.Videos.Streams.GetManifestAsync(videoId);

            var video = manifest.GetVideoOnlyStreams()
                    .Where(s => s.VideoQuality.MaxHeight <= 360)
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault()
                ?? manifest.GetVideoOnlyStreams()
                    .OrderBy(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault()
                ?? throw new InvalidOperationException("Bu şarkının video akışı yok.");

            var audio = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            return (video.Url, audio.Url);
        }
    }
}
