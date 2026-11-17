using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class NoteStore
{
    internal static void VerifyPassword(string path, string password)
    {
        using var connection = OpenDatabase(path, password, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_schema";
        command.ExecuteScalar();
    }
    internal static bool IsPlainDatabase(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length == 0) return true;
        Span<byte> header = stackalloc byte[16];
        return file.ReadAtLeast(header, header.Length, false) == header.Length
            && header.SequenceEqual("SQLite format 3\0"u8);
    }

    internal static void ReplaceDatabaseFormat(string path, string? oldPassword, string? newPassword)
        => ReplaceClosedDatabase(path, path, oldPassword, newPassword);

    internal static void ReplaceClosedDatabase(string path, string input, string? inputPassword, string? outputPassword)
    {
        var temporary = path + ".convert";
        try
        {
            File.Delete(temporary);
            using (var source = OpenDatabase(input, inputPassword, SqliteOpenMode.ReadWrite))
            {
                using var command = source.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                using var checkpoint = command.ExecuteReader();
                if (!checkpoint.Read() || checkpoint.GetInt64(0) != 0)
                    throw new IOException("Veritabanı meşgul, dönüştürme tamamlanamadı.");
            }
            ExportDatabase(input, inputPassword, temporary, outputPassword);
            if (File.Exists(path + "-wal") || File.Exists(path + "-shm"))
                throw new IOException("Veritabanı açıkken biçim değiştirilemez.");
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);
            File.Replace(temporary, path, null);
        }
        finally { File.Delete(temporary); }
    }

    internal bool IsEncrypted => databasePassword != null;
    internal string? CloudDefaultPassword => databasePassword;
    internal bool MatchesOpenPassword(string password)
    {
        if (databasePassword == null) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            PasswordHash(databasePassword), PasswordHash(password));
    }

    private static byte[] PasswordHash(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        try { return System.Security.Cryptography.SHA256.HashData(bytes); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
    internal void ApplyDatabasePassword(string? password, string backups)
    {
        if (password == null && HasAuthenticatorAccounts())
            throw new InvalidOperationException("Şifrelemeyi kapatmadan önce doğrulayıcı hesaplarını kaldırın.");
        var path = db.DataSource;
        ConvertBackups(backups, databasePassword, password);
        Checkpoint();
        Dispose();
        ReplaceDatabaseFormat(path, databasePassword, password);
    }

    internal static void ConvertBackups(string backups, string? oldPassword, string? password)
    {
        if (Directory.Exists(backups))
        {
            foreach (var backup in Directory.EnumerateFiles(backups, "*.db"))
            {
                var plain = IsPlainDatabase(backup);
                if (password == null && plain) continue;
                if (password == null)
                {
                    using var existing = OpenDatabase(backup, oldPassword, SqliteOpenMode.ReadOnly);
                    if (HasAuthenticatorAccounts(existing)) continue;
                }
                if (oldPassword == null && !plain)
                    VerifyPassword(backup, password!);
                else
                    ReplaceDatabaseFormat(backup, oldPassword, password);
            }
        }
    }

    private static SqliteConnection OpenDatabase(string path, string? password, SqliteOpenMode mode, bool foreignKeys = false)
    {
        var options = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = mode, Pooling = false, ForeignKeys = foreignKeys, DefaultTimeout = 5
        };
        var connection = new SqliteConnection(options.ToString());
        try
        {
            connection.Open();
            if (password != null)
            {
                using var key = connection.CreateCommand();
                key.CommandText = "SELECT quote($password)";
                key.Parameters.AddWithValue("$password", password);
                var quoted = (string)key.ExecuteScalar()!;
                key.CommandText = "PRAGMA key = " + quoted;
                key.ExecuteNonQuery();
            }
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static void ExportDatabase(string input, string? inputPassword, string output, string? outputPassword)
    {
        if (File.Exists(output)) throw new IOException("Dönüştürme hedefi zaten var.");
        using (var source = OpenDatabase(input, inputPassword, SqliteOpenMode.ReadWriteCreate))
        {
            if (outputPassword == null && HasAuthenticatorAccounts(source))
                throw new InvalidOperationException("Doğrulayıcı hesapları şifresiz veritabanına aktarılamaz.");
            source.CreateFunction<string, string>("search_fold", Fold, isDeterministic: true);
            using var command = source.CreateCommand();
            command.CommandText = "PRAGMA cipher_version";
            if (command.ExecuteScalar() is not string version || !version.StartsWith("4.", StringComparison.Ordinal))
                throw new InvalidOperationException("SQLCipher 4 yüklenemedi.");
            command.CommandText = "PRAGMA user_version";
            var schemaVersion = (long)command.ExecuteScalar()!;
            command.CommandText = "ATTACH DATABASE $file AS converted KEY $key";
            command.Parameters.AddWithValue("$file", output);
            command.Parameters.AddWithValue("$key", outputPassword ?? "");
            command.ExecuteNonQuery();
            command.CommandText = "SELECT sqlcipher_export('converted')";
            command.ExecuteScalar();
            if (outputPassword == null)
            {
                command.CommandText = "DELETE FROM converted.settings WHERE key='syncPassword' OR (key IN ('cloudSessionGoogle','cloudSessionOneDrive') AND value NOT LIKE 'dpapi:%')";
                command.ExecuteNonQuery();
            }
            command.CommandText = FormattableString.Invariant($"PRAGMA converted.user_version={schemaVersion}");
            command.ExecuteNonQuery();
            command.CommandText = "DETACH DATABASE converted";
            command.ExecuteNonQuery();
        }
        using var target = OpenDatabase(output, outputPassword, SqliteOpenMode.ReadOnly);
        target.CreateFunction<string, string>("search_fold", Fold, isDeterministic: true);
        using var check = target.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        using var result = check.ExecuteReader();
        if (!result.Read() || result.GetString(0) != "ok" || result.Read())
            throw new InvalidDataException("Dönüştürülen veritabanı doğrulanamadı.");
    }
}
