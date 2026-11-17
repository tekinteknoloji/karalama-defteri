using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace KaralamaDefteri;

internal sealed class SyncEncryption : IDisposable
{
    private readonly byte[] key;

    internal SyncEncryption(byte[] derivedKey)
    {
        if (derivedKey.Length != 32) throw new CryptographicException("Senkron anahtarı geçersiz.");
        key = derivedKey.ToArray();
    }

    internal byte[] CopyKey() => key.ToArray();

    internal SyncEncryption(string password, byte[] salt)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(bytes) { Salt = salt, MemorySize = 65536, Iterations = 3, DegreeOfParallelism = 2 };
            key = argon.GetBytes(32);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal byte[] Seal<T>(Guid id, string purpose, T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value);
        var encrypted = new byte[plain.Length + 28];
        RandomNumberGenerator.Fill(encrypted.AsSpan(0, 12));
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(encrypted.AsSpan(0, 12), plain, encrypted.AsSpan(28), encrypted.AsSpan(12, 16), Context(id, purpose));
            return encrypted;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal T Open<T>(Guid id, string purpose, byte[] encrypted)
    {
        if (encrypted.Length < 28) throw new InvalidDataException("Senkron kaydı eksik.");
        var plain = new byte[encrypted.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(encrypted.AsSpan(0, 12), encrypted.AsSpan(28), encrypted.AsSpan(12, 16), plain, Context(id, purpose));
            return JsonSerializer.Deserialize<T>(plain) ?? throw new InvalidDataException("Senkron kaydı okunamadı.");
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static byte[] Context(Guid id, string purpose) => Encoding.UTF8.GetBytes($"KaralamaDefteri/sync/v1/{purpose}/{id:N}");
    internal byte[] BackupKey() => HMACSHA256.HashData(key, "KaralamaDefteri/sync/v1/full-backup"u8);
    public void Dispose() => CryptographicOperations.ZeroMemory(key);
}
