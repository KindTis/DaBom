using System.IO;
using System.Windows.Media.Imaging;

namespace Dabom.Library;

internal static class PosterImage
{
    internal static async Task<string?> TryGetExtensionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => TryGetExtension(bytes))
            .WaitAsync(cancellationToken);
    }

    private static string? TryGetExtension(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.StreamSource = stream;
            image.EndInit();
            return bytes switch
            {
                [0xFF, 0xD8, 0xFF, ..] => ".jpg",
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ".png",
                [0x42, 0x4D, ..] => ".bmp",
                _ => null
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    internal static BitmapSource? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.DecodePixelWidth = 240;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }
}
