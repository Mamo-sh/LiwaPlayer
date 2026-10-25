using LiwaPlayer.Models;
using LiwaPlayer.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace LiwaPlayer
{
    public partial class MainWindow : Window
    {
        private readonly PlayerService _player = new();
        private readonly PlaylistService _playlist = new();
        private readonly SettingsService _settings = new();
        private readonly YouTubeService _youtube = new();
        private readonly YouTubeAuthService _auth = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly Random _random = new();

        private bool _draggingSlider;

        // Çalma sırası bu listeyi takip eder (görüntülenen liste değişse bile
        // sonraki/önceki, şarkının başlatıldığı listede döner)
        private Playlist? _playbackPlaylist;

        // Ana listede o an gösterilen koleksiyon (liste değişince abonelik tazelenir)
        private ObservableCollection<Song>? _boundSongs;

        // Aynı anda iki şarkı yüklenmesin diye: yeni bir çalma isteği
        // geldiğinde eski YouTube URL çözümlemesi geçersiz sayılır. Şarkı silinip
        // çalma durdurulduğunda da artırılır ki çözümlenmekte olan şarkı sonradan başlamasın.
        private long _playToken;

        // Tekrar modu: kapalı / liste / tek şarkı
        private RepeatMode _repeatMode = RepeatMode.All;

        // Şarkı başlar başlamaz sıradaki şarkı seçilir ve YouTube adresi arka
        // planda çözülür; "Sonraki"ye basınca/şarkı bitince beklemeden başlar
        private Song? _plannedNext;
        private bool _plannedForShuffle;

        // Tek şarkı tekrarında, hemen biten (bozuk akış) şarkıyı sonsuz döngüye sokmamak için
        private DateTime _playStartedAtUtc = DateTime.MinValue;

        // Radyo sekmesinde arama kutusuna kendiliğinden yazılan sorgu
        private const string AutoRadioQuery = "canlı radyo";

        // Sistem tepsisi: müzik çalarken pencere kapatılırsa uygulama
        // kapanmaz, tepsiye küçülür ve çalmaya devam eder
        private WinForms.NotifyIcon? _tray;
        private bool _reallyExit;
        private bool _balloonShown;

        private readonly UpdateService _updater = new();
        private bool _updateInProgress;

        // Açık olan görselleştirici penceresi (kapaktan açılır)
        private VisualizerWindow? _visualizer;

        // MP3 indirme kuyruğu (yt-dlp ile, sırayla işlenir)
        private readonly DownloadService _downloader = new();
        private readonly System.Collections.Generic.Queue<Song> _downloadQueue = new();
        private bool _downloading;

        // Bellek bekçisi: 100 MB tavanı aşılırsa bellek anında kırpılır
        private const long MemoryLimitBytes = 100 * 1024 * 1024;
        private readonly DispatcherTimer _memoryGuard = new() { Interval = TimeSpan.FromSeconds(15) };

        // Otomatik tema modunda gündüz/gece geçişini yakalamak için periyodik denetim
        // (saatler dakika hassasiyetinde ayarlanabildiği için dakikada bir bakılır;
        // görünüm değişmiyorsa hiçbir şey yeniden çizilmez)
        private readonly DispatcherTimer _themeTimer = new() { Interval = TimeSpan.FromMinutes(1) };

        // Pencere başlığında ve üst çubukta gösterilen sabit sürüm etiketi
        private readonly string _baseTitle = "LiwaPlayer v" + UpdateService.CurrentVersion.ToString(3);

        // Hesap girişi sonrası en iyi çaba ile çekilir (avatar, Premium durumu)
        private bool _isPremiumAccount;

        // Görev çubuğu simgesinin üzerine gelince çıkan küçük resim araç
        // çubuğu (tepsi simgesinden farklı — Windows'un resmi ITaskbarList3 API'si)
        private TaskbarThumbButtons? _taskbarButtons;

        // Gerçek internet erişimini izler; kopunca/gelince kullanıcıya sorar
        private readonly NetworkMonitorService _network = new();

        private const string DownloadedPlaylistName = "İndirilen Şarkılar";

        // Önceki şarkı butonunda "baştan başlat" eşiği (Spotify tarzı akıllı önceki)
        private const long PreviousRestartThresholdMs = 3000;

        public MainWindow()
        {
            InitializeComponent();

            Title = _baseTitle;
            txtAppVersion.Text = _baseTitle;

            InitTray();
            InitTaskbarButtons();

            _network.ConnectionLost += OnConnectionLost;
            _network.ConnectionRestored += OnConnectionRestored;

            Closing += MainWindow_Closing;

            BindActivePlaylist();

            ApplySettings();

            _themeTimer.Tick += (_, _) =>
            {
                if (_settings.Current.Theme == ThemeMode.Auto)
                    ThemeService.RefreshAuto();
            };
            _themeTimer.Start();

            // Kayıtlı oturum varsa kimlik doğrulamalı istemciyle başla
            if (_auth.IsLoggedIn)
            {
                _youtube.SetAuthCookies(_auth.Cookies);
                UpdateAccountButton();

                // Avatar/Premium bilgisi ve Beğenilen/Daha Sonra İzle listeleri
                // arka planda çekilir; açılışı bekletmez
                _ = OnLoggedInAsync();
            }

            _player.PlaybackEnded += (_, _) =>
                Dispatcher.BeginInvoke(new Action(OnPlaybackEnded));

            _player.EncounteredError += (_, _) =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Önbellekteki adres erken geçersiz olmuş olabilir; bir dahaki
                    // denemede taze adres çözülsün
                    if (_playlist.CurrentSong is { Source: SongSource.YouTube } current)
                        _youtube.InvalidateAudioUrl(current.FileName);

                    NotifyError("Çalma hatası oluştu.");
                }));

            sliderPosition.PreviewMouseDown += (_, _) => _draggingSlider = true;

            sliderPosition.PreviewMouseUp += (_, _) =>
            {
                _draggingSlider = false;
                _player.Position = (float)(sliderPosition.Value / 100.0);
            };

            _timer.Tick += Timer_Tick;
            _timer.Start();

            _memoryGuard.Tick += (_, _) => EnforceMemoryLimit();
            _memoryGuard.Start();

            ApplyOptimizationSettings();

            Loaded += async (_, _) =>
            {
                // Güncelleme sonrası ilk açılışta bir kez "Yenilikler" göster
                ShowWhatsNewIfPending();

                // Ana ekranda YouTube ana sayfası gibi öneriler göster
                await LoadRecommendationsAsync();

                // Açılış + öneri yüklemesinin şişirdiği belleği hemen iade et
                TrimMemory();

                // Açılıştan kısa süre sonra arka planda güncelleme denetle
                if (_settings.Current.CheckUpdatesOnStartup)
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));
                    await CheckForUpdatesAsync(silent: true);
                }
            };
        }

        // ═══════════════ Optimizasyon ayarları ═══════════════

        private void ApplyOptimizationSettings()
        {
            var s = _settings.Current;

            _player.NetworkCachingMs = Math.Clamp(s.NetworkCachingMs, 500, 15000);
            YouTubeService.LoadThumbnails = s.ShowThumbnails;

            ThemeService.SetSchedule(s.AutoLightStartMinutes, s.AutoDarkStartMinutes);
            ThemeService.Apply(s.Theme);

            _network.Configure(s.ConnectionCheckEnabled, s.ConnectionCheckSeconds);
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow(_settings) { Owner = this };

            if (window.ShowDialog() == true)
            {
                ApplyOptimizationSettings();
                NotifySuccess("Ayarlar kaydedildi.");
            }
        }

        // ═══════════════ Öneriler ═══════════════

        // Girişliyse önce abone olunan kanalların videoları (ağırlıklı gösterim),
        // sonra kitaplıktaki sanatçılardan rastgele seçilenler eklenir. Abonelik
        // akışı alınamazsa (giriş yok veya en-iyi-çaba uç noktası başarısız)
        // sessizce sadece kitaplık tabanlı önerilere düşülür.
        private async Task LoadRecommendationsAsync()
        {
            try
            {
                var subsPool = new System.Collections.Generic.List<YouTubeSearchResult>();

                if (_auth.IsLoggedIn)
                {
                    try
                    {
                        subsPool = await _youtube.FetchSubscriptionVideosAsync(_auth.Cookies, 16);
                    }
                    catch
                    {
                        // Abonelik akışı başarısız: kitaplık tabanlı önerilerle devam
                    }
                }

                var allSongs = _playlist.Playlists.SelectMany(p => p.Songs).ToList();

                var seeds = allSongs
                    .Select(s => s.Artist)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(_ => _random.Next())
                    .Take(subsPool.Count > 0 ? 2 : 3)
                    .ToList();

                var libraryPool = new System.Collections.Generic.List<YouTubeSearchResult>();

                if (seeds.Count == 0 && subsPool.Count == 0)
                {
                    libraryPool.AddRange(await _youtube.SearchMusicAsync("en çok dinlenen şarkılar", 20));
                }
                else
                {
                    foreach (var seed in seeds)
                    {
                        try
                        {
                            libraryPool.AddRange(await _youtube.SearchMusicAsync(seed, subsPool.Count > 0 ? 5 : 8));
                        }
                        catch
                        {
                            // Tek sanatçının araması patlarsa diğerleriyle devam
                        }
                    }
                }

                var known = allSongs
                    .Where(s => s.Source == SongSource.YouTube)
                    .Select(s => s.FileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Abonelik videoları YouTube'un kendi alaka sırasıyla önce gelir
                // (ağırlıklı gösterim); kitaplık tabanlı dolgu karıştırılıp sona eklenir
                var recommendations = subsPool
                    .Concat(libraryPool.OrderBy(_ => _random.Next()))
                    .GroupBy(r => r.VideoId)
                    .Select(g => g.First())
                    .Where(r => !known.Contains(r.VideoId))
                    .Take(20)
                    .ToList();

                if (recommendations.Count == 0)
                    return;

                // Kullanıcı bu arada arama yaptıysa önerilerle üzerine yazma
                if (!string.IsNullOrWhiteSpace(txtSearch.Text))
                    return;

                lstResults.ItemsSource = recommendations;
                txtRecoTitle.Text = subsPool.Count > 0
                    ? "✨ Abone olduğun kanallardan ve sana özel öneriler"
                    : "✨ Sana özel öneriler";
                txtRecoTitle.Visibility = Visibility.Visible;
                txtSearchHint.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LogService.Write("Öneriler yüklenemedi", ex);
            }
        }

        // ═══════════════ Güncelleme ═══════════════

        private async System.Threading.Tasks.Task CheckForUpdatesAsync(bool silent)
        {
            if (_updateInProgress)
                return;

            if (!UpdateService.IsConfigured)
            {
                if (!silent)
                    NotifyWarn("Güncelleme kaynağı henüz yapılandırılmamış.");
                return;
            }

            try
            {
                var update = await _updater.CheckAsync();

                if (update == null)
                {
                    if (!silent)
                        NotifySuccess($"Uygulama güncel (v{UpdateService.CurrentVersion.ToString(3)}).");
                    return;
                }

                if (silent)
                {
                    // Açılıştaki otomatik denetimde kullanıcıyı pencereyle bölme:
                    // tıklanabilir bildirim (pencere tepsideyse Windows balonu) çıkar
                    Notify($"Yeni sürüm hazır: {update.TagName}. Güncellemek için tıklayın.",
                        ToastKind.Info, "update", () => ShowUpdateWindow(update), 15000);
                    return;
                }

                ShowUpdateWindow(update);
            }
            catch (Exception ex)
            {
                LogService.Write("Güncelleme denetimi", ex);

                if (!silent)
                    NotifyError("Güncelleme denetlenemedi: " + ex.Message);
            }
        }

        // Güncelleme penceresi: öneri → indirme (yüzde + çizgi) → kurulum başlıyor.
        // Uygulama, kurulum başlamadan hemen önce bu pencerede ne olacağını gösterir.
        private void ShowUpdateWindow(UpdateInfo update)
        {
            if (_updateInProgress)
                return;

            _updateInProgress = true;

            bool exiting = false;

            try
            {
                // Pencere tepsideyse önce görünür yap ki güncelleme penceresi ortada çıksın
                ShowFromTray();

                var window = new UpdateWindow(_updater, update) { Owner = this };

                if (window.ShowDialog() != true || string.IsNullOrEmpty(window.DownloadedFile))
                    return;

                // Yeniden başlayan yeni sürüm bu notları okuyup "Yenilikler"
                // penceresinde gösterecek (uygulama temasıyla uyumlu, MessageBox değil)
                _settings.Current.PendingWhatsNewVersion = update.Version.ToString(3);
                _settings.Current.PendingWhatsNewNotes = update.Notes;
                _settings.Save();

                _updater.ApplyUpdate(window.DownloadedFile);

                exiting = true;
                ExitApplication();
            }
            catch (Exception ex)
            {
                LogService.Write("Güncelleme başlatma", ex);
                NotifyError("Güncelleme başlatılamadı: " + ex.Message);
            }
            finally
            {
                if (!exiting)
                    _updateInProgress = false;
            }
        }

        private void ApplySettings()
        {
            var s = _settings.Current;

            sliderVolume.Value = Math.Clamp(s.Volume, 0, 100);
            btnShuffle.IsChecked = s.Shuffle;
            SetRepeatMode((RepeatMode)Math.Clamp(s.RepeatModeValue, 0, 2));

            // Kayıtlı arayüz ölçeğini uygula (pencere boyutu zaten o ölçekle kaydedildi)
            ApplyUiScale(s.UiScale, resizeWindow: false, announce: false);

            if (s.WindowWidth >= MinWidth && s.WindowHeight >= MinHeight)
            {
                Width = s.WindowWidth;
                Height = s.WindowHeight;
            }

            if (s.WindowLeft >= 0 && s.WindowTop >= 0 &&
                s.WindowLeft < SystemParameters.VirtualScreenWidth - 100 &&
                s.WindowTop < SystemParameters.VirtualScreenHeight - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.WindowLeft;
                Top = s.WindowTop;
            }

            // Son açık listeyi geri getir
            var lastPlaylist = _playlist.Playlists
                .FirstOrDefault(p => p.Id.ToString() == s.ActivePlaylistId);

            if (lastPlaylist != null)
            {
                _playlist.SetActive(lastPlaylist);
                BindActivePlaylist();
            }

            // Son çalınan şarkıyı seçili getir (otomatik çalmaz)
            var last = _playlist.Playlists
                .SelectMany(p => p.Songs)
                .FirstOrDefault(x => x.Id.ToString() == s.LastSong);

            if (last != null)
            {
                _playlist.SetCurrent(last);
                lstPlaylist.SelectedItem = last;
                txtSong.Text = FormatSongLabel(last);
            }
        }

        // ═══════════════ Arayüz ölçeği ═══════════════

        // Dokunmatik POS ekranlarında butonlar küçük kalmasın diye tüm
        // arayüz %80-%170 arasında ölçeklenebilir
        private const double BaseMinWidth = 840;
        private const double BaseMinHeight = 480;

        private void btnZoomIn_Click(object sender, RoutedEventArgs e) =>
            ApplyUiScale(uiScale.ScaleX + 0.1, resizeWindow: true);

        private void btnZoomOut_Click(object sender, RoutedEventArgs e) =>
            ApplyUiScale(uiScale.ScaleX - 0.1, resizeWindow: true);

        private void ApplyUiScale(double scale, bool resizeWindow, bool announce = true)
        {
            scale = UiScaleHelper.Clamp(scale);

            double previous = uiScale.ScaleX;

            uiScale.ScaleX = scale;
            uiScale.ScaleY = scale;

            var workArea = SystemParameters.WorkArea;

            MinWidth = Math.Min(BaseMinWidth * scale, workArea.Width);
            MinHeight = Math.Min(BaseMinHeight * scale, workArea.Height);

            UiScaleHelper.Current = scale;
            _settings.Current.UiScale = scale;

            // Ölçek değişince pencereyi de aynı oranda büyüt/küçült (ekrana sığdır)
            if (resizeWindow && WindowState == WindowState.Normal &&
                Math.Abs(scale - previous) > 0.001)
            {
                double ratio = scale / previous;

                Width = Math.Clamp(Width * ratio, MinWidth, workArea.Width);
                Height = Math.Clamp(Height * ratio, MinHeight, workArea.Height);

                if (Left + Width > workArea.Right)
                    Left = Math.Max(workArea.Left, workArea.Right - Width);

                if (Top + Height > workArea.Bottom)
                    Top = Math.Max(workArea.Top, workArea.Bottom - Height);
            }

            if (announce)
                NotifyInfo($"Arayüz boyutu: %{Math.Round(scale * 100)}", "zoom", 1500);
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                ApplyUiScale(uiScale.ScaleX + (e.Delta > 0 ? 0.1 : -0.1), resizeWindow: true);
                e.Handled = true;
                return;
            }

            base.OnPreviewMouseWheel(e);
        }

        // ═══════════════ Sistem tepsisi ═══════════════

        private void InitTray()
        {
            _tray = new WinForms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
                Visible = true,
                Text = "LiwaPlayer"
            };

            _tray.DoubleClick += (_, _) => ShowFromTray();

            // Balona tıklanınca: bekleyen "indirilenleri dinle" sorusu ya da bildirime bağlı eylem
            _tray.BalloonTipClicked += (_, _) =>
            {
                if (_offlinePromptPending)
                {
                    OfferDownloadedPlaylist();
                    return;
                }

                var action = _balloonAction;
                _balloonAction = null;
                action?.Invoke();
            };

            var menu = new WinForms.ContextMenuStrip();

            menu.Items.Add("Göster", null, (_, _) => ShowFromTray());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Oynat / Duraklat", null, (_, _) => TogglePlayPause());
            menu.Items.Add("Önceki", null, (_, _) => PreviousOrRestart());
            menu.Items.Add("Sonraki", null, (_, _) =>
                PlaySong(GetNextSong(), CurrentPlaybackPlaylist));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Güncellemeleri Denetle", null,
                async (_, _) => await CheckForUpdatesAsync(silent: false));
            menu.Items.Add("YouTube Hesabını Yenile (avatar/Premium/listeler)", null,
                async (_, _) => await RefreshAccountAsync());
            menu.Items.Add("Çıkış", null, (_, _) => ExitApplication());

            _tray.ContextMenuStrip = menu;
        }

        // ═══════════════ Görev çubuğu düğmeleri ═══════════════

        private void InitTaskbarButtons()
        {
            _taskbarButtons = new TaskbarThumbButtons(this);

            _taskbarButtons.PreviousClicked += () => Dispatcher.Invoke(PreviousOrRestart);
            _taskbarButtons.PlayPauseClicked += () => Dispatcher.Invoke(TogglePlayPause);
            _taskbarButtons.NextClicked += () =>
                Dispatcher.Invoke(() => PlaySong(GetNextSong(), CurrentPlaybackPlaylist));
        }

        // ═══════════════ İnternet bağlantısı ═══════════════

        // Pencere tepsideyken bağlantı koptuysa soru bekletilir (pencere öne fırlayıp
        // odağı çalmaz); kullanıcı balona tıklayınca ya da pencereyi açınca sorulur
        private bool _offlinePromptPending;

        private void OnConnectionLost()
        {
            if (string.Equals(_playlist.ActivePlaylist.Name, DownloadedPlaylistName,
                    StringComparison.OrdinalIgnoreCase))
                return; // zaten indirilenler listesindeyiz, tekrar sorma

            if (!IsVisible)
            {
                _offlinePromptPending = true;

                _tray?.ShowBalloonTip(5000, "LiwaPlayer",
                    "İnternet bağlantısı yok. İndirilen şarkıları dinlemek için buraya tıklayın.",
                    WinForms.ToolTipIcon.Warning);

                return;
            }

            OfferDownloadedPlaylist();
        }

        private void OfferDownloadedPlaylist()
        {
            _offlinePromptPending = false;

            ShowFromTray();

            // Liste boş olsa bile her zaman sorulur; "hayır" derse veya liste
            // boşsa durum çubuğunda bilgi verilir, sessizce atlanmaz
            var answer = MessageBox.Show(this,
                "İnternet bağlantısı yok. İndirilen şarkıları dinlemek ister misiniz?",
                "Bağlantı Yok",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;

            var downloaded = _playlist.Playlists.FirstOrDefault(p =>
                string.Equals(p.Name, DownloadedPlaylistName, StringComparison.OrdinalIgnoreCase));

            if (downloaded == null || downloaded.Songs.Count == 0)
            {
                NotifyWarn("Henüz indirilmiş bir şarkın yok.");
                return;
            }

            _playlist.SetActive(downloaded);
            BindActivePlaylist();
            NotifyInfo("İndirilen şarkılar listesine geçildi.");
        }

        private void OnConnectionRestored()
        {
            // Bağlantı geri geldi: bekleyen "indirilenleri dinle" sorusu geçersiz
            _offlinePromptPending = false;

            // Kullanıcı çevrimdışıyken indirilenler listesine geçmediyse
            // (soruyu reddetti veya sorulmadı), geri alacak bir şey yok
            if (!string.Equals(_playlist.ActivePlaylist.Name, DownloadedPlaylistName,
                    StringComparison.OrdinalIgnoreCase))
                return;

            NotifySuccess("İnternet bağlantısı geri geldi. Müzik listenize geri aktarılıyorsunuz.");

            var music = _playlist.Playlists.FirstOrDefault(p =>
                string.Equals(p.Name, "Müzik", StringComparison.OrdinalIgnoreCase))
                ?? _playlist.Playlists.FirstOrDefault();

            if (music != null)
            {
                _playlist.SetActive(music);
                BindActivePlaylist();
            }
        }

        public void ShowFromTray()
        {
            Show();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Activate();
        }

        private void ExitApplication()
        {
            _reallyExit = true;
            Close();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // X her zaman tepsiye küçültür; gerçek çıkış tepsi menüsündeki Çıkış'tan
            if (_reallyExit)
                return;

            e.Cancel = true;

            // Tepsideyken elektrik kesilse bile ayarlar kaybolmasın
            SaveSettings();

            Hide();

            if (!_balloonShown)
            {
                _tray?.ShowBalloonTip(2500, "LiwaPlayer",
                    "Müzik arka planda çalmaya devam ediyor. " +
                    "Tamamen kapatmak için simgeye sağ tıklayıp Çıkış'ı seçin.",
                    WinForms.ToolTipIcon.Info);

                _balloonShown = true;
            }

            TrimMemory();
        }

        // ═══════════════ Aktif liste bağlama ═══════════════

        private void BindActivePlaylist()
        {
            if (_boundSongs != null)
                _boundSongs.CollectionChanged -= BoundSongs_CollectionChanged;

            _boundSongs = _playlist.ActivePlaylist.Songs;
            _boundSongs.CollectionChanged += BoundSongs_CollectionChanged;

            lstPlaylist.ItemsSource = _boundSongs;

            UpdatePlaylistInfo();

            if (_playlist.CurrentSong != null && _boundSongs.Contains(_playlist.CurrentSong))
            {
                lstPlaylist.SelectedItem = _playlist.CurrentSong;
                lstPlaylist.ScrollIntoView(_playlist.CurrentSong);
            }
        }

        private void BoundSongs_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
            UpdatePlaylistInfo();

        private void UpdatePlaylistInfo()
        {
            var active = _playlist.ActivePlaylist;
            int count = active.Songs.Count;

            txtPlaylistName.Text = active.Name;
            txtCount.Text = count == 0 ? "" : $"{count} şarkı";
            txtPlaylistHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

            txtPlaylistHint.Text =
                $"\"{active.Name}\" listesi boş.\n\n" +
                "Sağdaki kutudan YouTube'da arama yapıp\n" +
                "şarkı ekleyebilir veya üstteki dosya butonuyla\n" +
                "bilgisayardan müzik ekleyebilirsiniz.";
        }

        private void btnManager_Click(object sender, RoutedEventArgs e)
        {
            var manager = new PlaylistManagerWindow(_playlist) { Owner = this };

            manager.ShowDialog();

            // Yönetici penceresinde listeler silinmiş/değişmiş olabilir
            if (_playbackPlaylist != null && !_playlist.Playlists.Contains(_playbackPlaylist))
            {
                _playbackPlaylist = null;

                // Çalan şarkının listesi silindiyse şarkı da çalmaya devam etmesin
                if (_playlist.CurrentSong == null)
                    StopPlayback();
            }

            BindActivePlaylist();
        }

        // ═══════════════ Zamanlayıcı / UI güncelleme ═══════════════

        private void Timer_Tick(object? sender, EventArgs e)
        {
            btnPlayPause.Content = _player.IsPlaying ? "" : "";
            _taskbarButtons?.SetPlaying(_player.IsPlaying);

            if (_player.Duration <= 0)
                return;

            txtCurrent.Text = _player.FormatTime(_player.CurrentTime);
            txtDuration.Text = _player.FormatTime(_player.Duration);

            if (!_draggingSlider)
                sliderPosition.Value = _player.Position * 100;
        }

        // Durum satırı (şarkı adının altı) YALNIZCA çalma durumunu gösterir:
        // "YouTube üzerinden çalınıyor", "Duraklatıldı" gibi. Hata, bağlantı,
        // güncelleme, liste işlemleri gibi haberler bildirim olarak çıkar (Notify).
        private void SetStatus(string message) => txtStatus.Text = message;

        // Pencere tepsideyken balona tıklanınca çalıştırılacak eylem
        private Action? _balloonAction;

        private void Notify(string message, ToastKind kind = ToastKind.Info,
            string? key = null, Action? onClick = null, int durationMs = 0)
        {
            bool visible = IsVisible && WindowState != WindowState.Minimized;

            if (visible)
            {
                toasts.Show(message, kind, key, onClick, durationMs);
                return;
            }

            // Pencere tepsideyken sadece uyarı/hata ve tıklanabilir bildirimler
            // Windows balonu olarak gösterilir; sıradan bilgiler atlanır
            if (kind is ToastKind.Warning or ToastKind.Error || onClick != null)
            {
                _balloonAction = onClick;

                _tray?.ShowBalloonTip(5000, "LiwaPlayer",
                    message.Length > 240 ? message[..237] + "..." : message,
                    kind switch
                    {
                        ToastKind.Error => WinForms.ToolTipIcon.Error,
                        ToastKind.Warning => WinForms.ToolTipIcon.Warning,
                        _ => WinForms.ToolTipIcon.Info
                    });
            }
        }

        private void NotifyInfo(string message, string? key = null, int durationMs = 0) =>
            Notify(message, ToastKind.Info, key, null, durationMs);

        private void NotifySuccess(string message, string? key = null, int durationMs = 0) =>
            Notify(message, ToastKind.Success, key, null, durationMs);

        private void NotifyWarn(string message, string? key = null, int durationMs = 0) =>
            Notify(message, ToastKind.Warning, key, null, durationMs);

        private void NotifyError(string message, string? key = null, int durationMs = 0) =>
            Notify(message, ToastKind.Error, key, null, durationMs);

        // ═══════════════ Kapak ve görselleştirici ═══════════════

        private void SetCoverImage(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    brushCover.ImageSource = null;
                    return;
                }

                // 40px'lik kutu için küçük çözünürlük yeterli (bellek tasarrufu)
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(url);
                image.DecodePixelWidth = 96;
                image.EndInit();

                brushCover.ImageSource = image;
            }
            catch
            {
                brushCover.ImageSource = null;
            }
        }

        // Tepsiye inince/pencereler kapanınca bellek işletim sistemine iade
        // edilir; POS makinelerinde görünür RAM kullanımını düşük tutar
        private static void TrimMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect();

                EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
            }
            catch
            {
            }
        }

        [System.Runtime.InteropServices.DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // 20 saniyede bir çalışır: tavan aşıldıysa belleği hemen iade et
        private static void EnforceMemoryLimit()
        {
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();

                if (process.WorkingSet64 > MemoryLimitBytes)
                    TrimMemory();
            }
            catch
            {
            }
        }

        private void coverBox_Click(object sender, RoutedEventArgs e)
        {
            if (_settings.Current.DisableVisualizer)
            {
                NotifyInfo("Görselleştirici ayarlardan devre dışı bırakılmış.");
                return;
            }

            if (_visualizer != null)
            {
                _visualizer.Activate();
                return;
            }

            _visualizer = new VisualizerWindow(
                () => _player.IsPlaying,
                _player.MediaPlayer,
                SwitchToVideoAsync,
                SwitchToAudioAsync)
            {
                Owner = this
            };

            _visualizer.Closed += (_, _) =>
            {
                _visualizer = null;
                TrimMemory();
            };

            var song = _playlist.CurrentSong;

            if (song != null)
                _visualizer.UpdateSong(song.Title, song.Artist, song.CoverImage);

            _visualizer.Show();
        }

        // Görselleştirici klip moduna geçerken: mevcut şarkıyı kaldığı yerden
        // video+ses olarak yeniden başlatır. Video yoksa false döner.
        private async System.Threading.Tasks.Task<bool> SwitchToVideoAsync()
        {
            var song = _playlist.CurrentSong;

            if (song == null || song.Source != SongSource.YouTube)
                return false;

            long resumeTime = _player.CurrentTime;

            try
            {
                if (song.IsLive)
                {
                    var url = await _youtube.GetLiveStreamUrlAsync(song.FileName);
                    _player.Play(url, withVideo: true);
                }
                else
                {
                    var (videoUrl, audioUrl) = await _youtube.GetVideoStreamsAsync(song.FileName);

                    _player.PlayWithSlaveAudio(videoUrl, audioUrl);

                    // Medyanın açılmasını bekleyip kaldığı saniyeye dön
                    await System.Threading.Tasks.Task.Delay(900);
                    _player.Seek(resumeTime);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"Klip moduna geçiş ({song.Title})", ex);
                return false;
            }
        }

        // Klip modundan çıkarken: sadece ses akışına geri dön (CPU tasarrufu)
        private async System.Threading.Tasks.Task SwitchToAudioAsync()
        {
            var song = _playlist.CurrentSong;

            if (song == null || song.Source != SongSource.YouTube)
                return;

            long resumeTime = _player.CurrentTime;

            try
            {
                if (song.IsLive)
                {
                    var url = await _youtube.GetLiveStreamUrlAsync(song.FileName);
                    _player.Play(url);
                }
                else
                {
                    var url = await _youtube.GetAudioStreamUrlAsync(song.FileName);

                    _player.Play(url);

                    await System.Threading.Tasks.Task.Delay(900);
                    _player.Seek(resumeTime);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"Ses moduna dönüş ({song.Title})", ex);
            }
        }

        private static string FormatSongLabel(Song song) =>
            string.IsNullOrWhiteSpace(song.Artist)
                ? song.Title
                : $"{song.Title} — {song.Artist}";

        // ═══════════════ Doğum günü otomatik tanıma ═══════════════

        private static readonly CultureInfo TurkishCulture = new("tr-TR");

        // "doğmak" fiilinin çekimleri + doğum + birthday.
        // "doğdu" -> doğdun/doğdum/doğdular, "doğac" -> doğacak/doğacaksın gibi
        // tüm türevleri kök olarak yakalar.
        private static readonly string[] BirthdayKeywords =
            { "doğum", "doğdu", "doğac", "doğmuş", "doğmak", "doğar", "birthday" };

        // Yanlış eşleşme yapabilecek kalıplar (gün doğumu = şafak, doğum değil)
        private static readonly string[] BirthdayExclusions =
            { "gündoğ", "gün doğ" };

        private static bool IsBirthdaySong(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            var text = title.ToLower(TurkishCulture);

            foreach (var exclusion in BirthdayExclusions)
                text = text.Replace(exclusion, "");

            return BirthdayKeywords.Any(text.Contains);
        }

        // Doğum günü şarkısıysa ve kullanıcının adında "doğum" geçen bir listesi
        // varsa oraya yönlendirir; yoksa aktif listeye ekler
        private Playlist ResolveTargetPlaylist(string title) =>
            IsBirthdaySong(title)
                ? _playlist.FindBirthdayPlaylist() ?? _playlist.ActivePlaylist
                : _playlist.ActivePlaylist;

        // ═══════════════ Çalma ═══════════════

        private async void PlaySong(Song? song, Playlist? from = null)
        {
            if (song == null)
                return;

            long token = ++_playToken;

            _playbackPlaylist = from ?? _playlist.ActivePlaylist;

            _playlist.SetCurrent(song);

            // Önceki şarkı, yenisi hazır olana kadar çalmaya devam etmesin:
            // altta yeni şarkının adı yazarken eskisinin sesi gelmesi tutarsız
            _player.Stop();
            sliderPosition.Value = 0;
            txtCurrent.Text = "00:00";
            txtDuration.Text = song.IsLive ? "🔴" : song.DurationText;

            // Seçilen şarkı bu arada listeden silindiyse ya da başka bir şarkı
            // seçildiyse çözümlenen akış çalınmamalı
            bool IsStale() => token != _playToken || _playlist.CurrentSong != song;

            if (_boundSongs != null && _boundSongs.Contains(song))
            {
                lstPlaylist.SelectedItem = song;
                lstPlaylist.ScrollIntoView(song);
            }

            txtSong.Text = FormatSongLabel(song);
            Title = $"{_baseTitle} — {song.Title}";

            SetCoverImage(song.CoverImage);
            _visualizer?.UpdateSong(song.Title, song.Artist, song.CoverImage);

            if (_tray != null)
            {
                // NotifyIcon.Text en fazla 63 karakter kabul eder
                var trayText = _baseTitle + " — " + song.Title;
                _tray.Text = trayText.Length > 63 ? trayText[..60] + "..." : trayText;
            }

            try
            {
                // Görselleştirici klip modundaysa yeni şarkı da videolu başlasın
                bool wantVideo = _visualizer is { IsVideoActive: true } &&
                                 song.Source == SongSource.YouTube;

                string location;

                if (song.Source == SongSource.YouTube && song.IsLive)
                {
                    SetStatus("🔴 Canlı yayına bağlanılıyor...");
                    location = await _youtube.GetLiveStreamUrlAsync(song.FileName);

                    if (IsStale())
                        return;

                    _player.Play(location, withVideo: wantVideo);
                    _playStartedAtUtc = DateTime.UtcNow;
                    SetStatus("🔴 Canlı yayın çalınıyor");

                    _settings.Current.LastSong = song.Id.ToString();
                    return;
                }

                if (song.Source == SongSource.YouTube && wantVideo)
                {
                    SetStatus("Klip hazırlanıyor...");
                    var (videoUrl, audioUrl) = await _youtube.GetVideoStreamsAsync(song.FileName);

                    if (IsStale())
                        return;

                    _player.PlayWithSlaveAudio(videoUrl, audioUrl);
                    _playStartedAtUtc = DateTime.UtcNow;
                    SetStatus("Klip oynatılıyor");

                    _settings.Current.LastSong = song.Id.ToString();
                    return;
                }

                if (song.Source == SongSource.YouTube)
                {
                    SetStatus("YouTube akışı hazırlanıyor...");
                    location = await _youtube.GetAudioStreamUrlAsync(song.FileName);

                    // Bekleme sırasında başka şarkı seçildiyse ya da bu şarkı silindiyse çalma
                    if (IsStale())
                        return;
                }
                else
                {
                    if (!File.Exists(song.FileName))
                    {
                        // Durum satırı yalnızca çalma durumunu söyler; ayrıntı bildirimde
                        SetStatus("Çalınamadı");
                        NotifyError("Dosya bulunamadı: " + Path.GetFileName(song.FileName));
                        return;
                    }

                    location = song.FileName;
                }

                _player.Play(location);
                _playStartedAtUtc = DateTime.UtcNow;

                SetStatus(song.Source == SongSource.YouTube
                    ? "YouTube üzerinden çalınıyor"
                    : "Yerel dosyadan çalınıyor");

                _settings.Current.LastSong = song.Id.ToString();

                // Şarkı başladı: sıradakini seç ve adresini arka planda çöz
                PlanNextAndPrefetch(CurrentPlaybackPlaylist);
            }
            catch (Exception ex)
            {
                LogService.Write($"Çalma hatası ({song.Title})", ex);

                if (IsStale())
                    return;

                // "YouTube akışı hazırlanıyor..." eski metninde takılı kalmasın
                SetStatus("Çalınamadı");

                // YouTube bazı makinelere/IP'lere bot doğrulaması dayatır ve tüm
                // videolar için "is not available" döndürür; hesap girişi bunu aşar
                if (ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase))
                {
                    NotifyError(_auth.IsLoggedIn
                        ? "Video açılamadı. Oturum eskimiş olabilir: kişi simgesinden çıkıp yeniden giriş yapın."
                        : "YouTube bu makinede doğrulama istiyor: kişi simgesinden hesabınla giriş yapıp tekrar deneyin.");
                }
                else
                {
                    NotifyError("Çalınamadı: " + ex.Message);
                }
            }
        }

        private Playlist CurrentPlaybackPlaylist =>
            _playbackPlaylist != null && _playlist.Playlists.Contains(_playbackPlaylist)
                ? _playbackPlaylist
                : _playlist.ActivePlaylist;

        // Sıradaki şarkıyı seçer (karıştırma açıksa rastgele)
        private Song? PickNextSong(Playlist playlist)
        {
            var songs = playlist.Songs;

            if (songs.Count == 0)
                return null;

            if (btnShuffle.IsChecked == true && songs.Count > 1)
            {
                Song pick;
                do
                {
                    pick = songs[_random.Next(songs.Count)];
                } while (pick == _playlist.CurrentSong);

                return pick;
            }

            return _playlist.Next(playlist);
        }

        private Song? GetNextSong()
        {
            var playlist = CurrentPlaybackPlaylist;

            // Önceden seçilip adresi çözülmüş sıradaki şarkı varsa onu kullan
            // (karıştırmada da hazırlanan şarkının çalması için)
            bool shuffle = btnShuffle.IsChecked == true;

            if (_plannedNext != null &&
                _plannedForShuffle == shuffle &&
                _plannedNext != _playlist.CurrentSong &&
                playlist.Songs.Contains(_plannedNext))
                return _plannedNext;

            return PickNextSong(playlist);
        }

        // Çalan şarkıdan sonra gelecek şarkıyı belirler ve YouTube ise akış
        // adresini arka planda önceden çözer; şarkı geçişindeki bekleme
        // (1-3 sn) böylece kalkar
        private void PlanNextAndPrefetch(Playlist playlist)
        {
            _plannedForShuffle = btnShuffle.IsChecked == true;
            _plannedNext = PickNextSong(playlist);

            if (_plannedNext is { Source: SongSource.YouTube, IsLive: false } next)
                _youtube.PrefetchAudio(next.FileName);
        }

        // Çalan şarkıyı hemen durdurur, arayüzü sıfırlar ve çözümlenmekte olan
        // (henüz başlamamış) çalma isteklerini de iptal eder. Çalan şarkı silinince
        // ya da listesi temizlenince kullanılır — aksi halde çözümlenmesi biten
        // akış silinmiş şarkıyı yine de başlatırdı.
        private void StopPlayback()
        {
            _playToken++;
            _plannedNext = null;

            _player.Stop();

            sliderPosition.Value = 0;
            txtCurrent.Text = "00:00";
            txtDuration.Text = "00:00";

            txtSong.Text = "Şarkı seçilmedi";
            Title = _baseTitle;

            SetCoverImage("");
            _visualizer?.UpdateSong("", "", "");

            if (_tray != null)
                _tray.Text = _baseTitle.Length > 63 ? _baseTitle[..60] + "..." : _baseTitle;
        }

        // ═══════════════ Tekrar modu ═══════════════

        private void btnRepeat_Click(object sender, RoutedEventArgs e)
        {
            SetRepeatMode(_repeatMode switch
            {
                RepeatMode.Off => RepeatMode.All,
                RepeatMode.All => RepeatMode.One,
                _ => RepeatMode.Off
            }, announce: true);
        }

        private void SetRepeatMode(RepeatMode mode, bool announce = false)
        {
            _repeatMode = mode;

            // ToggleButton tıklamada kendiliğinden döner; üç durumu biz yönetiyoruz
            btnRepeat.IsChecked = mode != RepeatMode.Off;

            // E8ED = "tek şarkı tekrarı", E8EE = "liste tekrarı" (Segoe MDL2)
            btnRepeat.Content = mode == RepeatMode.One ? "" : "";

            var text = mode switch
            {
                RepeatMode.All => "Tekrar: liste bitince başa dön",
                RepeatMode.One => "Tekrar: aynı şarkıyı sürekli çal",
                _ => "Tekrar: kapalı"
            };

            btnRepeat.ToolTip = text;
            AutomationProperties.SetName(btnRepeat, text);

            if (announce)
                NotifyInfo(text, "repeat", 1800);
        }

        private void OnPlaybackEnded()
        {
            if (_playlist.CurrentSong == null)
                return;

            // Canlı yayın normalde bitmez; buraya düştüyse bağlantı kopmuştur
            if (_playlist.CurrentSong.IsLive)
            {
                NotifyWarn("🔴 Yayın koptu, yeniden bağlanılıyor...");
                PlaySong(_playlist.CurrentSong, CurrentPlaybackPlaylist);
                return;
            }

            var playlist = CurrentPlaybackPlaylist;
            var songs = playlist.Songs;

            if (songs.Count == 0)
                return;

            // Aynı şarkıyı sürekli çal: bitince yeniden başlat. Adres önbellekte
            // olduğundan ağa gitmeden hemen başlar.
            if (_repeatMode == RepeatMode.One)
            {
                // Şarkı daha başlar başlamaz bittiyse (bozuk/süresi dolmuş akış)
                // sonsuz döngüye girip YouTube'u yormasın
                if (DateTime.UtcNow - _playStartedAtUtc < TimeSpan.FromSeconds(3))
                {
                    NotifyWarn("Şarkı tekrarlanamadı (akış hemen sona erdi).");
                    return;
                }

                PlaySong(_playlist.CurrentSong, playlist);
                return;
            }

            bool shuffle = btnShuffle.IsChecked == true;
            bool repeat = _repeatMode == RepeatMode.All;
            bool isLast = songs.IndexOf(_playlist.CurrentSong) == songs.Count - 1;

            if (!shuffle && !repeat && isLast)
            {
                // Ayar açıksa benzer şarkı bulup listeye ekleyerek devam et
                if (_settings.Current.AutoContinue)
                {
                    _ = AutoContinueAsync(playlist, _playlist.CurrentSong);
                    return;
                }

                SetStatus("Liste bitti.");
                sliderPosition.Value = 0;
                return;
            }

            PlaySong(GetNextSong(), playlist);
        }

        // Liste bittiğinde son çalan şarkının sanatçısına göre benzer bir şarkı
        // bulur, listeye ekler ve çalar (Optimizasyon Ayarları → Oynatma'dan açılır)
        private async System.Threading.Tasks.Task AutoContinueAsync(Playlist playlist, Song lastSong)
        {
            try
            {
                SetStatus("Benzer şarkı aranıyor...");

                var query = string.IsNullOrWhiteSpace(lastSong.Artist)
                    ? lastSong.Title
                    : lastSong.Artist;

                System.Collections.Generic.List<YouTubeSearchResult> results;

                try
                {
                    results = await _youtube.SearchMusicAsync(query, 15);
                }
                catch
                {
                    results = await _youtube.SearchAsync(query, 15);
                }

                var known = playlist.Songs
                    .Where(s => s.Source == SongSource.YouTube)
                    .Select(s => s.FileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var pick = results
                    .Where(r => !known.Contains(r.VideoId))
                    .OrderBy(_ => _random.Next())
                    .FirstOrDefault();

                if (pick == null)
                {
                    SetStatus("Liste bitti (benzer şarkı bulunamadı).");
                    return;
                }

                var song = _playlist.Add(new Song
                {
                    FileName = pick.VideoId,
                    Title = pick.Title,
                    Artist = pick.Author,
                    Duration = pick.Duration,
                    Source = SongSource.YouTube,
                    CoverImage = pick.ThumbnailUrl
                }, playlist, out _);

                PlaySong(song, playlist);
                NotifyInfo($"✨ Otomatik eklendi: {pick.Title}");
            }
            catch (Exception ex)
            {
                LogService.Write("Otomatik devam", ex);
                SetStatus("Liste bitti.");
            }
        }

        // ═══════════════ Oynatıcı butonları ═══════════════

        private void btnPlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

        private void TogglePlayPause()
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
                SetStatus("Duraklatıldı");
            }
            else if (_player.HasMedia)
            {
                _player.Resume();
                SetStatus("Çalınıyor");
            }
            else if (lstPlaylist.SelectedItem is Song selected)
            {
                PlaySong(selected);
            }
            else
            {
                PlaySong(_playlist.CurrentSong ?? _playlist.ActivePlaylist.Songs.FirstOrDefault());
            }
        }

        private void btnNext_Click(object sender, RoutedEventArgs e) =>
            PlaySong(GetNextSong(), CurrentPlaybackPlaylist);

        private void btnPrevious_Click(object sender, RoutedEventArgs e) => PreviousOrRestart();

        // Spotify tarzı "akıllı önceki": şarkı birkaç saniyeden fazla ilerlediyse
        // başa sarar; en baştaysa (veya canlı yayınsa) gerçek önceki şarkıya geçer
        private void PreviousOrRestart()
        {
            bool nearStart = !_player.HasMedia || _player.CurrentTime <= PreviousRestartThresholdMs;
            bool isLive = _playlist.CurrentSong?.IsLive ?? false;

            if (!nearStart && !isLive)
            {
                _player.Seek(0);
                SetStatus("Baştan başlatıldı.");
                return;
            }

            PlaySong(_playlist.Previous(CurrentPlaybackPlaylist), CurrentPlaybackPlaylist);
        }

        private void sliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player == null)
                return;

            int volume = (int)e.NewValue;

            _player.Volume = volume;

            if (txtVolume != null)
                txtVolume.Text = "%" + volume;
        }

        // Fare imleci ses çubuğunun üzerindeyken tekerlekle ses ayarlanır
        private void sliderVolume_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            sliderVolume.Value = Math.Clamp(
                sliderVolume.Value + (e.Delta > 0 ? 5 : -5),
                sliderVolume.Minimum,
                sliderVolume.Maximum);

            e.Handled = true;
        }

        // ═══════════════ Playlist işlemleri ═══════════════

        private void lstPlaylist_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstPlaylist.SelectedItem is Song song)
                PlaySong(song);
        }

        private async void btnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Müzik Dosyaları|*.mp3;*.m4a;*.aac;*.wav;*.flac;*.ogg;*.wma|Tüm Dosyalar|*.*"
            };

            if (dlg.ShowDialog() != true)
                return;

            int addedCount = 0;
            int birthdayCount = 0;

            foreach (var file in dlg.FileNames)
            {
                var song = new Song
                {
                    FileName = file,
                    Title = Path.GetFileNameWithoutExtension(file),
                    Source = SongSource.Local
                };

                try
                {
                    var (duration, title, artist) = await _player.GetLocalMetadataAsync(file);

                    song.Duration = duration;

                    if (!string.IsNullOrWhiteSpace(title))
                        song.Title = title;

                    if (!string.IsNullOrWhiteSpace(artist))
                        song.Artist = artist;
                }
                catch
                {
                    // Etiket okunamazsa dosya adıyla devam et
                }

                var target = ResolveTargetPlaylist(song.Title);

                _playlist.Add(song, target, out bool added);

                if (added)
                {
                    addedCount++;

                    if (target != _playlist.ActivePlaylist)
                        birthdayCount++;
                }
            }

            if (addedCount == 0)
                NotifyInfo("Seçilen şarkılar zaten listede.");
            else if (birthdayCount > 0)
                NotifySuccess($"{addedCount} şarkı eklendi ({birthdayCount} tanesi 🎂 doğum günü listesine).");
            else
                NotifySuccess($"{addedCount} şarkı eklendi.");
        }

        private void btnSpotify_Click(object sender, RoutedEventArgs e)
        {
            var import = new SpotifyImportWindow(_playlist, _youtube) { Owner = this };

            import.ShowDialog();

            if (import.ImportedPlaylist != null)
            {
                BindActivePlaylist();
                NotifySuccess($"\"{import.ImportedPlaylist.Name}\" listesi Spotify'dan aktarıldı.");
            }
        }

        private void btnYouTubeImport_Click(object sender, RoutedEventArgs e)
        {
            var import = new YouTubeImportWindow(_playlist, _youtube) { Owner = this };

            import.ShowDialog();

            if (import.ImportedPlaylist != null)
            {
                BindActivePlaylist();
                NotifySuccess($"\"{import.ImportedPlaylist.Name}\" listesi YouTube'dan aktarıldı.");
            }
        }

        private void btnMoveSong_Click(object sender, RoutedEventArgs e)
        {
            if (lstPlaylist.SelectedItem is not Song song)
            {
                NotifyWarn("Taşımak için önce listeden bir şarkı seçin.");
                return;
            }

            var targets = _playlist.Playlists
                .Where(p => p != _playlist.ActivePlaylist)
                .ToList();

            if (targets.Count == 0)
            {
                NotifyWarn("Başka liste yok. Önce kütüphane butonundan yeni bir liste oluşturun.");
                return;
            }

            var menu = new ContextMenu
            {
                Style = (Style)FindResource("DarkContextMenu"),
                PlacementTarget = (UIElement)sender,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            foreach (var target in targets)
            {
                var item = new MenuItem
                {
                    Style = (Style)FindResource("DarkMenuItem"),
                    Header = $"{target.Name}  ({target.Songs.Count} şarkı)"
                };

                var captured = target;
                item.Click += (_, _) => MoveSongTo(song, captured);

                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        private void MoveSongTo(Song song, Playlist target)
        {
            if (!_playlist.MoveSong(song, _playlist.ActivePlaylist, target))
            {
                NotifyInfo($"\"{song.Title}\" zaten \"{target.Name}\" listesinde var.");
                return;
            }

            NotifySuccess($"\"{song.Title}\" → \"{target.Name}\" listesine taşındı.");
        }

        // ═══════════════ MP3 indirme ═══════════════

        private void btnDownloadSong_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Song song)
                return;

            if (song.Source != SongSource.YouTube || song.IsLive)
                return;

            if (_downloadQueue.Contains(song))
            {
                NotifyInfo("Bu şarkı zaten indirme kuyruğunda.");
                return;
            }

            _downloadQueue.Enqueue(song);

            // Tüm indirme haberleri aynı "download" bildirimini günceller (üst üste yığılmaz)
            NotifyInfo(_downloading
                ? $"Kuyruğa eklendi ({_downloadQueue.Count} bekliyor): {song.Title}"
                : $"İndirme başlıyor: {song.Title}", "download");

            _ = ProcessDownloadQueueAsync();
        }

        private async System.Threading.Tasks.Task ProcessDownloadQueueAsync()
        {
            if (_downloading)
                return;

            _downloading = true;

            try
            {
                // yt-dlp + ffmpeg ilk kullanımda otomatik kurulur
                if (!_downloader.ToolsReady)
                    await _downloader.EnsureToolsAsync(new Progress<string>(s => NotifyInfo(s, "download", 6000)));

                while (_downloadQueue.Count > 0)
                {
                    var song = _downloadQueue.Dequeue();

                    var (ok, filePath) = await _downloader.DownloadMp3Async(song.FileName,
                        new Progress<int>(p => NotifyInfo($"⬇ {song.Title} — %{p}", "download", 3000)));

                    if (ok && !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                    {
                        await AddDownloadedSongToPlaylistAsync(filePath, song);
                        NotifySuccess($"✔ İndirildi: {song.Title}  ({DownloadedPlaylistName} listesine eklendi)", "download");
                    }
                    else if (ok)
                    {
                        // Dosya yolu ayrıştırılamadıysa (beklenmeyen yt-dlp çıktısı)
                        // en azından dosya diskte var, sadece listeye eklenemedi
                        NotifySuccess($"✔ İndirildi: {song.Title}  (Müzik\\LiwaPlayer)", "download");
                    }
                    else
                    {
                        NotifyError($"İndirilemedi: {song.Title}", "download");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Write("MP3 indirme", ex);
                NotifyError("İndirme hatası: " + ex.Message, "download");
                _downloadQueue.Clear();
            }
            finally
            {
                _downloading = false;
            }
        }

        // İndirilen MP3'ü "İndirilen Şarkılar" listesine yerel bir şarkı olarak
        // ekler (liste yoksa oluşturulur); dosyadan etiket okumayı dener
        private async Task AddDownloadedSongToPlaylistAsync(string filePath, Song originalSong)
        {
            var target = _playlist.Playlists.FirstOrDefault(p =>
                string.Equals(p.Name, DownloadedPlaylistName, StringComparison.OrdinalIgnoreCase))
                ?? _playlist.CreatePlaylist(DownloadedPlaylistName);

            var localSong = new Song
            {
                FileName = filePath,
                Title = originalSong.Title,
                Artist = originalSong.Artist,
                Source = SongSource.Local,
                CoverImage = originalSong.CoverImage
            };

            try
            {
                var (duration, title, artist) = await _player.GetLocalMetadataAsync(filePath);

                localSong.Duration = duration;

                if (!string.IsNullOrWhiteSpace(title))
                    localSong.Title = title;

                if (!string.IsNullOrWhiteSpace(artist))
                    localSong.Artist = artist;
            }
            catch
            {
                // Etiket okunamazsa YouTube'dan gelen başlık/sanatçı adıyla devam
            }

            var added = _playlist.Add(localSong, target, out _);

            // İndirilen şarkının nereye gittiği belirsiz kalmasın diye o listeye
            // otomatik geçilir — kullanıcı elle aramak zorunda kalmaz
            _playlist.SetActive(target);
            BindActivePlaylist();

            lstPlaylist.SelectedItem = added;
            lstPlaylist.ScrollIntoView(added);

            // Pencere görünürken bildirim kutusu zaten gösteriliyor; balon yalnızca tepsideyken
            if (!IsVisible)
            {
                _tray?.ShowBalloonTip(3000, "LiwaPlayer",
                    $"\"{added.Title}\" indirildi ve \"{DownloadedPlaylistName}\" listesine eklendi.",
                    WinForms.ToolTipIcon.Info);
            }
        }

        private void btnDelete_Click(object sender, RoutedEventArgs e) => RemoveSelectedSong();

        private void RemoveSelectedSong()
        {
            if (lstPlaylist.SelectedItem is not Song song)
            {
                NotifyWarn("Silmek için önce listeden şarkı seçin.");
                return;
            }

            bool wasCurrent = _playlist.CurrentSong == song;

            _playlist.RemoveFromActive(song);

            // Çalan (ya da çalmak üzere akışı çözülen) şarkı silindiyse durdur
            if (wasCurrent)
                StopPlayback();

            NotifySuccess("Şarkı listeden çıkarıldı.");
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            var active = _playlist.ActivePlaylist;

            if (active.Songs.Count == 0)
                return;

            var answer = MessageBox.Show(
                $"\"{active.Name}\" listesindeki {active.Songs.Count} şarkı kaydı silinecek.\n" +
                "(Diğer listeler ve bilgisayardaki dosyalar korunur.)\n\nEmin misiniz?",
                "Listeyi Temizle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            bool currentWasHere = _playlist.CurrentSong != null &&
                                  active.Songs.Contains(_playlist.CurrentSong);

            _playlist.ClearActive();

            if (currentWasHere)
                StopPlayback();

            NotifySuccess($"\"{active.Name}\" listesi temizlendi.");
        }

        // ═══════════════ YouTube hesap girişi ═══════════════

        private void UpdateAccountButton()
        {
            if (_auth.IsLoggedIn)
            {
                iconAccountDefault.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                avatarBox.ToolTip = "YouTube oturumu açık — çıkış yapmak için tıkla";
                AutomationProperties.SetName(avatarBox, "YouTube hesabı: giriş yapılmış, çıkış için tıkla");
            }
            else
            {
                iconAccountDefault.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                avatarImageHost.Visibility = Visibility.Collapsed;
                brushAvatar.ImageSource = null;
                premiumBadge.Visibility = Visibility.Collapsed;
                _isPremiumAccount = false;

                avatarBox.ToolTip = "YouTube hesabıyla giriş yap (Premium: reklamsız + kendi listelerin)";
                AutomationProperties.SetName(avatarBox, "YouTube hesabı: giriş yapılmadı");
            }
        }

        // Avatar/Premium bilgisi belgelenmemiş bir uç noktadan en iyi çabayla
        // çekilir; başarısız olursa (info == null) mevcut varsayılan görünüm korunur
        private void ApplyAccountInfo(YouTubeAccountInfo? info)
        {
            if (info == null)
                return;

            try
            {
                if (!string.IsNullOrWhiteSpace(info.AvatarUrl))
                {
                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(info.AvatarUrl);
                    image.DecodePixelWidth = 60;
                    image.EndInit();

                    brushAvatar.ImageSource = image;
                    avatarImageHost.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                // Avatar indirilemezse hesap simgesi (aksan renkli) görünümde kalır
            }

            _isPremiumAccount = info.IsPremium;
            premiumBadge.Visibility = info.IsPremium ? Visibility.Visible : Visibility.Collapsed;

            var nameSuffix = string.IsNullOrWhiteSpace(info.ChannelName) ? "" : $" ({info.ChannelName})";
            avatarBox.ToolTip = $"YouTube oturumu açık{nameSuffix} — çıkış yapmak için tıkla";
            AutomationProperties.SetName(avatarBox,
                $"YouTube hesabı{nameSuffix}: giriş yapılmış, çıkış için tıkla");
        }

        // Girişten sonra (hem yeni giriş hem açılışta hatırlanan oturum için)
        // avatar/Premium bilgisi ve Beğenilen/Daha Sonra İzle listeleri çekilir
        private async Task OnLoggedInAsync()
        {
            var info = await _youtube.FetchAccountInfoAsync(_auth.Cookies);
            ApplyAccountInfo(info);

            await ImportSpecialPlaylistsAsync();
        }

        // Uygulama yeniden başlatılmadan hesap bilgilerini/listelerini elle
        // tazelemek için (tepsi menüsü → "YouTube Hesabını Yenile")
        // Bir önceki çalıştırmada güncelleme uygulandıysa (bkz. CheckForUpdatesAsync)
        // yeni sürümün notlarını, uygulama temasıyla uyumlu kendi penceremizde
        // bir kez gösterir. Gösterilir gösterilmez bayrak temizlenir; bir daha çıkmaz.
        private void ShowWhatsNewIfPending()
        {
            var s = _settings.Current;

            if (string.IsNullOrWhiteSpace(s.PendingWhatsNewVersion))
                return;

            var pendingVersion = s.PendingWhatsNewVersion;
            var pendingNotes = s.PendingWhatsNewNotes;

            s.PendingWhatsNewVersion = "";
            s.PendingWhatsNewNotes = "";
            _settings.Save();

            // Sürüm beklenenle eşleşmiyorsa (ör. güncelleme başka bir şekilde
            // iptal oldu) yanlış sürüm için "yenilikler" gösterme
            if (!string.Equals(pendingVersion, UpdateService.CurrentVersion.ToString(3),
                    StringComparison.OrdinalIgnoreCase))
                return;

            var window = new WhatsNewWindow(pendingVersion, pendingNotes) { Owner = this };

            window.ShowDialog();
        }

        private async Task RefreshAccountAsync()
        {
            if (!_auth.IsLoggedIn)
            {
                ShowFromTray();
                NotifyWarn("Önce YouTube hesabınla giriş yapmalısın.");
                return;
            }

            ShowFromTray();
            NotifyInfo("Hesap bilgileri yenileniyor...", "account");

            await OnLoggedInAsync();

            NotifySuccess("Hesap bilgileri yenilendi.", "account");
        }

        private async Task ImportSpecialPlaylistsAsync()
        {
            if (!_auth.IsLoggedIn)
                return;

            await ImportSpecialPlaylistAsync("Beğenilen Videolar", "LL");
            await ImportSpecialPlaylistAsync("Daha Sonra İzle", "WL");

            await ImportOwnPlaylistsAsync();
        }

        // Hesaptaki tüm kendi oynatma listelerini (LL/WL hariç) tek tek çekip
        // yerel playlist'lere dönüştürür. Kütüphane taraması en-iyi-çaba
        // olduğundan boş/başarısız dönerse sessizce hiçbir şey yapılmaz.
        private async Task ImportOwnPlaylistsAsync()
        {
            try
            {
                var playlists = await _youtube.FetchOwnPlaylistsAsync(_auth.Cookies);

                if (playlists.Count == 0)
                    return;

                int syncedCount = 0;

                foreach (var (playlistId, title) in playlists)
                {
                    await ImportSpecialPlaylistAsync(title, playlistId);
                    syncedCount++;
                }

                NotifySuccess($"Hesabındaki {syncedCount} oynatma listesi senkronize edildi.", "sync");
            }
            catch (Exception ex)
            {
                LogService.Write("Hesap listeleri içe aktarma", ex);
            }
        }

        // "LL" (Beğenilen Videolar) ve "WL" (Daha Sonra İzle) YouTube'un özel
        // liste kimlikleridir; hesap girişiyle çekilip yerel bir listeye
        // dönüştürülür. Liste zaten varsa sadece yeni şarkılar eklenir (dedup).
        private async Task ImportSpecialPlaylistAsync(string name, string listId)
        {
            try
            {
                var (_, videos) = await _youtube.GetPlaylistAsync(
                    $"https://www.youtube.com/playlist?list={listId}");

                if (videos.Count == 0)
                    return;

                var target = _playlist.Playlists.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? _playlist.CreatePlaylist(name);

                int added = 0;

                foreach (var video in videos)
                {
                    _playlist.Add(new Song
                    {
                        FileName = video.VideoId,
                        Title = video.Title,
                        Artist = video.Author,
                        Duration = video.Duration,
                        Source = SongSource.YouTube,
                        CoverImage = video.ThumbnailUrl
                    }, target, out bool wasAdded);

                    if (wasAdded)
                        added++;
                }

                if (added > 0)
                    NotifySuccess($"\"{name}\" listesi güncellendi (+{added} şarkı).", "sync");
            }
            catch (Exception ex)
            {
                LogService.Write($"Özel liste içe aktarma ({listId})", ex);
                // Bu otomatik bir arka plan işlemi; kullanıcıyı hata ile rahatsız etme
            }
        }

        private void btnAccount_Click(object sender, RoutedEventArgs e)
        {
            if (_auth.IsLoggedIn)
            {
                var answer = MessageBox.Show(
                    "YouTube oturumu kapatılsın mı?",
                    "Çıkış",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer != MessageBoxResult.Yes)
                    return;

                _auth.Logout();
                _youtube.SetAuthCookies(null);

                UpdateAccountButton();
                NotifyInfo("YouTube oturumu kapatıldı.");
                return;
            }

            var login = new LoginWindow { Owner = this };

            if (login.ShowDialog() == true && login.ResultCookies is { Count: > 0 })
            {
                _auth.SaveCookies(login.ResultCookies);
                _youtube.SetAuthCookies(_auth.Cookies);

                UpdateAccountButton();
                NotifySuccess("YouTube girişi yapıldı. Premium hesapta akışlar reklamsızdır.");

                _ = OnLoggedInAsync();
            }
        }

        // ═══════════════ YouTube arama ═══════════════

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SearchYouTubeAsync();
        }

        // Radyo sekmesine geçilince kutu boşsa hazır kanallar listelensin;
        // kullanıcı kendi sorgusunu yazmışsa o sorgu radyo filtresiyle yeniden aranır
        private void rbSearchRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
                return;

            if (string.IsNullOrWhiteSpace(txtSearch.Text))
                txtSearch.Text = AutoRadioQuery;

            RunSearchIfNotImport();
        }

        // Video/Müzik sekmesine geçilince: radyo için kendiliğinden yazılan
        // "canlı radyo" metni silinir ve öneriler geri gelir (elle silmek gerekmesin);
        // kullanıcının kendi yazdığı sorgu ise yeni modda yeniden aranır
        private async void rbSearchMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
                return;

            var text = txtSearch.Text.Trim();

            if (text.Length == 0 ||
                string.Equals(text, AutoRadioQuery, StringComparison.OrdinalIgnoreCase))
            {
                txtSearch.Clear();

                lstResults.ItemsSource = null;
                txtRecoTitle.Visibility = Visibility.Collapsed;
                txtSearchHint.Text = "Şarkı veya sanatçı adı yazıp Enter'a basın.";
                txtSearchHint.Visibility = Visibility.Visible;

                await LoadRecommendationsAsync();
                return;
            }

            RunSearchIfNotImport();
        }

        // Liste bağlantısı yapıştırılmışsa mod değişince yeniden içe aktarma yapılmasın
        private void RunSearchIfNotImport()
        {
            if (!txtSearch.Text.Contains("list=", StringComparison.OrdinalIgnoreCase))
                SearchYouTubeAsync();
        }

        private void btnSearch_Click(object sender, RoutedEventArgs e) => SearchYouTubeAsync();

        private async void SearchYouTubeAsync()
        {
            var query = txtSearch.Text.Trim();

            // Boş aramada öneriler tazelenir
            if (query.Length == 0)
            {
                await LoadRecommendationsAsync();
                return;
            }

            // Arama kutusuna liste bağlantısı yapıştırıldıysa içe aktar
            if (query.Contains("list=", StringComparison.OrdinalIgnoreCase))
            {
                await ImportYouTubePlaylistAsync(query);
                return;
            }

            btnSearch.IsEnabled = false;
            lstResults.ItemsSource = null;
            txtRecoTitle.Visibility = Visibility.Collapsed;
            txtSearchHint.Text = "Aranıyor...";
            txtSearchHint.Visibility = Visibility.Visible;

            try
            {
                int max = Math.Clamp(_settings.Current.SearchResultCount, 5, 50);

                var results =
                    rbSearchRadio.IsChecked == true ? await _youtube.SearchLiveAsync(query, max) :
                    rbSearchMusic.IsChecked == true ? await _youtube.SearchMusicAsync(query, max) :
                    await _youtube.SearchAsync(query, max);

                lstResults.ItemsSource = results;

                txtSearchHint.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                if (results.Count == 0)
                    txtSearchHint.Text = "Sonuç bulunamadı.";
            }
            catch (Exception ex)
            {
                LogService.Write($"Arama hatası ({query})", ex);

                txtSearchHint.Text = "Arama başarısız oldu.\nİnternet bağlantısını kontrol edin.";
                txtSearchHint.Visibility = Visibility.Visible;
            }
            finally
            {
                btnSearch.IsEnabled = true;
            }
        }

        // YouTube / YouTube Music liste bağlantısını yeni bir yerel listeye aktarır.
        // Giriş yapılmışsa kullanıcının kendi özel listeleri de çekilebilir.
        private async System.Threading.Tasks.Task ImportYouTubePlaylistAsync(string url)
        {
            btnSearch.IsEnabled = false;
            txtSearchHint.Text = "Liste alınıyor, lütfen bekleyin...";
            txtSearchHint.Visibility = Visibility.Visible;
            lstResults.ItemsSource = null;

            try
            {
                var (title, videos) = await _youtube.GetPlaylistAsync(url);

                if (videos.Count == 0)
                {
                    txtSearchHint.Text = "Listede video bulunamadı.";
                    return;
                }

                // İsim çakışırsa sonuna numara ekle
                string name = string.IsNullOrWhiteSpace(title) ? "YouTube Listesi" : title;
                string finalName = name;
                int suffix = 2;

                while (_playlist.NameExists(finalName))
                    finalName = $"{name} ({suffix++})";

                var target = _playlist.CreatePlaylist(finalName);

                foreach (var video in videos)
                {
                    var song = new Song
                    {
                        FileName = video.VideoId,
                        Title = video.Title,
                        Artist = video.Author,
                        Duration = video.Duration,
                        Source = SongSource.YouTube,
                        CoverImage = video.ThumbnailUrl
                    };

                    _playlist.Add(song, target, out _);
                }

                _playlist.SetActive(target);
                BindActivePlaylist();

                txtSearch.Clear();
                txtSearchHint.Text = "Şarkı veya sanatçı adı yazıp Enter'a basın.";
                txtSearchHint.Visibility = Visibility.Visible;

                NotifySuccess($"\"{finalName}\" içe aktarıldı ({videos.Count} şarkı).");
            }
            catch (Exception ex)
            {
                LogService.Write($"Liste içe aktarma hatası ({url})", ex);

                txtSearchHint.Text = "Liste alınamadı.\n" +
                    (_auth.IsLoggedIn
                        ? ex.Message
                        : "Özel bir listeyse önce hesabınla giriş yapmalısın.");
            }
            finally
            {
                btnSearch.IsEnabled = true;
            }
        }

        private Song AddResultToPlaylist(YouTubeSearchResult result, out bool added, out Playlist target)
        {
            var song = new Song
            {
                FileName = result.VideoId,
                Title = result.Title,
                Artist = result.Author,
                Duration = result.Duration,
                Source = SongSource.YouTube,
                CoverImage = result.ThumbnailUrl,
                IsLive = result.IsLive
            };

            target = ResolveTargetPlaylist(result.Title);

            return _playlist.Add(song, target, out added);
        }

        private void btnAddResult_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not YouTubeSearchResult result)
                return;

            AddResultToPlaylist(result, out bool added, out Playlist target);

            if (!added)
                NotifyInfo($"Bu şarkı \"{target.Name}\" listesinde zaten var.");
            else if (target != _playlist.ActivePlaylist)
                NotifySuccess($"🎂 Doğum günü şarkısı algılandı, \"{target.Name}\" listesine eklendi.");
            else
                NotifySuccess($"\"{target.Name}\" listesine eklendi.");
        }

        private void btnPlayResult_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not YouTubeSearchResult result)
                return;

            var song = AddResultToPlaylist(result, out _, out Playlist target);

            PlaySong(song, target);
        }

        private void lstResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstResults.SelectedItem is not YouTubeSearchResult result)
                return;

            var song = AddResultToPlaylist(result, out _, out Playlist target);

            PlaySong(song, target);
        }

        // ═══════════════ Klavye kısayolları ═══════════════

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Arama kutusuna yazarken kısayollar devreye girmesin
            if (Keyboard.FocusedElement is TextBox)
                return;

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            switch (e.Key)
            {
                case Key.OemPlus or Key.Add when ctrl:
                    ApplyUiScale(uiScale.ScaleX + 0.1, resizeWindow: true);
                    e.Handled = true;
                    break;

                case Key.OemMinus or Key.Subtract when ctrl:
                    ApplyUiScale(uiScale.ScaleX - 0.1, resizeWindow: true);
                    e.Handled = true;
                    break;

                case Key.Space:
                    btnPlayPause_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                case Key.Delete:
                    if (Keyboard.FocusedElement is ListViewItem || lstPlaylist.IsKeyboardFocusWithin)
                    {
                        RemoveSelectedSong();
                        e.Handled = true;
                    }
                    break;

                case Key.Right:
                    _player.Forward(5);
                    e.Handled = true;
                    break;

                case Key.Left:
                    _player.Backward(5);
                    e.Handled = true;
                    break;
            }
        }

        // ═══════════════ Kapanış ═══════════════

        private void SaveSettings()
        {
            var s = _settings.Current;

            s.Volume = (int)sliderVolume.Value;
            s.Shuffle = btnShuffle.IsChecked == true;
            s.RepeatModeValue = (int)_repeatMode;
            s.Repeat = _repeatMode != RepeatMode.Off;
            s.ActivePlaylistId = _playlist.ActivePlaylist.Id.ToString();

            if (WindowState == WindowState.Normal)
            {
                s.WindowWidth = Width;
                s.WindowHeight = Height;
                s.WindowLeft = Left;
                s.WindowTop = Top;
            }

            _settings.Save();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            _themeTimer.Stop();
            _memoryGuard.Stop();

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            SaveSettings();
            _playlist.Save();
            _player.Dispose();
            _taskbarButtons?.Dispose();
            _network.Dispose();

            base.OnClosed(e);
        }
    }
}
