using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LiwaPlayer
{
    // Kapak URL'sini KÜÇÜK çözünürlükte BitmapImage'a çevirir. DecodePixelWidth
    // verilmezse WPF resmi tam boyutuyla çözer ve önbelleğe alır; liste dolunca
    // bellek şişer. 64px'lik kutu için 160px çözmek fazlasıyla yeterli.
    public class ThumbnailImageConverter : IValueConverter
    {
        public int DecodeWidth { get; set; } = 160;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var url = value as string;

            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                var image = new BitmapImage();

                image.BeginInit();
                image.UriSource = new Uri(url);
                image.DecodePixelWidth = DecodeWidth;
                image.EndInit();

                return image;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
