using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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

        public string DurationText =>
            Duration.TotalHours >= 1
                ? Duration.ToString(@"h\:mm\:ss")
                : Duration.ToString(@"mm\:ss");
    }

    public class YouTubeService
    {
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
            if (thumbnails.Count == 0)
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
                    Duration = video.Duration ?? TimeSpan.Zero
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
                    Duration = video.Duration ?? TimeSpan.Zero
                };
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
    }
}
