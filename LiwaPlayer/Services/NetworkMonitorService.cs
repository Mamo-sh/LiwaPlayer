using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LiwaPlayer.Services
{
    // Gerçek internet erişimini (sadece ağ kartının "bağlı" görünmesini değil)
    // periyodik olarak Google'ın bağlantı denetim uç noktasına küçük bir istek
    // atarak kontrol eder. Durum DEĞİŞTİĞİNDE (çevrimdışı <-> çevrimiçi) olay
    // tetiklenir; her denetimde değil.
    public class NetworkMonitorService : IDisposable
    {
        private const string ProbeUrl = "https://www.gstatic.com/generate_204";

        // NetworkChange.NetworkAvailabilityChanged, WPF arayüz iş parçacığında
        // DEĞİL, .NET'in kendi arka plan iş parçacığında tetiklenir. Bu yüzden
        // olayları dinleyen taraf (MessageBox, liste güncelleme gibi arayüz
        // işlemleri yapan MainWindow) her zaman bu Dispatcher üzerinden
        // çağrılır — aksi halde "yanlış iş parçacığından arayüze erişim"
        // hatasıyla uygulama anında çöker (tam olarak internet kesintisi anında).
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

        private bool? _lastKnownOnline;
        private bool _checking;
        private bool _disposed;

        public event Action? ConnectionLost;
        public event Action? ConnectionRestored;

        public NetworkMonitorService()
        {
            _timer.Tick += async (_, _) => await CheckAsync();
            _timer.Start();

            // Ağ arayüzü durumu değişir değişmez (ör. kablo çekilince) hızlı denetim
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

            _ = CheckAsync();
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            // Bu olay arka plan iş parçacığında geliyor; CheckAsync'i (ve
            // sonundaki olay tetiklemesini) arayüz iş parçacığına taşı
            if (_disposed)
                return;

            _dispatcher.BeginInvoke(new Action(async () => await CheckAsync()));
        }

        private async Task CheckAsync()
        {
            if (_checking || _disposed)
                return;

            _checking = true;

            bool online;

            try
            {
                using var response = await _http.GetAsync(ProbeUrl);
                online = response.IsSuccessStatusCode || (int)response.StatusCode == 204;
            }
            catch
            {
                online = false;
            }

            _checking = false;

            if (_disposed)
                return;

            bool hadPreviousReading = _lastKnownOnline.HasValue;
            bool changed = _lastKnownOnline != online;

            _lastKnownOnline = online;

            // İlk ölçüm bir "geçiş" sayılmaz, sadece başlangıç durumunu kaydeder
            if (!hadPreviousReading || !changed)
                return;

            // Olay dinleyicileri (MainWindow) arayüz öğelerine dokunuyor;
            // CheckAsync hangi iş parçacığında tamamlanırsa tamamlansın
            // (DispatcherTimer'dan geldiyse zaten arayüz iş parçacığıdır,
            // NetworkAvailabilityChanged'den geldiyse değildir) tetikleme
            // her zaman Dispatcher üzerinden, senkron olarak yapılır
            _dispatcher.Invoke(() =>
            {
                if (_disposed)
                    return;

                if (online)
                    ConnectionRestored?.Invoke();
                else
                    ConnectionLost?.Invoke();
            });
        }

        public void Dispose()
        {
            _disposed = true;

            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;

            _timer.Stop();
            _http.Dispose();
        }
    }
}
