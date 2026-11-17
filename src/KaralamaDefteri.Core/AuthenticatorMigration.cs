namespace KaralamaDefteri;

internal sealed class AuthenticatorMigration
{
    private readonly Dictionary<int, OtpAccount[]> parts = [];
    private (ulong Id, int Size)? batch;

    internal static string[] Export(IReadOnlyList<OtpAccount> accounts)
    {
        if (accounts.Count is < 1 or > 300) throw new FormatException("Aktarımda 1–300 hesap seçin.");
        var chunks = accounts.Chunk(3).ToArray();
        var id = (ulong)System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);
        return chunks.Select((chunk, index) =>
        {
            using var payload = new MemoryStream();
            foreach (var input in chunk)
            {
                var account = Authenticator.Validate(input);
                if (account.Period != 30) throw new FormatException("Google QR aktarımı 30 saniyelik hesapları destekler.");
                using var entry = new MemoryStream();
                var secret = Authenticator.Decode(account.Secret);
                try { Bytes(entry, 1, secret); }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret); }
                Bytes(entry, 2, System.Text.Encoding.UTF8.GetBytes(account.Name));
                Bytes(entry, 3, System.Text.Encoding.UTF8.GetBytes(account.Issuer));
                Number(entry, 4, account.Algorithm switch { "SHA256" => 2, "SHA512" => 3, _ => 1 });
                Number(entry, 5, account.Digits == 8 ? 2UL : 1UL);
                Number(entry, 6, account.Counter is null ? 2UL : 1UL);
                if (account.Counter is long counter) Number(entry, 7, (ulong)counter);
                Bytes(payload, 1, entry.ToArray());
            }
            Number(payload, 2, 1);
            Number(payload, 3, (ulong)chunks.Length);
            Number(payload, 4, (ulong)index);
            Number(payload, 5, id);
            return "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload.ToArray()));
        }).ToArray();

        static void Number(Stream stream, int field, ulong value)
        {
            Integer(stream, (ulong)(field << 3));
            Integer(stream, value);
        }
        static void Bytes(Stream stream, int field, byte[] value)
        {
            Integer(stream, (ulong)((field << 3) | 2));
            Integer(stream, (ulong)value.Length);
            stream.Write(value);
        }
        static void Integer(Stream stream, ulong value)
        {
            while (value >= 128)
            {
                stream.WriteByte((byte)((value & 127) | 128));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }
    }

    internal OtpAccount[]? Import(string value)
    {
        if (!value.StartsWith("otpauth-migration:", StringComparison.OrdinalIgnoreCase)) return [Authenticator.Parse(value)];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host != "offline") throw new FormatException();
        var encoded = Authenticator.Query(uri).GetValueOrDefault("data", "");
        if (encoded.Length is 0 or > 100000) throw new FormatException();
        var reader = new ProtoReader(Convert.FromBase64String(encoded));
        var accounts = new List<OtpAccount>();
        int size = 1, index = 0;
        ulong id = 0;
        while (reader.Next(out var field, out var number, out var bytes))
        {
            switch (field)
            {
                case 1: accounts.Add(ReadAccount(bytes)); break;
                case 2 when number is not (1 or 2): throw new FormatException();
                case 3: size = checked((int)number); break;
                case 4: index = checked((int)number); break;
                case 5: id = number; break;
            }
        }
        if (size is < 1 or > 100 || index < 0 || index >= size || accounts.Count == 0) throw new FormatException();
        if (batch != (id, size)) { parts.Clear(); batch = (id, size); }
        parts[index] = accounts.ToArray();
        if (parts.Count < size) return null;
        var result = parts.OrderBy(part => part.Key).SelectMany(part => part.Value).ToArray();
        parts.Clear();
        batch = null;
        return result;
    }

    private static OtpAccount ReadAccount(ReadOnlySpan<byte> data)
    {
        var reader = new ProtoReader(data);
        string name = "", issuer = "", secret = "", algorithm = "SHA1";
        int digits = 6, type = 0;
        long counter = 0;
        while (reader.Next(out var field, out var number, out var bytes))
        {
            switch (field)
            {
                case 1: secret = Authenticator.Encode(bytes.ToArray()); break;
                case 2: name = System.Text.Encoding.UTF8.GetString(bytes); break;
                case 3: issuer = System.Text.Encoding.UTF8.GetString(bytes); break;
                case 4: algorithm = number switch { 0 or 1 => "SHA1", 2 => "SHA256", 3 => "SHA512", _ => throw new FormatException() }; break;
                case 5: digits = number switch { 0 or 1 => 6, 2 => 8, _ => throw new FormatException() }; break;
                case 6: type = checked((int)number); break;
                case 7: counter = checked((long)number); break;
            }
        }
        if (type is not (1 or 2)) throw new FormatException();
        return Authenticator.Validate(new(Guid.NewGuid(), issuer, name, secret, algorithm, digits, 30, type == 1 ? counter : null));
    }

    private ref struct ProtoReader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> remaining = data;

        internal bool Next(out int field, out ulong number, out ReadOnlySpan<byte> bytes)
        {
            field = 0;
            number = 0;
            bytes = default;
            if (remaining.IsEmpty) return false;
            var tag = ReadInteger();
            field = checked((int)(tag >> 3));
            switch (tag & 7)
            {
                case 0: number = ReadInteger(); break;
                case 1: Take(8); break;
                case 2: bytes = Take(checked((int)ReadInteger())); break;
                case 5: Take(4); break;
                default: throw new FormatException();
            }
            return true;
        }

        private ulong ReadInteger()
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                var b = Take(1)[0];
                if (shift == 63 && b > 1) throw new FormatException();
                value |= (ulong)(b & 127) << shift;
                if (b < 128) return value;
            }
            throw new FormatException();
        }

        private ReadOnlySpan<byte> Take(int length)
        {
            if (length > remaining.Length) throw new FormatException();
            var bytes = remaining[..length];
            remaining = remaining[length..];
            return bytes;
        }
    }
}
