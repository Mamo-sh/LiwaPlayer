namespace LiwaPlayer.Models
{
    public class Settings
    {
        public int Volume { get; set; } = 80;

        public bool Shuffle { get; set; } = false;

        public bool Repeat { get; set; } = true;

        public string LastSong { get; set; } = "";

        public string ActivePlaylistId { get; set; } = "";

        // Arayüz ölçeği: 1.0 = normal, dokunmatik POS ekranları için büyütülebilir
        public double UiScale { get; set; } = 1.0;

        public double WindowWidth { get; set; } = 1000;

        public double WindowHeight { get; set; } = 620;

        public double WindowLeft { get; set; } = -1;

        public double WindowTop { get; set; } = -1;
    }
}
