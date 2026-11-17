using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;

namespace KaralamaDefteri;

internal static class Pictures
{
    public static byte[] Encode(BitmapSource source, bool jpeg = false)
    {
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 85 } : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    public static BitmapSource Decode(byte[] bytes, int decodeWidth = 0)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.EndInit();
        bitmap.Freeze();
        return RecoverInvisiblePixels(bitmap);
    }

    internal static BitmapSource RecoverInvisiblePixels(BitmapSource bitmap)
    {
        if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Pbgra32) return bitmap;
        var stride = checked(bitmap.PixelWidth * 4);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);
        var hasColor = false;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index + 3] != 0) return bitmap;
            hasColor |= (pixels[index] | pixels[index + 1] | pixels[index + 2]) != 0;
        }
        if (!hasColor) return bitmap;
        var opaque = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96,
            PixelFormats.Bgr32, null, pixels, stride);
        opaque.Freeze();
        return opaque;
    }

    public static bool Supports(string file) => PictureProcessing.Supports(file);
    public static Picture FromFile(string file, int threshold) => PictureProcessing.FromFile(file, threshold);
    public static Picture FromClipboard(BitmapSource image, int threshold)
    {
        image = RecoverInvisiblePixels(image);
        if (image.Format == PixelFormats.Bgr32)
            image = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        return PictureProcessing.FromClipboardPng(Encode(image), threshold);
    }
    public static void Save(StoredPicture picture, Window owner)
    {
        var ext = picture.Mime.Split('/')[1];
        if (ext == "jpeg") ext = "jpg";
        var dialog = new SaveFileDialog
        {
            Title = "Resmi farklı kaydet", FileName = Path.GetFileNameWithoutExtension(picture.FileName) ?? $"resim-{picture.Id}",
            DefaultExt = $".{ext}", Filter = $"Resim (*.{ext})|*.{ext}"
        };
        if (dialog.ShowDialog(owner) == true) File.WriteAllBytes(dialog.FileName, picture.Bytes);
    }
}

internal sealed class PictureGenerator(Func<long, FrameworkElement?> create) : VisualLineElementGenerator
{
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var end = CurrentContext.Document.GetLineByOffset(startOffset).EndOffset;
        if (end <= startOffset) return -1;
        var match = ImageMarkers.Pattern().Match(CurrentContext.Document.GetText(startOffset, end - startOffset));
        return match.Success ? startOffset + match.Index : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var end = CurrentContext.Document.GetLineByOffset(offset).EndOffset;
        var match = ImageMarkers.Pattern().Match(CurrentContext.Document.GetText(offset, end - offset));
        if (!match.Success || match.Index != 0) return null;
        var image = create(ImageMarkers.Id(match));
        return image == null ? null : new InlineObjectElement(match.Length, image);
    }
}


