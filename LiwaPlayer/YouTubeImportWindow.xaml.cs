using LiwaPlayer.Models;
using LiwaPlayer.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace LiwaPlayer
{
    public partial class YouTubeImportWindow : Window
    {
        private readonly PlaylistService _playlist;
        private readonly YouTubeService _youtube;

        private readonly ObservableCollection<string> _log = new();
        private readonly CancellationTokenSource _cancel = new();

        private bool _running;

        // İçe aktarma başarılıysa oluşturulan liste
        public Playlist? ImportedPlaylist { get; private set; }

        public YouTubeImportWindow(PlaylistService playlist, YouTubeService youtube)
        {
            InitializeComponent();

            UiScaleHelper.Apply(this);

            _playlist = playlist;
            _youtube = youtube;

            lstLog.ItemsSource = _log;

            txtUrl.Focus();
        }

        private void txtUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                btnImport_Click(sender, e);
        }

        private async void btnImport_Click(object sender, RoutedEventArgs e)
        {
            if (_running)
                return;

            var url = txtUrl.Text.Trim();

            if (!url.Contains("list=", StringComparison.OrdinalIgnoreCase))
            {
                txtProgress.Text = url.Contains("browse/MPREb", StringComparison.OrdinalIgnoreCase)
                    ? "Bu albüm sayfası bağlantısı. Albümde Paylaş'a basıp çıkan bağlantıyı kullanın."
                    : "Bağlantıda liste bulunamadı (list= içermeli).";
                return;
            }

            _running = true;
            btnImport.IsEnabled = false;
            txtUrl.IsEnabled = false;
            _log.Clear();

            try
            {
                txtProgress.Text = "Liste bilgisi alınıyor...";

                var title = await _youtube.GetPlaylistTitleAsync(url);

                // İsim çakışırsa sonuna numara ekle
                string name = string.IsNullOrWhiteSpace(title) ? "YouTube Listesi" : title;
                string finalName = name;
                int suffix = 2;

                while (_playlist.NameExists(finalName))
                    finalName = $"{name} ({suffix++})";

                var target = _playlist.CreatePlaylist(finalName);
                ImportedPlaylist = target;

                int count = 0;

                await foreach (var video in _youtube.EnumeratePlaylistVideosAsync(url))
                {
                    if (_cancel.IsCancellationRequested)
                        break;

                    _playlist.Add(new Song
                    {
                        FileName = video.VideoId,
                        Title = video.Title,
                        Artist = video.Author,
                        Duration = video.Duration,
                        Source = SongSource.YouTube
                    }, target, out bool added);

                    if (added)
                    {
                        count++;
                        _log.Add($"✓  {video.Title}");
                        lstLog.ScrollIntoView(_log[^1]);
                        txtProgress.Text = $"{count} şarkı alındı...";
                    }
                }

                _playlist.SetActive(target);

                txtProgress.Text = _cancel.IsCancellationRequested
                    ? $"İptal edildi. {count} şarkı eklendi."
                    : $"Bitti: \"{finalName}\" listesine {count} şarkı eklendi.";
            }
            catch (Exception ex)
            {
                LogService.Write($"YouTube içe aktarma ({url})", ex);
                txtProgress.Text = "Liste alınamadı: " + ex.Message;
            }
            finally
            {
                _running = false;
                btnImport.IsEnabled = true;
                txtUrl.IsEnabled = true;
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            _cancel.Cancel();
        }
    }
}
