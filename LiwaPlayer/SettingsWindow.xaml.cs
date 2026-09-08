using LiwaPlayer.Models;
using LiwaPlayer.Services;
using System;
using System.Windows;

namespace LiwaPlayer
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settings;

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

            chkThumbnails.IsChecked = s.ShowThumbnails;
            chkDisableVisualizer.IsChecked = s.DisableVisualizer;
            chkSoftwareRender.IsChecked = s.SoftwareRendering;
            chkAutoContinue.IsChecked = s.AutoContinue;
            chkUpdates.IsChecked = s.CheckUpdatesOnStartup;

            (s.SearchResultCount switch
            {
                <= 10 => rbResults10,
                >= 30 => rbResults30,
                _ => rbResults20
            }).IsChecked = true;

            (s.NetworkCachingMs switch
            {
                <= 1500 => rbCacheLow,
                >= 6000 => rbCacheHigh,
                _ => rbCacheNormal
            }).IsChecked = true;

            txtVersion.Text = $"LiwaPlayer v{UpdateService.CurrentVersion.ToString(3)}";
        }

        // Tema seçimi anında önizlenir (kaydetmeden de canlı görülür); Vazgeç'e
        // basılırsa OnClosed'da eski ayara geri dönülür
        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            var mode =
                rbThemeLight.IsChecked == true ? ThemeMode.Light :
                rbThemeDark.IsChecked == true ? ThemeMode.Dark : ThemeMode.Auto;

            ThemeService.Apply(mode);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Vazgeçildiyse veya pencere X ile kapatıldıysa canlı önizlemeyi geri al
            if (DialogResult != true)
                ThemeService.Apply(_settings.Current.Theme);

            base.OnClosed(e);
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            var s = _settings.Current;

            bool renderChanged = s.SoftwareRendering != (chkSoftwareRender.IsChecked == true);

            s.Theme =
                rbThemeLight.IsChecked == true ? ThemeMode.Light :
                rbThemeDark.IsChecked == true ? ThemeMode.Dark : ThemeMode.Auto;

            s.ShowThumbnails = chkThumbnails.IsChecked == true;
            s.DisableVisualizer = chkDisableVisualizer.IsChecked == true;
            s.SoftwareRendering = chkSoftwareRender.IsChecked == true;
            s.AutoContinue = chkAutoContinue.IsChecked == true;
            s.CheckUpdatesOnStartup = chkUpdates.IsChecked == true;

            s.SearchResultCount =
                rbResults10.IsChecked == true ? 10 :
                rbResults30.IsChecked == true ? 30 : 20;

            s.NetworkCachingMs =
                rbCacheLow.IsChecked == true ? 1000 :
                rbCacheHigh.IsChecked == true ? 8000 : 3000;

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
