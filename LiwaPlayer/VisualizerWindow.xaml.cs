using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace LiwaPlayer
{
    // Görselleştirici: üç ışık stili + klip modu. Kontroller alttaki çubukta
    // (Stil / Klip / Tam Ekran / Kapat) — video yerel pencerede çizildiği için
    // videonun üzerine tıklama güvenilir değildir; butonlar videonun dışındadır.
    // Işık stilleri gerçek spektrum analizi yapmaz (POS işlemcisini yormamak için).
    public partial class VisualizerWindow : Window
    {
        private const int BarCount = 48;

        private readonly Func<bool> _isPlaying;
        private readonly LibVLCSharp.Shared.MediaPlayer _mediaPlayer;

        // Klip moduna geçiş: ana pencere akışı video+ses olarak yeniden başlatır.
        // false dönerse şarkının videosu yok demektir (yerel dosya vb.)
        private readonly Func<Task<bool>> _switchToVideo;
        private readonly Func<Task> _switchToAudio;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly Random _random = new();

        private readonly Rectangle[] _bars = new Rectangle[BarCount];
        private readonly Rectangle[] _mirrorBars = new Rectangle[BarCount];
        private readonly double[] _values = new double[BarCount];
        private readonly double[] _targets = new double[BarCount];

        // 0 = klip (video), 1 = alt çubuklar, 2 = ayna, 3 = halka
        private int _mode = 1;
        private bool _videoActive;
        private bool _switching;

        // Son seçilen stil oturum boyunca hatırlanır
        private static int _lastMode = 1;

        // Video yüzeyi her klip girişinde sıfırdan oluşturulur, çıkışta yok
        // edilir; kalıcı olursa WPF o bölgeyi yeniden çizemiyor (kazı kazan izi)
        private LibVLCSharp.WPF.VideoView? _videoView;

        // Tek tık (stil değiştir) ile çift tık (tam ekran) ayrımı
        private readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
        private bool _suppressNextClick;

        private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
        private string _barText = "";

        private WindowState _restoreState;
        private WindowStyle _restoreStyle;
        private bool _fullscreen;

        // Ana pencere şarkı değişiminde video modunu sürdürmek için okur
        public bool IsVideoActive => _videoActive;

        public VisualizerWindow(
            Func<bool> isPlaying,
            LibVLCSharp.Shared.MediaPlayer mediaPlayer,
            Func<Task<bool>> switchToVideo,
            Func<Task> switchToAudio)
        {
            InitializeComponent();

            UiScaleHelper.Apply(this);

            _isPlaying = isPlaying;
            _mediaPlayer = mediaPlayer;
            _switchToVideo = switchToVideo;
            _switchToAudio = switchToAudio;

            var accent = (SolidColorBrush)FindResource("AccentBrush");

            for (int i = 0; i < BarCount; i++)
            {
                _bars[i] = MakeBar(accent.Color, 0.95);
                _mirrorBars[i] = MakeBar(accent.Color, 0.45);

                canvas.Children.Add(_bars[i]);
                canvas.Children.Add(_mirrorBars[i]);
            }

            _timer.Tick += Timer_Tick;
            _timer.Start();

            _clickTimer.Tick += (_, _) =>
            {
                _clickTimer.Stop();
                CycleLightStyle();
            };

            _hintTimer.Tick += (_, _) =>
            {
                _hintTimer.Stop();
                txtBarTitle.Text = _barText;
            };

            // Son seçilen stille açılır (klipte bırakıldıysa klipten devam)
            Loaded += async (_, _) =>
            {
                if (_lastMode == 0)
                    await EnterVideoModeAsync();
                else
                    _mode = _lastMode;
            };

            Closed += (_, _) =>
            {
                _timer.Stop();
                _clickTimer.Stop();
                _hintTimer.Stop();
            };
        }

        private Rectangle MakeBar(Color color, double opacity) => new()
        {
            RadiusX = 2,
            RadiusY = 2,
            Opacity = opacity,
            Fill = new LinearGradientBrush(
                Color.FromArgb(255, color.R, color.G, color.B),
                Color.FromArgb(60, color.R, color.G, color.B),
                90)
        };

        public void UpdateSong(string title, string artist, string coverUrl)
        {
            txtVisTitle.Text = title;
            txtVisArtist.Text = artist;

            _barText = string.IsNullOrWhiteSpace(artist) ? title : $"{title} — {artist}";
            txtBarTitle.Text = _barText;

            try
            {
                if (string.IsNullOrWhiteSpace(coverUrl))
                {
                    brushArt.ImageSource = null;
                    return;
                }

                // 220px'lik kutu için sınırlı çözünürlük (bellek tasarrufu)
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(coverUrl);
                image.DecodePixelWidth = 440;
                image.EndInit();

                brushArt.ImageSource = image;
            }
            catch
            {
                brushArt.ImageSource = null;
            }
        }

        private void ShowBarHint(string text)
        {
            txtBarTitle.Text = text;
            _hintTimer.Stop();
            _hintTimer.Start();
        }

        // ═══════════ Klip modu ═══════════

        private async Task EnterVideoModeAsync()
        {
            if (_switching)
                return;

            _switching = true;
            btnClip.IsEnabled = false;

            try
            {
                // Video yüzeyini sıfırdan oluştur (HWND için önce görünür olmalı)
                _videoView = new LibVLCSharp.WPF.VideoView();

                videoHost.Content = _videoView;
                videoHost.Visibility = Visibility.Visible;
                UpdateLayout();

                _videoView.MediaPlayer = _mediaPlayer;

                bool ok = await _switchToVideo();

                if (ok)
                {
                    _mode = 0;
                    _lastMode = 0;
                    _videoActive = true;

                    canvas.Visibility = Visibility.Collapsed;
                    infoPanel.Visibility = Visibility.Collapsed;
                    btnClip.Content = "Klibi Kapat";
                }
                else
                {
                    DestroyVideoSurface();
                    ShowBarHint("Bu şarkının klibi yok.");
                }
            }
            catch
            {
                DestroyVideoSurface();
            }
            finally
            {
                _switching = false;
                btnClip.IsEnabled = true;
            }
        }

        private async Task LeaveVideoModeAsync()
        {
            if (_switching)
                return;

            _switching = true;
            btnClip.IsEnabled = false;

            try
            {
                DestroyVideoSurface();

                _mode = _lastMode = 1;
                _videoActive = false;

                await _switchToAudio();
            }
            catch
            {
            }
            finally
            {
                _switching = false;
                btnClip.IsEnabled = true;
                Render();
            }
        }

        private void DestroyVideoSurface()
        {
            _videoActive = false;

            if (_videoView != null)
            {
                try
                {
                    _videoView.MediaPlayer = null;
                    _videoView.Dispose();
                }
                catch
                {
                }

                _videoView = null;
            }

            videoHost.Content = null;
            videoHost.Visibility = Visibility.Collapsed;

            canvas.Visibility = Visibility.Visible;
            infoPanel.Visibility = Visibility.Visible;
            btnClip.Content = "Klip";

            // Video bölgesinin kalıntısız yeniden çizilmesini garanti et
            InvalidateVisual();
            UpdateLayout();
        }

        // ═══════════ Animasyon ═══════════

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_videoActive)
                return;

            bool playing = _isPlaying();

            for (int i = 0; i < BarCount; i++)
            {
                if (Math.Abs(_values[i] - _targets[i]) < 0.03)
                {
                    _targets[i] = playing
                        ? Math.Clamp(
                            0.15 + _random.NextDouble() * 0.85 * BandWeight(i), 0.05, 1.0)
                        : 0.02;
                }

                double speed = playing ? 0.18 : 0.08;

                _values[i] += (_targets[i] - _values[i]) * speed;
            }

            Render();
        }

        // Basların (kenar bantların) daha yüksek oynaması doğal durur
        private static double BandWeight(int i)
        {
            double x = (double)i / BarCount;
            return 0.55 + 0.45 * Math.Cos(x * Math.PI * 2);
        }

        private void Render()
        {
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;

            if (w < 50 || h < 50)
                return;

            switch (_mode)
            {
                case 1: RenderBottomBars(w, h); break;
                case 2: RenderMirror(w, h); break;
                case 3: RenderRing(w, h); break;
            }
        }

        private void RenderBottomBars(double w, double h)
        {
            double barWidth = w / BarCount * 0.7;
            double gap = w / BarCount;
            double maxHeight = h * 0.38;

            for (int i = 0; i < BarCount; i++)
            {
                double v = _values[i] * maxHeight + 2;

                Place(_bars[i], gap * i + (gap - barWidth) / 2, h - v, barWidth, v);
                _mirrorBars[i].Visibility = Visibility.Collapsed;
            }
        }

        private void RenderMirror(double w, double h)
        {
            double barWidth = w / BarCount * 0.7;
            double gap = w / BarCount;
            double maxHeight = h * 0.30;
            double center = h * 0.82;

            for (int i = 0; i < BarCount; i++)
            {
                double v = _values[i] * maxHeight + 2;
                double x = gap * i + (gap - barWidth) / 2;

                Place(_bars[i], x, center - v, barWidth, v);

                _mirrorBars[i].Visibility = Visibility.Visible;
                Place(_mirrorBars[i], x, center + 2, barWidth, v * 0.5);
            }
        }

        private void RenderRing(double w, double h)
        {
            double cx = w / 2;
            double cy = h / 2;

            // Kapak kutusunun (220px, ölçek dahil) hemen dışından başlasın
            double radius = 130 * UiScaleHelper.Current;
            double maxLen = Math.Min(w, h) / 2 - radius - 10;

            if (maxLen < 20)
                maxLen = 20;

            double barWidth = 2 * Math.PI * radius / BarCount * 0.55;

            for (int i = 0; i < BarCount; i++)
            {
                double v = _values[i] * maxLen + 3;
                double angle = 360.0 / BarCount * i;

                var bar = _bars[i];

                bar.Width = barWidth;
                bar.Height = v;

                Canvas.SetLeft(bar, cx - barWidth / 2);
                Canvas.SetTop(bar, cy - radius - v);

                bar.RenderTransformOrigin = new Point(0.5, (radius + v) / v);
                bar.RenderTransform = new RotateTransform(angle);

                _mirrorBars[i].Visibility = Visibility.Collapsed;
            }
        }

        private static void Place(Rectangle bar, double x, double y, double width, double height)
        {
            bar.Width = width;
            bar.Height = height;
            bar.RenderTransform = null;

            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
        }

        // ═══════════ Etkileşim ═══════════

        // Işık stilleri arasında döner (1 → 2 → 3 → 1); klip yalnızca butonla açılır
        private void CycleLightStyle()
        {
            if (_videoActive || _switching)
                return;

            _mode = _mode >= 3 ? 1 : _mode + 1;
            _lastMode = _mode;

            Render();
        }

        private void stage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_suppressNextClick)
            {
                _suppressNextClick = false;
                return;
            }

            _clickTimer.Stop();
            _clickTimer.Start();
        }

        private void stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                _clickTimer.Stop();
                _suppressNextClick = true;
                ToggleFullscreen();
            }
        }

        private void btnStyle_Click(object sender, RoutedEventArgs e)
        {
            if (_videoActive)
            {
                // Klipteyken Stil'e basılırsa klipten çıkıp ışık stiline dön
                _ = LeaveVideoModeAsync();
                return;
            }

            CycleLightStyle();
        }

        private async void btnClip_Click(object sender, RoutedEventArgs e)
        {
            if (_videoActive)
                await LeaveVideoModeAsync();
            else
                await EnterVideoModeAsync();
        }

        private void btnFull_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

        private void btnCloseVis_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape when _fullscreen:
                    ToggleFullscreen();
                    break;

                case Key.Escape:
                    Close();
                    break;

                case Key.F11:
                    ToggleFullscreen();
                    break;
            }
        }

        private void ToggleFullscreen()
        {
            if (!_fullscreen)
            {
                _restoreState = WindowState;
                _restoreStyle = WindowStyle;

                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                _fullscreen = true;
            }
            else
            {
                WindowStyle = _restoreStyle;
                WindowState = _restoreState;
                _fullscreen = false;
            }

            Activate();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

        protected override async void OnClosed(EventArgs e)
        {
            // Pencere kapanırken video moddaysak sese geri dön (CPU tasarrufu)
            try
            {
                bool wasVideo = _videoActive;

                DestroyVideoSurface();

                if (wasVideo)
                    await _switchToAudio();
            }
            catch
            {
            }

            base.OnClosed(e);
        }
    }
}
