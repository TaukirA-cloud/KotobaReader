using System.IO;
using System.Windows.Media.Imaging;
using KotobaReader.Core;

namespace KotobaReader.Services;

public static class PageDecoder
{
    public static BitmapSource Load(ComicDocument comic, int index, CancellationToken token)
    {
        var bytes = comic.ReadPage(index, token);
        using var input = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        int width = frame.PixelWidth, height = frame.PixelHeight;
        if (width <= 0 || height <= 0 || width > 100_000 || height > 100_000 || (long)width * height > 150_000_000)
            throw new InvalidDataException("Image exceeds the 150 megapixel safety limit.");
        token.ThrowIfCancellationRequested();
        input.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (width >= height) image.DecodePixelWidth = Math.Min(width, 4096);
        else image.DecodePixelHeight = Math.Min(height, 4096);
        image.StreamSource = input;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
