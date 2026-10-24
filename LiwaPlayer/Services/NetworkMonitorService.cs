using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LiwaPlayer.Services
{
    // Gerçek internet erişimini (sadece ağ kartının "bağlı" görünmesini değil)
    // periyodik olarak küçük bir istek atarak kontrol eder. Durum DEĞİŞTİĞİNDE
    // (çevrimdışı <-> çevrimiçi) olay tetiklenir; her denetimde değil.
    //
    // Aralık ve açık/kapalı durumu Ayarlar'dan gelir (Configure).
    //
    // Yanlış alarmı azaltmak için "çevrimdışı" kararı tek bir başarısız isteğe
    // bağlanmaz: iki farklı adres sınanır ve hepsi başarısız olursa birkaç
    // saniye sonra bir kez daha denenir. Yalnızca o zaman bağlantı kopmuş sayılır.
    public class NetworkMonitorService : IDisposable
    {
        // Birincisi Google'ın genel bağlantı denetimi, ikincisi uygulamanın
        // asıl ihtiyaç duyduğu YouTube. İkisinden biri yanıtlıyorsa internet vardır.
        private static readonly string[] ProbeUrls =
        {
            "https://www.gstatic.com/generate_204",
            "https://www.youtube.com/generate_204"
        };

        // NetworkChange.NetworkAvailabilityChanged, WPF arayüz iş parçacığında
        // DEĞİL, .NET'in kendi arka plan iş parçacığında tetiklenir. Bu yüzden
        // olayları dinleyen taraf (MessageBox, liste güncelleme gibi arayüz
        // işlemleri yapan MainWindow) her zaman bu Dispatcher üzerinden
        // çağrılır — aksi halde "yanlış iş parçacığından arayüze erişim"
        // hatasıyla uygulama anında çöker (tam olarak internet kesintisi anında).
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

        private bool? _lastKnownOnline;
        private bool _checking;
        private bool _enabled;
        private bool _disposed;

        public event Action? ConnectionLost;
        public event Action? ConnectionRestored;

        public NetworkMonitorService()
        {
            _timer.Tick += async (_, _) => await CheckAsync();

            // Ağ arayüzü durumu değişir değişmez (ör. kablo çekilince) hızlı denetim
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }

        // Ayarlar'dan gelen değerleri uygular; çalışırken de çağrılabilir
        public void Configure(bool enabled, int intervalSeconds)
        {
            bool wasEnabled = _enabled;

            _enabled = enabled;

            _timer.Stop();

            if (!enabled)
            {
                // Kapatılınca bir sonraki açılışta yeni bir başlangıç ölçümü alınır
                _lastKnownOnline = null;
                return;
            }

            _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 15, 3600));
            _timer.Start();

            if (!wasEnabled)
                _ = CheckAsync();
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            // Bu olay arka plan iş parçacığında geliyor; CheckAsync'i (ve
            // sonundaki olay tetiklemesini) arayüz iş parçacığına taşı
            if (_disposed || !_enabled)
                return;

            _dispatcher.BeginInvoke(new Action(async () => await CheckAsync()));
        }

        private async Task<bool> ProbeOnceAsync()
        {
            foreach (var url in ProbeUrls)
            {
                try
                {
                    using var response = await _http.GetAsync(url);

                    if (response.IsSuccessStatusCode || (int)response.StatusCode == 204)
                        return true;
                }
                catch
                {
                    // Bu adres yanıt vermedi; diğerini dene
                }
            }

            return false;
        }

        private async Task CheckAsync()
        {
            if (_checking || _disposed || !_enabled)
                return;

            _checking = true;

            bool online;

            try
            {
                online = await ProbeOnceAsync();

                // Kısa süreli bir takılmayı "internet yok" sayma: bir kez daha dene
                if (!online)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    online = await ProbeOnceAsync();
                }
            }
            finally
            {
                _checking = false;
            }

            // Denetim sürerken kapatıldıysa sonucu yok say
            if (_disposed || !_enabled)
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
                if (_disposed || !_enabled)
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
