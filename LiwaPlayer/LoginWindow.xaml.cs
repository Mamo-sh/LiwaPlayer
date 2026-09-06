using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;

namespace LiwaPlayer
{
    public partial class LoginWindow : Window
    {
        private const string LoginUrl =
            "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";

        // Uygulama klasörü salt okunur olabilir (Program Files, USB vb.);
        // tarayıcı profili her zaman yazılabilir olan LocalAppData'da tutulur
        public static string WebViewDataFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiwaPlayer",
                "WebView2");

        // Başarılı girişte doldurulur
        public List<Cookie>? ResultCookies { get; private set; }

        private bool _completed;

        public LoginWindow()
        {
            InitializeComponent();

            UiScaleHelper.Apply(this);

            Loaded += LoginWindow_Loaded;
        }

        private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(WebViewDataFolder);

                var environment = await CreateEnvironmentWithRetryAsync();

                await webView.EnsureCoreWebView2Async(environment);

                // Önceki oturumun çerezlerini sil ki her girişte hesap seçimi
                // temiz başlasın (profil klasörünü silmeye gerek kalmaz)
                webView.CoreWebView2.CookieManager.DeleteAllCookies();

                webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;
                webView.CoreWebView2.Navigate(LoginUrl);

                txtLoading.Visibility = Visibility.Collapsed;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(
                    "Bu özellik için Microsoft Edge WebView2 Runtime gereklidir.\n\n" +
                    "İndirme adresi:\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                    "WebView2 Gerekli",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                DialogResult = false;
                Close();
            }
            catch (Exception ex)
            {
                bool resourceBusy =
                    ex.HResult == unchecked((int)0x800700AA) ||
                    ex.Message.Contains("kullanımda", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("in use", StringComparison.OrdinalIgnoreCase);

                MessageBox.Show(
                    resourceBusy
                        ? "Tarayıcı profili başka bir işlem tarafından kullanılıyor.\n\n" +
                          "Birkaç saniye bekleyip tekrar deneyin. Sorun sürerse uygulamayı " +
                          "kapatıp yeniden açın."
                        : "Giriş penceresi açılamadı.\n\nHata: " + ex.Message,
                    "Giriş Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                DialogResult = false;
                Close();
            }
        }

        // Önceki giriş penceresinin tarayıcı süreci kapanırken profil kısa süre
        // kilitli kalabilir ("istenen kaynak kullanımda"); bekleyip yeniden dene
        private static async Task<CoreWebView2Environment> CreateEnvironmentWithRetryAsync()
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await CoreWebView2Environment.CreateAsync(null, WebViewDataFolder);
                }
                catch (Exception ex) when (attempt < 4 && ex is not WebView2RuntimeNotFoundException)
                {
                    await Task.Delay(1000 * attempt);
                }
            }
        }

        private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_completed)
                return;

            var url = webView.CoreWebView2?.Source ?? "";

            // Giriş bitince Google, youtube.com'a yönlendirir; oturum çerezini kontrol et
            if (!url.Contains("youtube.com"))
                return;

            try
            {
                var cookies = await webView.CoreWebView2!.CookieManager
                    .GetCookiesAsync("https://www.youtube.com");

                bool loggedIn = cookies.Any(c =>
                    c.Name == "SAPISID" || c.Name == "__Secure-3PAPISID");

                if (!loggedIn)
                    return;

                ResultCookies = cookies
                    .Select(c => c.ToSystemNetCookie())
                    .ToList();

                _completed = true;
                DialogResult = true;
                Close();
            }
            catch
            {
                // Çerez okunamazsa kullanıcı pencereyi kapatana kadar beklenir
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Tarayıcı sürecini hemen serbest bırak; bırakılmazsa bir sonraki
            // giriş denemesi profil kilidine takılır
            try
            {
                webView.Dispose();
            }
            catch
            {
            }

            base.OnClosed(e);
        }
    }
}
