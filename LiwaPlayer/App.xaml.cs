using System;
using System.Threading;
using System.Windows;

namespace LiwaPlayer
{
    public partial class App : Application
    {
        // Farklı bir veri klasörüyle (LIWAPLAYER_DATA) açılan kopya, normal kopyanın
        // kilidine takılmasın: kilit adına veri klasörünün özeti eklenir. Böylece
        // gerçek uygulama çalışırken izole bir test kopyası da açılabilir.
        private static readonly string InstanceSuffix = BuildInstanceSuffix();

        private static readonly string MutexName = @"Local\LiwaPlayer_SingleInstance" + InstanceSuffix;
        private static readonly string ShowEventName = @"Local\LiwaPlayer_ShowSignal" + InstanceSuffix;

        private static string BuildInstanceSuffix()
        {
            var custom = Environment.GetEnvironmentVariable("LIWAPLAYER_DATA");

            if (string.IsNullOrWhiteSpace(custom))
                return "";

            var hash = System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(custom).ToLowerInvariant()));

            return "_" + Convert.ToHexString(hash, 0, 6);
        }

        private static Mutex? _singleInstanceMutex;
        private EventWaitHandle? _showEvent;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // Zaten çalışan bir kopya var (pencere tepside gizli olabilir):
                // ona "kendini göster" sinyali yolla ve bu kopyayı kapat
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(ShowEventName);
                    signal.Set();
                }
                catch
                {
                }

                Shutdown();
                return;
            }

            // Optimizasyon ayarı: zayıf ekran kartlarında yazılım tabanlı çizim
            // (pencere oluşmadan önce, süreç genelinde uygulanmalı)
            try
            {
                var settings = new Services.SettingsService();

                if (settings.Current.SoftwareRendering)
                    System.Windows.Media.RenderOptions.ProcessRenderMode =
                        System.Windows.Interop.RenderMode.SoftwareOnly;

                // Tema, pencere oluşmadan önce uygulanır ki açılışta yanlış
                // renklerle bir an görünüp sonra değişmesin
                Services.ThemeService.SetSchedule(
                    settings.Current.AutoLightStartMinutes,
                    settings.Current.AutoDarkStartMinutes);

                Services.ThemeService.Apply(settings.Current.Theme);
            }
            catch
            {
            }

            // İkinci kopyalardan gelecek "göster" sinyalini arka planda dinle
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

            var listener = new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (MainWindow is MainWindow main)
                            main.ShowFromTray();
                    }));
                }
            })
            {
                IsBackground = true
            };

            listener.Start();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _showEvent?.Dispose();

            base.OnExit(e);
        }
    }
}
