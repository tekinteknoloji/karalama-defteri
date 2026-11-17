namespace KaralamaDefteri.Mobile;

internal sealed record MobileEntry(string Text = "", Picture? Picture = null)
{
    internal static async Task<Picture> ReadPicture(Stream input, string fileName, bool camera = false)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[65536];
        int read;
        while ((read = await input.ReadAsync(bytes)) != 0)
        {
            if (buffer.Length + read > 32 * 1024 * 1024)
                throw new InvalidDataException("Resim 32 MB sınırını aşıyor.");
            buffer.Write(bytes, 0, read);
        }
        var data = buffer.ToArray();
        return await Task.Run(() => camera
            ? CameraPicture.Decode(data, fileName)
            : PictureProcessing.FromBytes(data, fileName, 3 * 1024 * 1024));
    }
}
