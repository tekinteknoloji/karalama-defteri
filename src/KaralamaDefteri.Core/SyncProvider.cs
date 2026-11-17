namespace KaralamaDefteri;

internal interface ISyncProvider
{
    IEnumerable<Guid> ListRecords();
    byte[]? GetRecord(Guid id);
    void PutRecord(Guid id, byte[] encrypted);
    byte[]? GetBlob(Guid id);
    void PutBlob(Guid id, byte[] encrypted);
    void Delete(Guid id);
}

internal sealed class FolderSyncProvider(string root) : ISyncProvider
{
    private readonly string directory = Path.GetFullPath(root);

    internal FileStream BeginSync()
    {
        Directory.CreateDirectory(directory);
        return new FileStream(Path.Combine(directory, ".sync-lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal byte[] GetOrCreateSalt()
    {
        var path = Path.Combine(directory, ".sync-salt");
        if (!File.Exists(path)) Write(path, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var salt = File.ReadAllBytes(path);
        if (salt.Length != 32) throw new InvalidDataException("Senkron klasörü anahtar bilgisi geçersiz.");
        return salt;
    }

    internal void SaveDailyBackup(Func<byte[]> create)
    {
        var name = $"backup-{DateTime.UtcNow:yyyyMMdd}.encrypted";
        var target = Path.Combine(directory, name);
        if (!File.Exists(target)) Write(target, create());
        foreach (var older in Directory.EnumerateFiles(directory, "backup-????????.encrypted")
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(7))
            File.Delete(older);
    }

    public IEnumerable<Guid> ListRecords()
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.record"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id))
                yield return id;
    }

    public byte[]? GetRecord(Guid id) => Read(PathFor(id, ".record"));
    public byte[]? GetBlob(Guid id) => Read(PathFor(id, ".blob"));
    public void PutRecord(Guid id, byte[] encrypted) => Write(PathFor(id, ".record"), encrypted);
    public void PutBlob(Guid id, byte[] encrypted) => Write(PathFor(id, ".blob"), encrypted);
    public void Delete(Guid id) => File.Delete(PathFor(id, ".record"));

    private string PathFor(Guid id, string extension) => Path.Combine(directory, id.ToString("N") + extension);

    private static byte[]? Read(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
    }

    private static void Write(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }
}
