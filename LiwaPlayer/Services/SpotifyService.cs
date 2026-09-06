using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LiwaPlayer.Services
{
    public class SpotifyTrack
    {
        public string Title { get; set; } = "";

        public string Artist { get; set; } = "";

        public TimeSpan Duration { get; set; }
    }

    // Spotify'ın herkese açık embed sayfasından liste veya albüm okur; API
    // anahtarı gerekmez. Yalnızca herkese açık içerik desteklenir ve embed
    // en fazla ~100 parça verir.
    public class SpotifyService
    {
        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            return http;
        }

        public async Task<(string Name, List<SpotifyTrack> Tracks)> GetPlaylistAsync(string url)
        {
            var (type, id) = ExtractReference(url)
                ?? throw new ArgumentException("Geçerli bir Spotify liste veya albüm bağlantısı değil.");

            var html = await Http.GetStringAsync($"https://open.spotify.com/embed/{type}/{id}");

            var match = Regex.Match(html,
                "<script id=\"__NEXT_DATA__\" type=\"application/json\">(.*?)</script>",
                RegexOptions.Singleline);

            if (!match.Success)
                throw new InvalidOperationException("Spotify liste verisi okunamadı.");

            using var doc = JsonDocument.Parse(match.Groups[1].Value);

            string? name = null;
            JsonElement? trackList = null;

            Walk(doc.RootElement, 0, ref name, ref trackList);

            if (trackList == null)
                throw new InvalidOperationException(
                    "Listede parça bulunamadı. Liste gizliyse önce herkese açık yapın.");

            var tracks = new List<SpotifyTrack>();

            foreach (var item in trackList.Value.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var artist = item.TryGetProperty("subtitle", out var s) ? s.GetString() ?? "" : "";

                long durationMs = 0;
                if (item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number)
                    durationMs = d.GetInt64();

                tracks.Add(new SpotifyTrack
                {
                    Title = title,
                    Artist = artist,
                    Duration = TimeSpan.FromMilliseconds(durationMs)
                });
            }

            return (string.IsNullOrWhiteSpace(name) ? "Spotify Listesi" : name!, tracks);
        }

        private static void Walk(JsonElement element, int depth, ref string? name, ref JsonElement? trackList)
        {
            if (trackList != null || depth > 25)
                return;

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("trackList", out var tl) &&
                    tl.ValueKind == JsonValueKind.Array)
                {
                    trackList = tl;

                    if (element.TryGetProperty("name", out var n))
                        name = n.GetString();
                    else if (element.TryGetProperty("title", out var t))
                        name = t.GetString();

                    return;
                }

                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, depth + 1, ref name, ref trackList);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                    Walk(child, depth + 1, ref name, ref trackList);
            }
        }

        // open.spotify.com/playlist/ID, open.spotify.com/album/ID,
        // spotify:playlist:ID / spotify:album:ID ve paylaşım bağlantılarındaki
        // (?si=...) biçimlerin hepsini kabul eder
        private static (string Type, string Id)? ExtractReference(string url)
        {
            var m = Regex.Match(url, @"(playlist|album)[/:]([A-Za-z0-9]{10,})",
                RegexOptions.IgnoreCase);

            return m.Success
                ? (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value)
                : null;
        }
    }
}
