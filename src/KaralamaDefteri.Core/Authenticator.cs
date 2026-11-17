using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace KaralamaDefteri;

internal sealed record OtpAccount(Guid Id, string Issuer, string Name, string Secret, string Algorithm,
    int Digits, int Period, long? Counter, long? Group = null, string? Color = null, Guid? Note = null);

internal static class Authenticator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    internal static string Code(OtpAccount account, long unixTime) =>
        Code(account.Secret, account.Algorithm, account.Digits, account.Counter ?? unixTime / account.Period);

    internal static string Code(string secret, string algorithm, int digits, long factor)
    {
        var key = Decode(secret);
        try
        {
            Span<byte> input = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(input, factor);
            var hash = algorithm switch
            {
                "SHA1" => HMACSHA1.HashData(key, input),
                "SHA256" => HMACSHA256.HashData(key, input),
                "SHA512" => HMACSHA512.HashData(key, input),
                _ => throw new FormatException()
            };
            var number = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(hash[^1] & 15)) & int.MaxValue;
            return (number % (digits == 8 ? 100000000 : 1000000)).ToString($"D{digits}", CultureInfo.InvariantCulture);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal static OtpAccount Validate(OtpAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.Name) || account.Name.Length > 300 || account.Issuer.Length > 300 ||
            account.Algorithm is not ("SHA1" or "SHA256" or "SHA512") || account.Digits is not (6 or 8) ||
            account.Period is not (30 or 60) || account.Counter is < 0 || account.Secret.Length > 2048)
            throw new FormatException();
        var key = Decode(account.Secret);
        CryptographicOperations.ZeroMemory(key);
        return account;
    }

    internal static byte[] Decode(string input)
    {
        var result = new List<byte>();
        var bits = 0;
        var value = 0;
        foreach (var c in input.Trim().TrimEnd('=').Where(c => !char.IsWhiteSpace(c)))
        {
            var digit = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (digit < 0) throw new FormatException();
            value = (value << 5) | digit;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            result.Add((byte)(value >> bits));
        }
        if (result.Count == 0 || bits >= 5 || (value & ((1 << bits) - 1)) != 0) throw new FormatException();
        return result.ToArray();
    }

    internal static string Encode(byte[] input)
    {
        var output = new System.Text.StringBuilder();
        var value = 0;
        var bits = 0;
        foreach (var b in input)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5) { bits -= 5; output.Append(Alphabet[(value >> bits) & 31]); }
        }
        if (bits > 0) output.Append(Alphabet[(value << (5 - bits)) & 31]);
        return output.ToString();
    }

    internal static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=', 2))
        .Where(pair => pair.Length == 2).GroupBy(pair => pair[0], StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => Uri.UnescapeDataString(group.First()[1]), StringComparer.OrdinalIgnoreCase);

    internal static OtpAccount Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "otpauth" || uri.Host is not ("totp" or "hotp"))
            throw new FormatException();
        var query = Query(uri);
        var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Split(':', 2);
        return Validate(new(Guid.NewGuid(), query.GetValueOrDefault("issuer", label.Length == 2 ? label[0] : ""), label[^1],
            query.GetValueOrDefault("secret", ""), query.GetValueOrDefault("algorithm", "SHA1").ToUpperInvariant(),
            int.Parse(query.GetValueOrDefault("digits", "6"), CultureInfo.InvariantCulture),
            int.Parse(query.GetValueOrDefault("period", "30"), CultureInfo.InvariantCulture),
            uri.Host == "hotp" ? long.Parse(query.GetValueOrDefault("counter", ""), CultureInfo.InvariantCulture) : null));
    }

    internal static string Export(OtpAccount account)
    {
        var kind = account.Counter == null ? "totp" : "hotp";
        var label = string.IsNullOrEmpty(account.Issuer) ? account.Name : account.Issuer + ":" + account.Name;
        var factor = account.Counter is long counter ? $"counter={counter}" : $"period={account.Period}";
        return $"otpauth://{kind}/{Uri.EscapeDataString(label)}?secret={Uri.EscapeDataString(account.Secret)}&issuer={Uri.EscapeDataString(account.Issuer)}&algorithm={account.Algorithm}&digits={account.Digits}&{factor}";
    }
}
