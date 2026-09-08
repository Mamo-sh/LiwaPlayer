using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace LiwaPlayer.Models
{
    public enum SongSource
    {
        Local = 0,
        YouTube = 1
    }

    public enum SongCategory
    {
        Music = 0,
        Birthday = 1
    }

    public class Song : INotifyPropertyChanged
    {
        private bool _isCurrent;
        private SongCategory _category = SongCategory.Music;

        public Guid Id { get; set; } = Guid.NewGuid();

        // Yerel dosya yolu veya YouTube video ID'si
        public string FileName { get; set; } = "";

        public string Title { get; set; } = "";

        public string Artist { get; set; } = "";

        public string Album { get; set; } = "";

        public string CoverImage { get; set; } = "";

        public SongSource Source { get; set; } = SongSource.Local;

        public SongCategory Category
        {
            get => _category;
            set
            {
                if (_category == value)
                    return;

                _category = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Category)));
            }
        }

        public TimeSpan Duration { get; set; }

        // Canlı yayın (radyo kanalı): süresi yoktur, bitmez, kopunca yeniden bağlanılır
        public bool IsLive { get; set; }

        public bool Favorite { get; set; }

        public DateTime AddedDate { get; set; } = DateTime.Now;

        [JsonIgnore]
        public string DurationText =>
            IsLive
                ? "CANLI"
                : Duration.TotalHours >= 1
                    ? Duration.ToString(@"h\:mm\:ss")
                    : Duration.ToString(@"mm\:ss");

        [JsonIgnore]
        public string SourceText => Source == SongSource.YouTube ? "YT" : "MP3";

        [JsonIgnore]
        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent == value)
                    return;

                _isCurrent = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => Title;
    }
}
