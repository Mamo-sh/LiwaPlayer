using LiwaPlayer.Models;
using LiwaPlayer.Services;
using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LiwaPlayer
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settings;

        // Pencere ilk doldurulurken TextChanged/Checked olayları işlenmesin
        private readonly bool _initialized;

        public SettingsWindow(SettingsService settings)
        {
            InitializeComponent();

            UiScaleHelper.Apply(this);

            _settings = settings;

            var s = _settings.Current;

            (s.Theme switch
            {
                ThemeMode.Light => rbThemeLight,
                ThemeMode.Dark => rbThemeDark,
                _ => rbThemeAuto
            }).IsChecked = true;

            txtLightStart.Text = FormatTime(s.AutoLightStartMinutes);
            txtDarkStart.Text = FormatTime(s.AutoDarkStartMinutes);

            chkThumbnails.IsChecked = s.ShowThumbnails;
            chkDisableVisualizer.IsChecked = s.DisableVisualizer;
            chkSoftwareRender.IsChecked = s.SoftwareRendering;
            chkAutoContinue.IsChecked = s.AutoContinue;
            chkUpdates.IsChecked = s.CheckUpdatesOnStartup;

            chkConnectionCheck.IsChecked = s.ConnectionCheckEnabled;

            (s.ConnectionCheckSeconds switch
            {
                <= 45 => rbCheck30,
                <= 90 => rbCheck60,
                <= 200 => rbCheck120,
                _ => rbCheck300
            }).IsChecked = true;

            panelConnectionInterval.IsEnabled = s.ConnectionCheckEnabled;

            (s.SearchResultCount switch
            {
                <= 10 => rbResults10,
                >= 30 => rbResults30,
                _ => rbResults20
            }).IsChecked = true;

            (s.NetworkCachingMs switch
            {
                <= 1000 => rbCacheLow,
                >= 4000 => rbCacheHigh,
                _ => rbCacheNormal
            }).IsChecked = true;

            txtVersion.Text = $"LiwaPlayer v{UpdateService.CurrentVersion.ToString(3)}";

            _initialized = true;
        }

        // ═══════════ Tema ═══════════

        private ThemeMode SelectedTheme =>
            rbThemeLight.IsChecked == true ? ThemeMode.Light :
            rbThemeDark.IsChecked == true ? ThemeMode.Dark : ThemeMode.Auto;

        private static string FormatTime(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

        // "8:00", "08:00" ve "08.00" kabul edilir
        private static bool TryParseTime(string text, out int minutes)
        {
            minutes = 0;

            var m = Regex.Match(text.Trim(), @"^(\d{1,2})[:.](\d{2})$");

            if (!m.Success)
                return false;

            int hour = int.Parse(m.Groups[1].Value);
            int minute = int.Parse(m.Groups[2].Value);

            if (hour > 23 || minute > 59)
                return false;

            minutes = hour * 60 + minute;
            return true;
        }

        // Geçerli bir aralık girildiyse true; hatayı ipucu satırında gösterir
        private bool TryReadSchedule(out int light, out int dark)
        {
            light = dark = 0;

            string? error = null;

            if (!TryParseTime(txtLightStart.Text, out light) ||
                !TryParseTime(txtDarkStart.Text, out dark))
                error = "Saat biçimi geçersiz. SS:DD şeklinde yazın (ör. 08:00).";
            else if (light == dark)
                error = "Açık ve koyu tema başlangıcı aynı saat olamaz.";

            if (error != null)
            {
                txtThemeScheduleHint.Text = error;
                txtThemeScheduleHint.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE5, 0x48, 0x4D));
                return false;
            }

            txtThemeScheduleHint.Text =
                "Saat biçimi: SS:DD (ör. 08:00 ve 20:00). Gece yarısını aşan aralıklar da olur.";
            txtThemeScheduleHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            return true;
        }

        // Tema seçimi anında önizlenir (kaydetmeden de canlı görülür); Vazgeç'e
        // basılırsa OnClosed'da eski ayara geri dönülür
        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!_initialized)
            {
                // Pencere dolarken (IsChecked atanırken) önizleme yapılmaz
                return;
            }

            PreviewTheme();
        }

        private void ThemeTime_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialized)
                return;

            PreviewTheme();
        }

        private void PreviewTheme()
        {
            if (TryReadSchedule(out int light, out int dark))
                ThemeService.SetSchedule(light, dark);

            ThemeService.Apply(SelectedTheme);
        }

        // ═══════════ Bağlantı ═══════════

        private void ConnectionCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized)
                return;

            panelConnectionInterval.IsEnabled = chkConnectionCheck.IsChecked == true;
        }

        // ═══════════ Kapanış ═══════════

        protected override void OnClosed(EventArgs e)
        {
            // Vazgeçildiyse veya pencere X ile kapatıldıysa canlı önizlemeyi geri al
            if (DialogResult != true)
            {
                ThemeService.SetSchedule(
                    _settings.Current.AutoLightStartMinutes,
                    _settings.Current.AutoDarkStartMinutes);

                ThemeService.Apply(_settings.Current.Theme);
            }

            base.OnClosed(e);
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSchedule(out int light, out int dark))
            {
                txtLightStart.Focus();
                return;
            }

            var s = _settings.Current;

            bool renderChanged = s.SoftwareRendering != (chkSoftwareRender.IsChecked == true);

            s.Theme = SelectedTheme;
            s.AutoLightStartMinutes = light;
            s.AutoDarkStartMinutes = dark;

            s.ShowThumbnails = chkThumbnails.IsChecked == true;
            s.DisableVisualizer = chkDisableVisualizer.IsChecked == true;
            s.SoftwareRendering = chkSoftwareRender.IsChecked == true;
            s.AutoContinue = chkAutoContinue.IsChecked == true;
            s.CheckUpdatesOnStartup = chkUpdates.IsChecked == true;

            s.ConnectionCheckEnabled = chkConnectionCheck.IsChecked == true;
            s.ConnectionCheckSeconds =
                rbCheck30.IsChecked == true ? 30 :
                rbCheck120.IsChecked == true ? 120 :
                rbCheck300.IsChecked == true ? 300 : 60;

            s.SearchResultCount =
                rbResults10.IsChecked == true ? 10 :
                rbResults30.IsChecked == true ? 30 : 20;

            s.NetworkCachingMs =
                rbCacheLow.IsChecked == true ? 800 :
                rbCacheHigh.IsChecked == true ? 6000 : 1500;

            _settings.Save();

            if (renderChanged)
                MessageBox.Show(this,
                    "Çizim modu değişikliği uygulama yeniden açılınca etkinleşir.",
                    "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
