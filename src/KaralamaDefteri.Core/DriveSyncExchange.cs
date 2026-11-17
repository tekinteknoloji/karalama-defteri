using System.Security.Cryptography;

namespace KaralamaDefteri;

internal static class DriveSyncExchange
{
    internal static (int Conflicts, byte[] Salt, byte[] Key) Run(
        HttpClient http, string accessToken, string database, string? databasePassword, string area,
        string? password, byte[]? savedSalt = null, byte[]? savedKey = null,
        Action? localChanges = null, Func<Guid, bool>? isNoteEditing = null)
    {
        var provider = new CloudSyncProvider(new GoogleDriveObjectStore(http, () => accessToken), area);
        if (password is null && (savedSalt is null || savedKey is null))
            throw new CloudSyncKeyMismatchException(new CryptographicException());
        var salt = provider.GetSalt(create: password is not null);
        var reuse = savedKey is not null && savedSalt is not null && savedSalt.AsSpan().SequenceEqual(salt);
        using var crypto = reuse ? new SyncEncryption(savedKey!) : password is not null
            ? new SyncEncryption(password, salt)
            : throw new CloudSyncKeyMismatchException(new CryptographicException());
        using var connection = new NoteStore(database, databasePassword, syncConnection: true);
        var conflicts = connection.SyncNotes(provider, crypto, salt, provider.SaveDailyBackup,
            localChanges: localChanges, isNoteEditing: isNoteEditing);
        return (conflicts, salt, crypto.CopyKey());
    }
}
