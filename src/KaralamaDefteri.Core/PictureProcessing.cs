using SkiaSharp;

namespace KaralamaDefteri;

public static class PictureProcessing
{
    public static bool Supports(string file) => Mime(file) != null;

    private static string? Mime(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif",
        ".webp" => "image/webp", ".bmp" => "image/bmp", _ => null
    };

    public static Picture FromFile(string file, int threshold) =>
        FromBytes(File.ReadAllBytes(file), file, threshold);

    internal static Picture FromBytes(byte[] bytes, string file, int threshold) =>
        Prepare(bytes, Mime(file) ?? throw new NotSupportedException("Resim biçimi desteklenmiyor."),
            Path.GetFileName(file), threshold);

    public static Picture FromClipboardPng(byte[] bytes, int threshold) => Prepare(bytes, "image/png", null, threshold);

    private static Picture Prepare(byte[] bytes, string mime, string? name, int threshold)
    {
        using var original = SKBitmap.Decode(bytes) ?? throw new NotSupportedException("Resim verisi okunamadı.");
        using var resized = Fit(original, 3840);
        var image = resized ?? original;
        if (bytes.LongLength > threshold)
        {
            bytes = Encode(image, SKEncodedImageFormat.Jpeg);
            mime = "image/jpeg";
        }
        else if (resized != null)
        {
            bytes = Encode(image, SKEncodedImageFormat.Png);
            mime = "image/png";
        }
        using var thumbnail = Fit(image, 320);
        return new(mime, bytes, Encode(thumbnail ?? image, SKEncodedImageFormat.Png), image.Width, image.Height, name);
    }

    private static byte[] Encode(SKBitmap image, SKEncodedImageFormat format)
    {
        using var data = image.Encode(format, 85) ?? throw new NotSupportedException("Resim kodlanamadı.");
        return data.ToArray();
    }

    private static SKBitmap? Fit(SKBitmap image, int limit)
    {
        var scale = (double)limit / Math.Max(image.Width, image.Height);
        if (scale >= 1) return null;
        var size = new SKImageInfo(Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
        return image.Resize(size, new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new NotSupportedException("Resim yeniden boyutlandırılamadı.");
    }
}
