using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KaralamaDefteri;

internal static class ClipboardPictures
{
    public static BitmapSource? Read()
    {
        var data = Clipboard.GetDataObject();
        return Read(data) ?? ReadNativeDib() ??
            (Clipboard.GetImage() is { } fallback ? Visible(Pictures.RecoverInvisiblePixels(fallback)) : null);
    }

    private static BitmapSource? ReadNativeDib()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            foreach (var format in new uint[] { 8, 17 })
            {
                var handle = GetClipboardData(format);
                if (handle == IntPtr.Zero) continue;
                var size = GlobalSize(handle);
                if (size < 40 || size > int.MaxValue) continue;
                var pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero) continue;
                try
                {
                    var bytes = new byte[(int)size];
                    Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    if (DecodeDib(bytes) is { } image) return image;
                }
                finally { GlobalUnlock(handle); }
            }
            return null;
        }
        finally { CloseClipboard(); }
    }

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);

    internal static BitmapSource? Read(IDataObject? data)
    {
        if (data is null) return null;
        foreach (var format in new[] { "PNG", DataFormats.Dib, "Format17" })
        {
            if (!data.GetDataPresent(format, false)) continue;
            try
            {
                var bytes = ReadBytes(data.GetData(format, false));
                if (bytes is null) continue;
                var image = format == "PNG" ? Pictures.Decode(bytes) : DecodeDib(bytes);
                if (image != null && Visible(image) is { } visible) return visible;
            }
            catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException) { }
        }
        return null;
    }

    internal static BitmapSource? Visible(BitmapSource bitmap)
    {
        var rgba = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = checked(rgba.PixelWidth * 4);
        var row = new byte[stride];
        for (var y = 0; y < rgba.PixelHeight; y++)
        {
            rgba.CopyPixels(new Int32Rect(0, y, rgba.PixelWidth, 1), row, stride, 0);
            for (var x = 3; x < row.Length; x += 4)
                if (row[x] != 0) return bitmap;
        }
        return null;
    }

    private static byte[]? ReadBytes(object? data)
    {
        if (data is byte[] bytes) return bytes;
        if (data is not Stream stream) return null;
        if (stream.CanSeek) stream.Position = 0;
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    internal static BitmapSource? DecodeDib(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 40) return null;
        var header = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        if (header < 40 || header > bytes.Length || width <= 0 || signedHeight is 0 or int.MinValue ||
            planes != 1 || bits is not (24 or 32) || compression is not (0 or 3 or 6)) return null;

        var offset = (long)header;
        if (compression != 0)
        {
            if (bits != 32 || bytes.Length < 52 ||
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[40..]) != 0x00FF0000 ||
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[44..]) != 0x0000FF00 ||
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[48..]) != 0x000000FF) return null;
            if (header == 40) offset += compression == 6 ? 16 : 12;
            else if (header < 52) return null;
        }
        offset += 4L * BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..]);
        var height = Math.Abs(signedHeight);
        var stride = ((long)width * bits + 31) / 32 * 4;
        if (offset > bytes.Length || stride > int.MaxValue || stride > (bytes.Length - offset) / height) return null;
        var pixels = new byte[(int)(stride * height)];
        for (var row = 0; row < height; row++)
        {
            var sourceRow = signedHeight > 0 ? height - 1 - row : row;
            bytes.Slice((int)(offset + sourceRow * stride), (int)stride).CopyTo(pixels.AsSpan((int)(row * stride)));
        }
        var image = BitmapSource.Create(width, height, 96, 96, bits == 32 ? PixelFormats.Bgr32 : PixelFormats.Bgr24,
            null, pixels, (int)stride);
        image.Freeze();
        return image;
    }
}
