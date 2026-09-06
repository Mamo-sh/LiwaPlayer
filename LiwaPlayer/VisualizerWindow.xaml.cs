using System;
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
    // Müzikle uyumlu ritim hissi veren hafif görselleştirici. Gerçek spektrum
    // analizi yapılmaz (POS işlemcisini yormamak için); çubuklar müzik çalarken
    // yumuşatılmış rastgele tepe değerleriyle canlandırılır, duraklayınca söner.
    public partial class VisualizerWindow : Window
    {
        private const int BarCount = 48;

        private readonly Func<bool> _isPlaying;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly Random _random = new();

        private readonly Rectangle[] _bars = new Rectangle[BarCount];
        private readonly Rectangle[] _mirrorBars = new Rectangle[BarCount];
        private readonly double[] _values = new double[BarCount];
        private readonly double[] _targets = new double[BarCount];

        // 0 = alt çubuklar, 1 = ayna (ortadan), 2 = kapak etrafında halka
        private int _mode;

        private WindowState _restoreState;
        private WindowStyle _restoreStyle;
        private bool _fullscreen;

        public VisualizerWindow(Func<bool> isPlaying)
        {
            InitializeComponent();

            UiScaleHelper.Apply(this);

            _isPlaying = isPlaying;

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

            Closed += (_, _) => _timer.Stop();
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

            try
            {
                brushArt.ImageSource = string.IsNullOrWhiteSpace(coverUrl)
                    ? null
                    : new BitmapImage(new Uri(coverUrl));
            }
            catch
            {
                brushArt.ImageSource = null;
            }
        }

        // ═══════════ Animasyon ═══════════

        private void Timer_Tick(object? sender, EventArgs e)
        {
            bool playing = _isPlaying();

            for (int i = 0; i < BarCount; i++)
            {
                // Hedefe yaklaşınca yeni rastgele tepe seç; ritim hissi için
                // komşu çubuklarla hafif ilişkilendir
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
                case 0: RenderBottomBars(w, h); break;
                case 1: RenderMirror(w, h); break;
                default: RenderRing(w, h); break;
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

                Place(_bars[i], gap * i + (gap - barWidth) / 2, h - v, barWidth, v, 0);
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

                Place(_bars[i], x, center - v, barWidth, v, 0);

                _mirrorBars[i].Visibility = Visibility.Visible;
                Place(_mirrorBars[i], x, center + 2, barWidth, v * 0.5, 0);
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

        private static void Place(Rectangle bar, double x, double y, double width, double height, double angle)
        {
            bar.Width = width;
            bar.Height = height;
            bar.RenderTransform = null;

            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
        }

        // ═══════════ Etkileşim ═══════════

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _mode = (_mode + 1) % 3;

            // Halka modunda bilgi paneli ortada kalır; diğerlerinde de ortada iyi durur
            Render();
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ToggleFullscreen();

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
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => Render();
    }
}
