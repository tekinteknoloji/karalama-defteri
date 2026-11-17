namespace KaralamaDefteri;

internal sealed class CloudSyncKeyMismatchException : System.Security.Cryptography.CryptographicException
{
    internal CloudSyncKeyMismatchException(Exception inner) : base("Bulut senkron anahtarı doğrulanamadı.", inner) { }
}

internal sealed class CloudSyncProvider(CloudObjectStore store, string prefix = "") : ISyncProvider
{
    private static readonly Guid VerifierId = new("214d9898-968a-46b7-ae5c-dc27e144273f");
    private const string VerifierValue = "KaralamaDefteri/cloud-key/v1";

    internal void ResetArea()
    {
        var names = store.List().Where(IsAreaObject).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var name in names.OrderBy(name => name == prefix + "sync-salt" ? 2 : name == prefix + "key-verifier" ? 1 : 0))
            store.Delete(name);
        if (store.List().Any(IsAreaObject))
            throw new IOException("Bulut alanı tamamen temizlenemedi.");
    }

    private bool IsAreaObject(string name)
    {
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var key = name[prefix.Length..];
        if (key is "sync-salt" or "key-verifier") return true;
        if (key.Length == 25 && key.StartsWith("backup-", StringComparison.Ordinal) &&
            key.EndsWith(".encrypted", StringComparison.Ordinal))
            return key.AsSpan(7, 8).ToString().All(char.IsAsciiDigit);
        return key.Length > 32 && key[32..] is (".record" or ".blob") &&
            Guid.TryParseExact(key[..32], "N", out _);
    }

    internal void VerifyKey(SyncEncryption crypto)
    {
        if (store.Read(prefix + "key-verifier") is not { } data) return;
        try
        {
            if (crypto.Open<string>(VerifierId, "cloud-key", data) != VerifierValue)
                throw new InvalidDataException("Bulut anahtar doğrulayıcı kaydı geçersiz.");
        }
        catch (System.Security.Cryptography.AuthenticationTagMismatchException error)
        {
            throw new CloudSyncKeyMismatchException(error);
        }
    }

    internal void SaveKeyVerifier(SyncEncryption crypto)
    {
        var name = prefix + "key-verifier";
        if (store.Read(name) == null)
            store.Write(name, crypto.Seal(VerifierId, "cloud-key", VerifierValue));
        VerifyKey(crypto);
    }
    internal byte[] GetSalt(bool create = true)
    {
        var name = prefix + "sync-salt";
        var salt = store.Read(name);
        if (salt == null)
        {
            if (!create) throw new CloudSyncKeyMismatchException(new System.Security.Cryptography.CryptographicException());
            salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            store.Write(name, salt);
            salt = store.Read(name);
        }
        return salt is { Length: 32 } ? salt : throw new InvalidDataException("Bulut senkron anahtar bilgisi geçersiz.");
    }

    internal void SaveDailyBackup(Func<byte[]> create)
    {
        var name = $"{prefix}backup-{DateTime.UtcNow:yyyyMMdd}.encrypted";
        var all = store.List().Where(item => item.Length == name.Length &&
            item.StartsWith(prefix + "backup-", StringComparison.Ordinal) && item.EndsWith(".encrypted", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        if (!all.Contains(name)) { store.Write(name, create()); all.Add(name); }
        foreach (var old in all.OrderDescending(StringComparer.Ordinal).Skip(7)) store.Delete(old);
    }
    public IEnumerable<Guid> ListRecords()
    {
        foreach (var name in store.List())
            if (name.Length == prefix.Length + 39 && name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".record", StringComparison.Ordinal)
                && Guid.TryParseExact(name[prefix.Length..^7], "N", out var id))
                yield return id;
    }

    public byte[]? GetRecord(Guid id) => store.Read(Name(id, ".record"));
    public byte[]? GetBlob(Guid id) => store.Read(Name(id, ".blob"));
    public void PutRecord(Guid id, byte[] encrypted) => store.Write(Name(id, ".record"), encrypted);
    public void PutBlob(Guid id, byte[] encrypted) => store.Write(Name(id, ".blob"), encrypted);
    public void Delete(Guid id) => store.Delete(Name(id, ".record"));

    private string Name(Guid id, string extension) => prefix + id.ToString("N") + extension;
}
