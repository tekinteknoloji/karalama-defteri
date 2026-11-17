using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KaralamaDefteri;
using Xunit;

public class ClipboardPictureTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public void DibIgnoresAlphaAndRespectsRowOrder(int height)
    {
        var bytes = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), height);
        bytes[12] = 1;
        bytes[14] = 32;
        bytes[height > 0 ? 46 : 42] = 255;
        bytes[height > 0 ? 40 : 44] = 255;
        var bitmap = ClipboardPictures.DecodeDib(bytes)!;
        var pixels = new byte[8];
        bitmap.CopyPixels(pixels, 4, 0);
        Assert.Equal(PixelFormats.Bgr32, bitmap.Format);
        Assert.Equal(255, pixels[2]);
        Assert.Equal(255, pixels[4]);
        Assert.Null(ClipboardPictures.DecodeDib(bytes.AsSpan(0, 47)));
    }

    [Fact]
    public void OpaqueDibColorsSurviveStorageAndThumbnail()
    {
        var image = BitmapSource.Create(640, 1, 96, 96, PixelFormats.Bgr32, null,
            Enumerable.Range(0, 640).SelectMany(_ => new byte[] { 50, 80, 120, 0 }).ToArray(), 2560);
        var stored = Pictures.FromClipboard(image, int.MaxValue);
        foreach (var bytes in new[] { stored.Bytes, stored.Thumbnail })
        {
            using var decoded = SkiaSharp.SKBitmap.Decode(bytes);
            var color = decoded.GetPixel(0, 0);
            Assert.Equal(255, color.Alpha);
            Assert.Equal(120, color.Red);
        }
    }

    [Fact]
    public void PngStreamRecoversZeroAlphaRgb()
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 50, 80, 120, 0 }, 4);
        using var stream = new MemoryStream(Pictures.Encode(bitmap));
        var clipboard = new DataObject();
        clipboard.SetData("PNG", stream);
        var result = ClipboardPictures.Read(clipboard)!;
        Assert.True(result.IsFrozen);
        Assert.Equal(PixelFormats.Bgr32, result.Format);
        var pixel = new byte[4];
        result.CopyPixels(pixel, 4, 0);
        Assert.Equal(new byte[] { 50, 80, 120 }, pixel[..3]);
    }
}
