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

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

        private bool? _lastKnownOnline;
        private bool _checking;

        public event Action? ConnectionLost;
        public event Action? ConnectionRestored;

        public NetworkMonitorService()
        {
            _timer.Tick += async (_, _) => await CheckAsync();
            _timer.Start();

            // Ağ arayüzü durumu değişir değişmez (ör. kablo çekilince) hızlı denetim
            NetworkChange.NetworkAvailabilityChanged += async (_, _) => await CheckAsync();

            _ = CheckAsync();
        }

        private async Task CheckAsync()
        {
            if (_checking)
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

            bool hadPreviousReading = _lastKnownOnline.HasValue;
            bool changed = _lastKnownOnline != online;

            _lastKnownOnline = online;

            // İlk ölçüm bir "geçiş" sayılmaz, sadece başlangıç durumunu kaydeder
            if (!hadPreviousReading || !changed)
                return;

            if (online)
                ConnectionRestored?.Invoke();
            else
                ConnectionLost?.Invoke();
        }

        public void Dispose()
        {
            _timer.Stop();
            _http.Dispose();
        }
    }
}
