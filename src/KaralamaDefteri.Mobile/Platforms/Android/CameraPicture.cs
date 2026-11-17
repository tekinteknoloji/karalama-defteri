using Android.Graphics;
using Java.Nio;

namespace KaralamaDefteri.Mobile;

internal static class CameraPicture
{
    internal static Picture Decode(byte[] data, string fileName)
    {
        var jpeg = Encode(data, 3840, Bitmap.CompressFormat.Jpeg!);
        return PictureProcessing.FromBytes(jpeg, System.IO.Path.ChangeExtension(fileName, ".jpg"), 3 * 1024 * 1024);
    }

    internal static byte[] ForDisplay(StoredPicture picture)
    {
        return Encode(picture.Bytes, 2400, picture.Mime == "image/jpeg" ?
            Bitmap.CompressFormat.Jpeg! : Bitmap.CompressFormat.Png!);
    }

    private static byte[] Encode(byte[] data, int limit, Bitmap.CompressFormat format)
    {
        using var bitmap = OperatingSystem.IsAndroidVersionAtLeast(28)
            ? DecodeModern(data, limit)
            : BitmapFactory.DecodeByteArray(data, 0, data.Length);
        if (bitmap is null) throw new InvalidDataException("Kamera fotoğrafı okunamadı.");
        using var output = new MemoryStream();
        if (!bitmap.Compress(format, 85, output))
            throw new InvalidDataException("Kamera fotoğrafı kaydedilemedi.");
        return output.ToArray();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android28.0")]
    private static Bitmap DecodeModern(byte[] data, int limit)
    {
        using var buffer = ByteBuffer.Wrap(data);
        using var source = ImageDecoder.CreateSource(buffer);
        using var header = new PhotoHeader(limit);
        return ImageDecoder.DecodeBitmap(source, header);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android28.0")]
    private sealed class PhotoHeader(int limit) : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source source)
        {
            using var size = info.Size;
            var largest = Math.Max(size.Width, size.Height);
            if (largest <= limit) return;
            decoder.SetTargetSize(Math.Max(1, (int)Math.Round(size.Width * (double)limit / largest)),
                Math.Max(1, (int)Math.Round(size.Height * (double)limit / largest)));
        }
    }
}
