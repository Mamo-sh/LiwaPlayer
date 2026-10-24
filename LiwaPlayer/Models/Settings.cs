namespace LiwaPlayer.Models
{
    public enum ThemeMode
    {
        Auto = 0,
        Light = 1,
        Dark = 2
    }

    // Tekrar modu: kapalı / liste bitince başa dön / aynı şarkıyı sürekli çal
    public enum RepeatMode
    {
        Off = 0,
        All = 1,
        One = 2
    }

    public class Settings
    {
        // Otomatik: saate göre (varsayılan 08:00-20:00 açık, gerisi koyu)
        public ThemeMode Theme { get; set; } = ThemeMode.Auto;

        // Otomatik temada açık temanın başladığı ve koyu temanın başladığı an
        // (gece yarısından itibaren dakika). Varsayılan 08:00 ve 20:00.
        public int AutoLightStartMinutes { get; set; } = 8 * 60;

        public int AutoDarkStartMinutes { get; set; } = 20 * 60;

        public int Volume { get; set; } = 80;

        public bool Shuffle { get; set; } = false;

        // Eski ayar dosyalarıyla uyumluluk için duruyor (RepeatMode yoksa buradan okunur)
        public bool Repeat { get; set; } = true;

        // -1: henüz yok (eski ayar dosyası), Repeat alanından türetilir
        public int RepeatModeValue { get; set; } = -1;

        // ═══ İnternet bağlantısı denetimi ═══

        // Açıkken periyodik olarak gerçek internet erişimi sınanır; kopunca
        // indirilen şarkıları dinlemek isteyip istemediğin sorulur
        public bool ConnectionCheckEnabled { get; set; } = true;

        public int ConnectionCheckSeconds { get; set; } = 60;

        public string LastSong { get; set; } = "";

        public string ActivePlaylistId { get; set; } = "";

        // Arayüz ölçeği: 1.0 = normal, dokunmatik POS ekranları için büyütülebilir
        public double UiScale { get; set; } = 1.0;

        // ═══ Optimizasyon ayarları ═══

        // Kapak resimlerini indir/göster (kapatınca RAM ve bant genişliği azalır)
        public bool ShowThumbnails { get; set; } = true;

        // YouTube akışı ağ önbelleği (ms): şarkı bu kadar tampon dolunca başlar.
        // Düşük = şarkı hızlı açılır; takılma/kesilme olan ağlarda yükseltilir
        public int NetworkCachingMs { get; set; } = 1500;

        // Arama sonucu sayısı (azaltmak aramayı hızlandırır)
        public int SearchResultCount { get; set; } = 20;

        // Açılışta GitHub'dan güncelleme denetle
        public bool CheckUpdatesOnStartup { get; set; } = true;

        // Zayıf ekran kartlarında görüntü sorunlarına karşı yazılım tabanlı çizim
        public bool SoftwareRendering { get; set; } = false;

        // Liste bitince benzer şarkı bulup çalmaya devam et
        public bool AutoContinue { get; set; } = false;

        // Görselleştiriciyi tamamen devre dışı bırak (kapak tıklaması pencere açmaz)
        public bool DisableVisualizer { get; set; } = false;

        public double WindowWidth { get; set; } = 1000;

        public double WindowHeight { get; set; } = 620;

        public double WindowLeft { get; set; } = -1;

        public double WindowTop { get; set; } = -1;

        // Güncelleme uygulanıp yeniden başlatılınca gösterilecek "Yenilikler"
        // penceresi için geçici olarak burada tutulur; gösterilince temizlenir
        public string PendingWhatsNewVersion { get; set; } = "";

        public string PendingWhatsNewNotes { get; set; } = "";
    }
}
