using LiwaPlayer.Models;
using LiwaPlayer.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        // geldiğinde eski YouTube URL çözümlemesi geçersiz sayılır
        private long _playToken;

        // Sistem tepsisi: müzik çalarken pencere kapatılırsa uygulama
        // kapanmaz, tepsiye küçülür ve çalmaya devam eder
        private WinForms.NotifyIcon? _tray;
        private bool _reallyExit;
        private bool _balloonShown;

        private readonly UpdateService _updater = new();
        private bool _updateInProgress;

        public MainWindow()
        {
            InitializeComponent();

            InitTray();

            Closing += MainWindow_Closing;

            BindActivePlaylist();

            ApplySettings();

            // Kayıtlı oturum varsa kimlik doğrulamalı istemciyle başla
            if (_auth.IsLoggedIn)
            {
                _youtube.SetAuthCookies(_auth.Cookies);
                UpdateAccountButton();
            }

            _player.PlaybackEnded += (_, _) =>
                Dispatcher.BeginInvoke(new Action(OnPlaybackEnded));

            _player.EncounteredError += (_, _) =>
                Dispatcher.BeginInvoke(new Action(() => SetStatus("Çalma hatası oluştu.")));

            sliderPosition.PreviewMouseDown += (_, _) => _draggingSlider = true;

            sliderPosition.PreviewMouseUp += (_, _) =>
            {
                _draggingSlider = false;
                _player.Position = (float)(sliderPosition.Value / 100.0);
            };

            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Açılıştan kısa süre sonra arka planda güncelleme denetle
            Loaded += async (_, _) =>
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));
                await CheckForUpdatesAsync(silent: true);
            };
        }

        // ═══════════════ Güncelleme ═══════════════

        private async System.Threading.Tasks.Task CheckForUpdatesAsync(bool silent)
        {
            if (_updateInProgress)
                return;

            if (!UpdateService.IsConfigured)
            {
                if (!silent)
                    SetStatus("Güncelleme kaynağı henüz yapılandırılmamış.");
                return;
            }

            try
            {
                var update = await _updater.CheckAsync();

                if (update == null)
                {
                    if (!silent)
                        SetStatus($"Uygulama güncel (v{UpdateService.CurrentVersion.ToString(3)}).");
                    return;
                }

                ShowFromTray();

                var answer = MessageBox.Show(this,
                    $"Yeni sürüm hazır: {update.TagName} " +
                    $"(şu anki sürüm: v{UpdateService.CurrentVersion.ToString(3)})\n\n" +
                    "Şimdi indirilip kurulsun mu?\n" +
                    "Kurulum sırasında uygulama kapanır ve otomatik olarak yeniden açılır.",
                    "LiwaPlayer Güncelleme",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer != MessageBoxResult.Yes)
                    return;

                _updateInProgress = true;

                var file = await _updater.DownloadAsync(update,
                    new Progress<int>(p => SetStatus($"Güncelleme indiriliyor... %{p}")));

                SetStatus("Kurulum başlatılıyor...");

                _updater.ApplyUpdate(file);

                ExitApplication();
            }
            catch (Exception ex)
            {
                LogService.Write("Güncelleme denetimi", ex);

                _updateInProgress = false;

                if (!silent)
                    SetStatus("Güncelleme denetlenemedi: " + ex.Message);
            }
        }

        private void ApplySettings()
        {
            var s = _settings.Current;

            sliderVolume.Value = Math.Clamp(s.Volume, 0, 100);
            btnShuffle.IsChecked = s.Shuffle;
            btnRepeat.IsChecked = s.Repeat;

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
                SetStatus($"Arayüz boyutu: %{Math.Round(scale * 100)}");
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

            var menu = new WinForms.ContextMenuStrip();

            menu.Items.Add("Göster", null, (_, _) => ShowFromTray());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Oynat / Duraklat", null, (_, _) => TogglePlayPause());
            menu.Items.Add("Önceki", null, (_, _) =>
                PlaySong(_playlist.Previous(CurrentPlaybackPlaylist), CurrentPlaybackPlaylist));
            menu.Items.Add("Sonraki", null, (_, _) =>
                PlaySong(GetNextSong(), CurrentPlaybackPlaylist));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Güncellemeleri Denetle", null,
                async (_, _) => await CheckForUpdatesAsync(silent: false));
            menu.Items.Add("Çıkış", null, (_, _) => ExitApplication());

            _tray.ContextMenuStrip = menu;
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
                _playbackPlaylist = null;

            BindActivePlaylist();
        }

        // ═══════════════ Zamanlayıcı / UI güncelleme ═══════════════

        private void Timer_Tick(object? sender, EventArgs e)
        {
            btnPlayPause.Content = _player.IsPlaying ? "" : "";

            if (_player.Duration <= 0)
                return;

            txtCurrent.Text = _player.FormatTime(_player.CurrentTime);
            txtDuration.Text = _player.FormatTime(_player.Duration);

            if (!_draggingSlider)
                sliderPosition.Value = _player.Position * 100;
        }

        private void SetStatus(string message) => txtStatus.Text = message;

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

            if (_boundSongs != null && _boundSongs.Contains(song))
            {
                lstPlaylist.SelectedItem = song;
                lstPlaylist.ScrollIntoView(song);
            }

            txtSong.Text = FormatSongLabel(song);
            Title = "LiwaPlayer — " + song.Title;

            if (_tray != null)
            {
                // NotifyIcon.Text en fazla 63 karakter kabul eder
                var trayText = "LiwaPlayer — " + song.Title;
                _tray.Text = trayText.Length > 63 ? trayText[..60] + "..." : trayText;
            }

            try
            {
                string location;

                if (song.Source == SongSource.YouTube)
                {
                    SetStatus("YouTube akışı hazırlanıyor...");
                    location = await _youtube.GetAudioStreamUrlAsync(song.FileName);

                    // Bekleme sırasında başka şarkı seçildiyse bunu çalma
                    if (token != _playToken)
                        return;
                }
                else
                {
                    if (!File.Exists(song.FileName))
                    {
                        SetStatus("Dosya bulunamadı: " + Path.GetFileName(song.FileName));
                        return;
                    }

                    location = song.FileName;
                }

                _player.Play(location);

                SetStatus(song.Source == SongSource.YouTube
                    ? "YouTube üzerinden çalınıyor"
                    : "Yerel dosyadan çalınıyor");

                _settings.Current.LastSong = song.Id.ToString();
            }
            catch (Exception ex)
            {
                LogService.Write($"Çalma hatası ({song.Title})", ex);

                if (token != _playToken)
                    return;

                // YouTube bazı makinelere/IP'lere bot doğrulaması dayatır ve tüm
                // videolar için "is not available" döndürür; hesap girişi bunu aşar
                if (ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus(_auth.IsLoggedIn
                        ? "Video açılamadı. Oturum eskimiş olabilir: kişi simgesinden çıkıp yeniden giriş yapın."
                        : "YouTube bu makinede doğrulama istiyor: kişi simgesinden hesabınla giriş yapıp tekrar deneyin.");
                }
                else
                {
                    SetStatus("Çalınamadı: " + ex.Message);
                }
            }
        }

        private Playlist CurrentPlaybackPlaylist =>
            _playbackPlaylist != null && _playlist.Playlists.Contains(_playbackPlaylist)
                ? _playbackPlaylist
                : _playlist.ActivePlaylist;

        private Song? GetNextSong()
        {
            var playlist = CurrentPlaybackPlaylist;
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

        private void OnPlaybackEnded()
        {
            if (_playlist.CurrentSong == null)
                return;

            var playlist = CurrentPlaybackPlaylist;
            var songs = playlist.Songs;

            if (songs.Count == 0)
                return;

            bool shuffle = btnShuffle.IsChecked == true;
            bool repeat = btnRepeat.IsChecked == true;
            bool isLast = songs.IndexOf(_playlist.CurrentSong) == songs.Count - 1;

            if (!shuffle && !repeat && isLast)
            {
                SetStatus("Liste bitti.");
                sliderPosition.Value = 0;
                return;
            }

            PlaySong(GetNextSong(), playlist);
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

        private void btnPrevious_Click(object sender, RoutedEventArgs e) =>
            PlaySong(_playlist.Previous(CurrentPlaybackPlaylist), CurrentPlaybackPlaylist);

        private void sliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player == null)
                return;

            int volume = (int)e.NewValue;

            _player.Volume = volume;

            if (txtVolume != null)
                txtVolume.Text = "%" + volume;
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
                SetStatus("Seçilen şarkılar zaten listede.");
            else if (birthdayCount > 0)
                SetStatus($"{addedCount} şarkı eklendi ({birthdayCount} tanesi 🎂 doğum günü listesine).");
            else
                SetStatus($"{addedCount} şarkı eklendi.");
        }

        private void btnSpotify_Click(object sender, RoutedEventArgs e)
        {
            var import = new SpotifyImportWindow(_playlist, _youtube) { Owner = this };

            import.ShowDialog();

            if (import.ImportedPlaylist != null)
            {
                BindActivePlaylist();
                SetStatus($"\"{import.ImportedPlaylist.Name}\" listesi Spotify'dan aktarıldı.");
            }
        }

        private void btnYouTubeImport_Click(object sender, RoutedEventArgs e)
        {
            var import = new YouTubeImportWindow(_playlist, _youtube) { Owner = this };

            import.ShowDialog();

            if (import.ImportedPlaylist != null)
            {
                BindActivePlaylist();
                SetStatus($"\"{import.ImportedPlaylist.Name}\" listesi YouTube'dan aktarıldı.");
            }
        }

        private void btnMoveSong_Click(object sender, RoutedEventArgs e)
        {
            if (lstPlaylist.SelectedItem is not Song song)
            {
                SetStatus("Taşımak için önce listeden bir şarkı seçin.");
                return;
            }

            var targets = _playlist.Playlists
                .Where(p => p != _playlist.ActivePlaylist)
                .ToList();

            if (targets.Count == 0)
            {
                SetStatus("Başka liste yok. Önce kütüphane butonundan yeni bir liste oluşturun.");
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
                SetStatus($"\"{song.Title}\" zaten \"{target.Name}\" listesinde var.");
                return;
            }

            SetStatus($"\"{song.Title}\" → \"{target.Name}\" listesine taşındı.");
        }

        private void btnDelete_Click(object sender, RoutedEventArgs e) => RemoveSelectedSong();

        private void RemoveSelectedSong()
        {
            if (lstPlaylist.SelectedItem is not Song song)
            {
                SetStatus("Silmek için önce listeden şarkı seçin.");
                return;
            }

            bool wasCurrent = _playlist.CurrentSong == song;

            _playlist.RemoveFromActive(song);

            if (wasCurrent)
            {
                _player.Stop();
                txtSong.Text = "Şarkı seçilmedi";
                sliderPosition.Value = 0;
            }

            SetStatus("Şarkı listeden çıkarıldı.");
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
            {
                _player.Stop();
                txtSong.Text = "Şarkı seçilmedi";
                sliderPosition.Value = 0;
            }

            SetStatus($"\"{active.Name}\" listesi temizlendi.");
        }

        // ═══════════════ YouTube hesap girişi ═══════════════

        private void UpdateAccountButton()
        {
            if (_auth.IsLoggedIn)
            {
                btnAccount.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
                btnAccount.ToolTip = "YouTube oturumu açık — çıkış yapmak için tıkla";
            }
            else
            {
                btnAccount.ClearValue(ForegroundProperty);
                btnAccount.ToolTip = "YouTube hesabıyla giriş yap (Premium: reklamsız + kendi listelerin)";
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
                SetStatus("YouTube oturumu kapatıldı.");
                return;
            }

            var login = new LoginWindow { Owner = this };

            if (login.ShowDialog() == true && login.ResultCookies is { Count: > 0 })
            {
                _auth.SaveCookies(login.ResultCookies);
                _youtube.SetAuthCookies(_auth.Cookies);

                UpdateAccountButton();
                SetStatus("YouTube girişi yapıldı. Premium hesapta akışlar reklamsızdır.");
            }
        }

        // ═══════════════ YouTube arama ═══════════════

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SearchYouTubeAsync();
        }

        private void btnSearch_Click(object sender, RoutedEventArgs e) => SearchYouTubeAsync();

        private async void SearchYouTubeAsync()
        {
            var query = txtSearch.Text.Trim();

            if (query.Length == 0)
                return;

            // Arama kutusuna liste bağlantısı yapıştırıldıysa içe aktar
            if (query.Contains("list=", StringComparison.OrdinalIgnoreCase))
            {
                await ImportYouTubePlaylistAsync(query);
                return;
            }

            btnSearch.IsEnabled = false;
            lstResults.ItemsSource = null;
            txtSearchHint.Text = "Aranıyor...";
            txtSearchHint.Visibility = Visibility.Visible;

            try
            {
                var results = rbSearchMusic.IsChecked == true
                    ? await _youtube.SearchMusicAsync(query)
                    : await _youtube.SearchAsync(query);

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
                        Source = SongSource.YouTube
                    };

                    _playlist.Add(song, target, out _);
                }

                _playlist.SetActive(target);
                BindActivePlaylist();

                txtSearch.Clear();
                txtSearchHint.Text = "Şarkı veya sanatçı adı yazıp Enter'a basın.";
                txtSearchHint.Visibility = Visibility.Visible;

                SetStatus($"\"{finalName}\" içe aktarıldı ({videos.Count} şarkı).");
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
                Source = SongSource.YouTube
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
                SetStatus($"Bu şarkı \"{target.Name}\" listesinde zaten var.");
            else if (target != _playlist.ActivePlaylist)
                SetStatus($"🎂 Doğum günü şarkısı algılandı, \"{target.Name}\" listesine eklendi.");
            else
                SetStatus($"\"{target.Name}\" listesine eklendi.");
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
            s.Repeat = btnRepeat.IsChecked == true;
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

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            SaveSettings();
            _playlist.Save();
            _player.Dispose();

            base.OnClosed(e);
        }
    }
}
