using LibVLCSharp.Shared;
using System;
using System.Threading.Tasks;

namespace LiwaPlayer.Services
{
    public class PlayerService : IDisposable
    {
        private readonly LibVLC _libVLC;
        private readonly MediaPlayer _mediaPlayer;
        private Media? _currentMedia;

        public event EventHandler? PlaybackEnded;
        public event EventHandler? EncounteredError;

        public PlayerService()
        {
            Core.Initialize();

            // Video kod çözümü medya bazında kontrol edilir: normal çalmada ":no-video"
            // ile kapalıdır (CPU harcamaz), görselleştiricide klip için açılır
            _libVLC = new LibVLC("--quiet");
            _mediaPlayer = new MediaPlayer(_libVLC);

            _mediaPlayer.EndReached += MediaPlayer_EndReached;
            _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
        }

        public bool IsPlaying => _mediaPlayer.IsPlaying;

        // Görselleştirici penceresi klip gösterirken VideoView'a bağlanır
        public MediaPlayer MediaPlayer => _mediaPlayer;

        // YouTube akışları için ağ önbelleği; optimizasyon ayarlarından değişir
        public int NetworkCachingMs { get; set; } = 3000;

        public bool HasMedia => _mediaPlayer.Media != null;

        public int Volume
        {
            get => _mediaPlayer.Volume;
            set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
        }

        public long Duration => Math.Max(0, _mediaPlayer.Length);

        public long CurrentTime => Math.Max(0, _mediaPlayer.Time);

        public float Position
        {
            get => _mediaPlayer.Position;
            set => _mediaPlayer.Position = Math.Clamp(value, 0f, 1f);
        }

        public void Play(string location, bool withVideo = false)
        {
            if (string.IsNullOrWhiteSpace(location))
                return;

            Stop();

            bool isRemote = location.StartsWith("http", StringComparison.OrdinalIgnoreCase);

            var options = new System.Collections.Generic.List<string>();

            if (isRemote)
                options.Add($":network-caching={NetworkCachingMs}");

            // Video parçası olsa bile (canlı yayınlar gibi) kod çözme; CPU tasarrufu
            if (!withVideo)
                options.Add(":no-video");

            _currentMedia = new Media(_libVLC, new Uri(location), options.ToArray());

            _mediaPlayer.Play(_currentMedia);
        }

        // Klip modu: YouTube artık tek dosyada video+ses vermediği için video-only
        // akış, ses akışı input-slave olarak eklenerek birlikte oynatılır
        public void PlayWithSlaveAudio(string videoUrl, string audioUrl)
        {
            if (string.IsNullOrWhiteSpace(videoUrl) || string.IsNullOrWhiteSpace(audioUrl))
                return;

            Stop();

            _currentMedia = new Media(_libVLC, new Uri(videoUrl),
                $":input-slave={audioUrl}",
                $":network-caching={NetworkCachingMs}");

            _mediaPlayer.Play(_currentMedia);
        }

        public void Resume()
        {
            if (!_mediaPlayer.IsPlaying && _mediaPlayer.Media != null)
                _mediaPlayer.Play();
        }

        public void Pause()
        {
            if (_mediaPlayer.CanPause)
                _mediaPlayer.Pause();
        }

        public void Stop()
        {
            _mediaPlayer.Stop();

            _currentMedia?.Dispose();
            _currentMedia = null;
        }

        public void Seek(long milliseconds)
        {
            if (Duration <= 0)
                return;

            _mediaPlayer.Time = Math.Clamp(milliseconds, 0, Duration);
        }

        public void Forward(int seconds = 10) => Seek(CurrentTime + seconds * 1000L);

        public void Backward(int seconds = 10) => Seek(CurrentTime - seconds * 1000L);

        // Yerel dosyanın süre + etiket bilgisini okur (playlist'e eklerken kullanılır)
        public async Task<(TimeSpan Duration, string? Title, string? Artist)> GetLocalMetadataAsync(string file)
        {
            using var media = new Media(_libVLC, new Uri(file));

            await media.Parse(MediaParseOptions.ParseLocal);

            var duration = TimeSpan.FromMilliseconds(Math.Max(0, media.Duration));

            return (duration, media.Meta(MetadataType.Title), media.Meta(MetadataType.Artist));
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
        {
            EncounteredError?.Invoke(this, EventArgs.Empty);
        }

        public string FormatTime(long milliseconds)
        {
            var ts = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));

            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"mm\:ss");
        }

        public void Dispose()
        {
            _mediaPlayer.EndReached -= MediaPlayer_EndReached;
            _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;

            Stop();

            _mediaPlayer.Dispose();
            _libVLC.Dispose();
        }
    }
}
