using System;
using System.Threading;
using System.Windows;

namespace LiwaPlayer
{
    public partial class App : Application
    {
        private const string MutexName = @"Local\LiwaPlayer_SingleInstance";
        private const string ShowEventName = @"Local\LiwaPlayer_ShowSignal";

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
                if (new Services.SettingsService().Current.SoftwareRendering)
                    System.Windows.Media.RenderOptions.ProcessRenderMode =
                        System.Windows.Interop.RenderMode.SoftwareOnly;
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
