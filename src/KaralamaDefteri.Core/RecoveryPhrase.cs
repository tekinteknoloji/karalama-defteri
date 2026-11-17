using System.Security.Cryptography;
using System.Text;

namespace KaralamaDefteri;

internal static class RecoveryPhrase
{
    private static readonly string[] Words = LoadWords();

    private static string[] LoadWords()
    {
        using var stream = typeof(RecoveryPhrase).Assembly.GetManifestResourceStream("KaralamaDefteri.Core.recovery-words.txt")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    internal static string Generate() => string.Join(' ', Enumerable.Range(0, 24)
        .Select(_ => Words[RandomNumberGenerator.GetInt32(Words.Length)]));

    internal static byte[] TransitionMarker(bool enabling, string password)
    {
        var marker = new byte[49];
        marker[0] = enabling ? (byte)1 : (byte)0;
        RandomNumberGenerator.Fill(marker.AsSpan(1, 16));
        Rfc2898DeriveBytes.Pbkdf2(password, marker.AsSpan(1, 16), marker.AsSpan(17), 256000, HashAlgorithmName.SHA512);
        return marker;
    }

    internal static bool VerifyTransition(byte[] marker, string password)
    {
        Span<byte> proof = stackalloc byte[32];
        Rfc2898DeriveBytes.Pbkdf2(password, marker.AsSpan(1, 16), proof, 256000, HashAlgorithmName.SHA512);
        return CryptographicOperations.FixedTimeEquals(proof, marker.AsSpan(17));
    }

    private static byte[] Key(string phrase)
    {
        var words = phrase.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 24 || words.Any(word => Array.BinarySearch(Words, word, StringComparer.Ordinal) < 0))
            throw new CryptographicException("Kurtarma anahtarı 24 geçerli kelime içermeli.");
        return SHA256.HashData(Encoding.UTF8.GetBytes("KaralamaDefteri/recovery/v1/" + string.Join(' ', words)));
    }

    internal static byte[] Protect(string password, string phrase)
    {
        var key = Key(phrase);
        var plaintext = Encoding.UTF8.GetBytes(password);
        var result = new byte[28 + plaintext.Length];
        RandomNumberGenerator.Fill(result.AsSpan(0, 12));
        try
        {
            using var cipher = new AesGcm(key, 16);
            cipher.Encrypt(result.AsSpan(0, 12), plaintext, result.AsSpan(28), result.AsSpan(12, 16));
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal static string Unprotect(byte[] payload, string phrase)
    {
        if (payload.Length < 29) throw new CryptographicException("Kurtarma kaydı eksik.");
        var key = Key(phrase);
        var plaintext = new byte[payload.Length - 28];
        try
        {
            using var cipher = new AesGcm(key, 16);
            cipher.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }
}
