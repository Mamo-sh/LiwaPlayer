using System;
using System.Windows;
using System.Windows.Media;

namespace LiwaPlayer
{
    // Arayüz ölçeği tüm pencerelerde ortak kullanılır. Ana pencere ayarı
    // yükleyip Current'ı doldurur; yardımcı pencereler ctor'da Apply çağırır.
    public static class UiScaleHelper
    {
        public const double Min = 0.8;
        public const double Max = 1.7;

        public static double Current { get; set; } = 1.0;

        public static double Clamp(double scale) =>
            Math.Clamp(Math.Round(scale, 2), Min, Max);

        // Yardımcı pencereler için: içeriği ölçekler, pencere boyutunu da büyütür
        public static void Apply(Window window)
        {
            if (Math.Abs(Current - 1.0) < 0.01)
                return;

            if (window.Content is FrameworkElement content)
                content.LayoutTransform = new ScaleTransform(Current, Current);

            var workArea = SystemParameters.WorkArea;

            window.MinWidth = Math.Min(window.MinWidth * Current, workArea.Width);
            window.MinHeight = Math.Min(window.MinHeight * Current, workArea.Height);
            window.Width = Math.Min(window.Width * Current, workArea.Width);
            window.Height = Math.Min(window.Height * Current, workArea.Height);
        }
    }
}
