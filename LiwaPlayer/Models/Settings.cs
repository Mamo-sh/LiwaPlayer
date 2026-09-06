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

        // ═══ Optimizasyon ayarları ═══

        // Kapak resimlerini indir/göster (kapatınca RAM ve bant genişliği azalır)
        public bool ShowThumbnails { get; set; } = true;

        // YouTube akışı ağ önbelleği (ms); takılma olan ağlarda yükseltilir
        public int NetworkCachingMs { get; set; } = 3000;

        // Arama sonucu sayısı (azaltmak aramayı hızlandırır)
        public int SearchResultCount { get; set; } = 20;

        // Açılışta GitHub'dan güncelleme denetle
        public bool CheckUpdatesOnStartup { get; set; } = true;

        // Zayıf ekran kartlarında görüntü sorunlarına karşı yazılım tabanlı çizim
        public bool SoftwareRendering { get; set; } = false;

        // Liste bitince benzer şarkı bulup çalmaya devam et
        public bool AutoContinue { get; set; } = false;

        public double WindowWidth { get; set; } = 1000;

        public double WindowHeight { get; set; } = 620;

        public double WindowLeft { get; set; } = -1;

        public double WindowTop { get; set; } = -1;
    }
}
