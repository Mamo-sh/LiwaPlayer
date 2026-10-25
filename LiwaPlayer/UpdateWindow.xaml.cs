using LiwaPlayer.Services;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace LiwaPlayer
{
    // Güncelleme penceresi: önerir → indirir (yüzde + çizgi) → kurulum başlarken
    // bilgi verir. Uygulama kapanmadan önce kullanıcı ne olduğunu görsün diye
    // ana pencere değil, bu pencere ilerlemeyi gösterir.
    public partial class UpdateWindow : Window
    {
        private enum Stage { Offer, Downloading, Installing, Failed }

        private readonly UpdateService _updater;
        private readonly UpdateInfo _update;

        private CancellationTokenSource? _cts;
        private Stage _stage = Stage.Offer;

        // Pencere kapanırken (X) iptal sonrası DialogResult atanmaya çalışılmasın
        private bool _closing;

        // İndirme başarıyla bittiyse kurulum dosyasının yolu (DialogResult = true)
        public string? DownloadedFile { get; private set; }

        public UpdateWindow(UpdateService updater, UpdateInfo update)
        {
            InitializeComponent();

            UiScaleHelper.Apply(this);

            _updater = updater;
            _update = update;

            ShowOffer();
        }

        // ═══════════ Aşamalar ═══════════

        private void ShowOffer()
        {
            _stage = Stage.Offer;

            txtTitle.Text = "Yeni sürüm hazır";
            txtSub.Text = $"v{UpdateService.CurrentVersion.ToString(3)}  →  {_update.TagName}";

            panelProgress.Visibility = Visibility.Collapsed;

            btnPrimary.Content = "Şimdi güncelle";
            btnPrimary.Visibility = Visibility.Visible;
            btnSecondary.Content = "Daha sonra";
            btnSecondary.IsCancel = true;
            btnSecondary.Visibility = Visibility.Visible;

            panelButtons.Visibility = Visibility.Visible;

            StopSpinning();
        }

        private async Task StartDownloadAsync()
        {
            _stage = Stage.Downloading;
            _cts = new CancellationTokenSource();

            txtTitle.Text = "Güncelleme indiriliyor";
            txtSub.Text = $"{_update.TagName} sürümü indiriliyor, lütfen bekleyin…";

            bar.Value = 0;
            txtPercent.Text = "%0";
            txtBytes.Text = "";
            panelProgress.Visibility = Visibility.Visible;

            btnPrimary.Visibility = Visibility.Collapsed;
            btnSecondary.Content = "İptal";
            btnSecondary.IsCancel = false;
            btnSecondary.Visibility = Visibility.Visible;
            panelButtons.Visibility = Visibility.Visible;

            StartSpinning();

            var progress = new Progress<(long Done, long Total)>(p =>
            {
                int percent = p.Total > 0 ? (int)(p.Done * 100 / p.Total) : 0;

                bar.Value = percent;
                txtPercent.Text = $"%{percent}";
                txtBytes.Text = p.Total > 0
                    ? $"{FormatMb(p.Done)} / {FormatMb(p.Total)}"
                    : FormatMb(p.Done);
            });

            string file;

            try
            {
                file = await _updater.DownloadAsync(_update, progress, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Kullanıcı "İptal"e bastı; pencere X ile kapatılıyorsa zaten kapanıyor
                if (!_closing)
                    DialogResult = false;

                return;
            }
            catch (Exception ex)
            {
                LogService.Write("Güncelleme indirme", ex);
                ShowFailure(ex);
                return;
            }

            DownloadedFile = file;

            _stage = Stage.Installing;

            bar.Value = 100;
            txtPercent.Text = "%100";

            txtTitle.Text = "Kurulum başlatılıyor";
            txtSub.Text = "Uygulama kapanıp güncellenecek ve kendiliğinden yeniden açılacak.";

            panelButtons.Visibility = Visibility.Collapsed;

            // Kullanıcı son durumu görebilsin; sonra ana pencere kurulumu başlatıp kapanır
            await Task.Delay(2200);

            DialogResult = true;
        }

        private void ShowFailure(Exception ex)
        {
            _stage = Stage.Failed;

            StopSpinning();

            txtTitle.Text = "İndirme tamamlanamadı";
            txtSub.Text = ex is HttpRequestException or IOException
                ? "İnternet bağlantısı kesilmiş olabilir. Bağlantını kontrol edip tekrar dene."
                : ex.Message;

            panelProgress.Visibility = Visibility.Collapsed;

            btnPrimary.Content = "Tekrar dene";
            btnPrimary.Visibility = Visibility.Visible;
            btnSecondary.Content = "Kapat";
            btnSecondary.IsCancel = true;
            btnSecondary.Visibility = Visibility.Visible;
            panelButtons.Visibility = Visibility.Visible;
        }

        // ═══════════ Düğmeler ═══════════

        private async void btnPrimary_Click(object sender, RoutedEventArgs e)
        {
            if (_stage is Stage.Offer or Stage.Failed)
                await StartDownloadAsync();
        }

        private void btnSecondary_Click(object sender, RoutedEventArgs e)
        {
            if (_stage == Stage.Downloading)
            {
                _cts?.Cancel();
                return;
            }

            DialogResult = false;
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            // Kurulum başlarken pencere kapatılamaz; indirirken kapatmak indirmeyi iptal eder
            if (_stage == Stage.Installing && DialogResult != true)
            {
                e.Cancel = true;
                return;
            }

            _closing = true;

            if (_stage == Stage.Downloading)
                _cts?.Cancel();
        }

        // ═══════════ Yardımcılar ═══════════

        private static string FormatMb(long bytes) =>
            (bytes / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

        private void StartSpinning()
        {
            var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.4))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            iconRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, animation);
        }

        private void StopSpinning()
        {
            iconRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            iconRotate.Angle = 0;
        }
    }
}
