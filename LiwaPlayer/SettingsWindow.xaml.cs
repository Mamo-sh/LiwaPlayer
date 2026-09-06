using LiwaPlayer.Models;
using LiwaPlayer.Services;
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

            chkThumbnails.IsChecked = s.ShowThumbnails;
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

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            var s = _settings.Current;

            bool renderChanged = s.SoftwareRendering != (chkSoftwareRender.IsChecked == true);

            s.ShowThumbnails = chkThumbnails.IsChecked == true;
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
