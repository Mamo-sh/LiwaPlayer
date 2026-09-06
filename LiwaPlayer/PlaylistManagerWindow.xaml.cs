using LiwaPlayer.Models;
using LiwaPlayer.Services;
using System.Windows;
using System.Windows.Input;

namespace LiwaPlayer
{
    public partial class PlaylistManagerWindow : Window
    {
        private readonly PlaylistService _service;

        public PlaylistManagerWindow(PlaylistService service)
        {
            InitializeComponent();

            UiScaleHelper.Apply(this);

            _service = service;

            lstPlaylists.ItemsSource = _service.Playlists;

            txtNewName.Focus();
        }

        private void txtNewName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                CreatePlaylist();
        }

        private void btnCreate_Click(object sender, RoutedEventArgs e) => CreatePlaylist();

        private void CreatePlaylist()
        {
            var name = txtNewName.Text.Trim();

            if (name.Length == 0)
            {
                txtMgrStatus.Text = "Liste adı boş olamaz.";
                return;
            }

            if (_service.NameExists(name))
            {
                txtMgrStatus.Text = $"\"{name}\" adında bir liste zaten var.";
                return;
            }

            _service.CreatePlaylist(name);

            txtNewName.Clear();
            txtMgrStatus.Text = $"\"{name}\" listesi oluşturuldu.";
        }

        private void OpenPlaylist(Playlist playlist)
        {
            _service.SetActive(playlist);

            DialogResult = true;
            Close();
        }

        private void btnOpen_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Playlist playlist)
                OpenPlaylist(playlist);
        }

        private void lstPlaylists_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstPlaylists.SelectedItem is Playlist playlist)
                OpenPlaylist(playlist);
        }

        private void btnDeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Playlist playlist)
                return;

            var answer = MessageBox.Show(
                $"\"{playlist.Name}\" listesi silinecek ({playlist.Songs.Count} şarkı kaydı).\n" +
                "Bilgisayardaki müzik dosyaları silinmez, sadece liste kaydı kaldırılır.\n\nEmin misiniz?",
                "Listeyi Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            _service.DeletePlaylist(playlist);

            txtMgrStatus.Text = $"\"{playlist.Name}\" listesi silindi.";
        }
    }
}
