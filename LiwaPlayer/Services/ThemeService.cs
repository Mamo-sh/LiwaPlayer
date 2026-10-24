using LiwaPlayer.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace LiwaPlayer.Services
{
    // Uygulama genelindeki 10 tema rengini (App.xaml'deki DynamicResource
    // fırçaları) çalışma zamanında değiştirir. Otomatik modda saate göre
    // (Ayarlar'daki açık/koyu başlangıç saatlerine göre) seçilir.
    public static class ThemeService
    {
        private static readonly Dictionary<string, Color> DarkPalette = new()
        {
            ["BgBrush"] = Color.FromRgb(0x0E, 0x0E, 0x14),
            ["SurfaceBrush"] = Color.FromRgb(0x17, 0x17, 0x1F),
            ["Surface2Brush"] = Color.FromRgb(0x20, 0x20, 0x2C),
            ["Surface3Brush"] = Color.FromRgb(0x2A, 0x2A, 0x3A),
            ["LineBrush"] = Color.FromRgb(0x2A, 0x2A, 0x38),
            ["AccentBrush"] = Color.FromRgb(0x7C, 0x6A, 0xF0),
            ["AccentHoverBrush"] = Color.FromRgb(0x8F, 0x7E, 0xFF),
            ["TextBrush"] = Color.FromRgb(0xEC, 0xEC, 0xF4),
            ["MutedBrush"] = Color.FromRgb(0x9A, 0x9A, 0xB0),
            ["TrackBrush"] = Color.FromRgb(0x32, 0x32, 0x42),
        };

        private static readonly Dictionary<string, Color> LightPalette = new()
        {
            ["BgBrush"] = Color.FromRgb(0xF3, 0xF3, 0xF7),
            ["SurfaceBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
            ["Surface2Brush"] = Color.FromRgb(0xF0, 0xF0, 0xF4),
            ["Surface3Brush"] = Color.FromRgb(0xE4, 0xE4, 0xEB),
            ["LineBrush"] = Color.FromRgb(0xDB, 0xDB, 0xE2),
            ["AccentBrush"] = Color.FromRgb(0x6A, 0x57, 0xE8),
            ["AccentHoverBrush"] = Color.FromRgb(0x59, 0x47, 0xD6),
            ["TextBrush"] = Color.FromRgb(0x18, 0x18, 0x1F),
            ["MutedBrush"] = Color.FromRgb(0x60, 0x60, 0x6E),
            ["TrackBrush"] = Color.FromRgb(0xD9, 0xD9, 0xE2),
        };

        // Otomatik modda açık temanın ve koyu temanın başladığı an (gece
        // yarısından itibaren dakika). Ayarlar'dan gelir; varsayılan 08:00-20:00.
        // Koyu başlangıç açık başlangıçtan önceyse aralık gece yarısını aşar
        // (ör. açık 06:00, koyu 02:00 → 06:00-24:00 ile 00:00-02:00 açık).
        private static int _lightStartMinutes = 8 * 60;
        private static int _darkStartMinutes = 20 * 60;

        public static bool IsDarkActive { get; private set; } = true;

        public static void SetSchedule(int lightStartMinutes, int darkStartMinutes)
        {
            _lightStartMinutes = Math.Clamp(lightStartMinutes, 0, 1439);
            _darkStartMinutes = Math.Clamp(darkStartMinutes, 0, 1439);
        }

        public static bool IsDayTime(DateTime now)
        {
            int minute = now.Hour * 60 + now.Minute;

            if (_lightStartMinutes == _darkStartMinutes)
                return false;

            return _lightStartMinutes < _darkStartMinutes
                ? minute >= _lightStartMinutes && minute < _darkStartMinutes
                : minute >= _lightStartMinutes || minute < _darkStartMinutes;
        }

        // Etkin (görünen) modu hesaplar: Auto ise saate bakar
        public static bool ResolveIsDark(ThemeMode mode) => mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => !IsDayTime(DateTime.Now)
        };

        // Periyodik denetim için: görünüm gerçekten değişecekse uygular, yoksa
        // renkleri boş yere yeniden yazıp arayüzü yeniden çizdirmez
        public static void RefreshAuto()
        {
            bool dark = ResolveIsDark(ThemeMode.Auto);

            if (dark != IsDarkActive)
                Apply(dark);
        }

        public static void Apply(ThemeMode mode)
        {
            bool dark = ResolveIsDark(mode);

            Apply(dark);
        }

        public static void Apply(bool dark)
        {
            IsDarkActive = dark;

            var palette = dark ? DarkPalette : LightPalette;
            var resources = Application.Current.Resources;

            foreach (var (key, color) in palette)
            {
                if (resources[key] is SolidColorBrush existing && !existing.IsFrozen)
                {
                    // Aynı örneği güncelle: FindResource ile önceden alınmış
                    // referanslar (ör. kod içinde saklanan fırçalar) da güncel kalır
                    existing.Color = color;
                }
                else
                {
                    resources[key] = new SolidColorBrush(color);
                }
            }
        }
    }
}
