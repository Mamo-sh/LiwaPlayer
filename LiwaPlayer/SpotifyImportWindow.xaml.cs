using LiwaPlayer.Models;
using LiwaPlayer.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LiwaPlayer
{
    public partial class SpotifyImportWindow : Window
    {
        private readonly PlaylistService _playlist;
        private readonly YouTubeService _youtube;
        private readonly SpotifyService _spotify = new();

        private readonly ObservableCollection<string> _log = new();
        private readonly CancellationTokenSource _cancel = new();

        private bool _running;

        // İçe aktarma başarılıysa oluşturulan liste
        public Playlist? ImportedPlaylist { get; private set; }

        public SpotifyImportWindow(PlaylistService playlist, YouTubeService youtube)
        {
            InitializeComponent();

            UiScaleHelper.Apply(this);

            _playlist = playlist;
            _youtube = youtube;

            lstLog.ItemsSource = _log;

            txtUrl.Focus();
        }

        private void txtUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                btnImport_Click(sender, e);
        }

        private async void btnImport_Click(object sender, RoutedEventArgs e)
        {
            if (_running)
                return;

            var url = txtUrl.Text.Trim();

            if (!url.Contains("spotify", StringComparison.OrdinalIgnoreCase))
            {
                txtProgress.Text = "Geçerli bir Spotify bağlantısı yapıştırın.";
                return;
            }

            _running = true;
            btnImport.IsEnabled = false;
            txtUrl.IsEnabled = false;
            _log.Clear();

            try
            {
                txtProgress.Text = "Spotify listesi okunuyor...";

                var (name, tracks) = await _spotify.GetPlaylistAsync(url);

                if (tracks.Count == 0)
                {
                    txtProgress.Text = "Listede parça bulunamadı.";
                    return;
                }

                // İsim çakışırsa sonuna numara ekle
                string finalName = name;
                int suffix = 2;

                while (_playlist.NameExists(finalName))
                    finalName = $"{name} ({suffix++})";

                var target = _playlist.CreatePlaylist(finalName);
                ImportedPlaylist = target;

                progress.Maximum = tracks.Count;

                int matched = 0;
                var unmatched = new System.Collections.Generic.List<SpotifyTrack>();

                for (int i = 0; i < tracks.Count; i++)
                {
                    if (_cancel.IsCancellationRequested)
                        break;

                    var track = tracks[i];

                    txtProgress.Text = $"{i + 1}/{tracks.Count}  —  {track.Artist} - {track.Title}";

                    var match = await FindYouTubeMatchAsync(track);

                    if (match != null)
                    {
                        _playlist.Add(new Song
                        {
                            FileName = match.VideoId,
                            Title = match.Title,
                            Artist = match.Author,
                            Duration = match.Duration,
                            Source = SongSource.YouTube
                        }, target, out _);

                        matched++;
                        _log.Add($"✓  {track.Artist} - {track.Title}");
                    }
                    else
                    {
                        unmatched.Add(track);
                        _log.Add($"✗  {track.Artist} - {track.Title}  (bulunamadı)");
                    }

                    progress.Value = i + 1;
                    lstLog.ScrollIntoView(_log[^1]);

                    // YouTube'a nazik davran
                    await Task.Delay(150);
                }

                _playlist.SetActive(target);

                if (unmatched.Count > 0)
                {
                    _log.Add("");
                    _log.Add($"YouTube'da bulunamayan {unmatched.Count} şarkıyı elinizdeki");
                    _log.Add("MP3 dosyalarından dosya ekleme butonuyla ekleyebilirsiniz.");
                }

                txtProgress.Text = _cancel.IsCancellationRequested
                    ? $"İptal edildi. {matched} şarkı eklendi."
                    : $"Bitti: \"{finalName}\" listesine {matched}/{tracks.Count} şarkı eklendi.";
            }
            catch (Exception ex)
            {
                LogService.Write($"Spotify içe aktarma ({url})", ex);
                txtProgress.Text = "Hata: " + ex.Message;
            }
            finally
            {
                _running = false;
                btnImport.IsEnabled = true;
                txtUrl.IsEnabled = true;
            }
        }

        // Önce YouTube Music'te ara (temiz şarkı sonuçları), yoksa normal arama.
        // Spotify süresi biliniyorsa en yakın süreli sonuç seçilir.
        private async Task<YouTubeSearchResult?> FindYouTubeMatchAsync(SpotifyTrack track)
        {
            var query = $"{track.Artist} {track.Title}".Trim();

            var results = await SearchSafeAsync(() => _youtube.SearchMusicAsync(query, 6));

            if (results.Count == 0)
                results = await SearchSafeAsync(() => _youtube.SearchAsync(query, 6));

            if (results.Count == 0)
                return null;

            if (track.Duration.TotalSeconds < 10)
                return results[0];

            // 25 saniyeden fazla sapan sonuçlar büyük ihtimalle yanlış eşleşmedir
            var closest = results
                .Where(r => r.Duration.TotalSeconds > 0)
                .OrderBy(r => Math.Abs((r.Duration - track.Duration).TotalSeconds))
                .FirstOrDefault();

            if (closest != null &&
                Math.Abs((closest.Duration - track.Duration).TotalSeconds) <= 25)
                return closest;

            return results[0];
        }

        private static async Task<System.Collections.Generic.List<YouTubeSearchResult>> SearchSafeAsync(
            Func<Task<System.Collections.Generic.List<YouTubeSearchResult>>> search)
        {
            try
            {
                return await search();
            }
            catch
            {
                return new System.Collections.Generic.List<YouTubeSearchResult>();
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            _cancel.Cancel();
        }
    }
}
