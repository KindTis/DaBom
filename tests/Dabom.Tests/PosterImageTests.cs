using Dabom.Library;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dabom.Tests;

[TestClass]
public sealed class PosterImageTests
{
    [TestMethod]
    public void TryLoad_DecodesLargeImageToPosterWidthAndFreezesIt()
    {
        var root = Directory.CreateTempSubdirectory("dabom-poster-image-");
        try
        {
            var path = Path.Combine(root.FullName, "poster.png");
            WritePng(path, 1200, 1800);

            var image = PosterImage.TryLoad(path);

            Assert.IsNotNull(image);
            Assert.AreEqual(240, image.PixelWidth);
            Assert.AreEqual(360, image.PixelHeight);
            Assert.IsTrue(image.IsFrozen);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task TryLoad_WhenImageIsCorrupt_ReturnsNull()
    {
        var root = Directory.CreateTempSubdirectory("dabom-poster-corrupt-");
        try
        {
            var path = Path.Combine(root.FullName, "poster.png");
            await File.WriteAllTextAsync(path, "not an image");

            Assert.IsNull(PosterImage.TryLoad(path));
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static void WritePng(string path, int width, int height)
    {
        var stride = width * 4;
        var bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, new byte[stride * height], stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
