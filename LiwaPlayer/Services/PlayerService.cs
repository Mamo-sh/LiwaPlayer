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

            // Ses çalar: video kod çözümü tamamen kapalı, POS makinelerinde CPU harcamaz
            _libVLC = new LibVLC("--no-video", "--quiet");
            _mediaPlayer = new MediaPlayer(_libVLC);

            _mediaPlayer.EndReached += MediaPlayer_EndReached;
            _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
        }

        public bool IsPlaying => _mediaPlayer.IsPlaying;

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

        public void Play(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return;

            Stop();

            bool isRemote = location.StartsWith("http", StringComparison.OrdinalIgnoreCase);

            _currentMedia = isRemote
                ? new Media(_libVLC, new Uri(location), $":network-caching={NetworkCachingMs}")
                : new Media(_libVLC, new Uri(location));

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
