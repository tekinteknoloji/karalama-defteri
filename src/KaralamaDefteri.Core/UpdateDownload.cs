using System.Net.Http.Json;
using System.Security.Cryptography;

namespace KaralamaDefteri;

internal sealed record UpdateManifest(string Version, Uri Url, string Sha256, long Size, string Notes, string MinVersion,
    SupportOptions? Support = null);

internal sealed class UpdateDownload(HttpClient http)
{
    internal async Task<UpdateManifest?> CheckAsync(Uri feed, string current, bool beta, CancellationToken cancellation,
        Action<SupportOptions>? receiveSupport = null)
    {
        RequireHttps(feed);
        using var response = await http.GetAsync(feed, cancellation);
        response.EnsureSuccessStatusCode();
        RequireHttps(response.RequestMessage!.RequestUri!);
        var update = await response.Content.ReadFromJsonAsync<UpdateManifest>(cancellation)
            ?? throw new InvalidDataException("Güncelleme bilgisi eksik.");
        if (string.IsNullOrWhiteSpace(update.Version) || string.IsNullOrWhiteSpace(update.MinVersion) ||
            update.Url == null || update.Sha256 is not { Length: 64 } || !update.Sha256.All(Uri.IsHexDigit) ||
            update.Size is <= 0 or > 512 * 1024 * 1024)
            throw new InvalidDataException("Güncelleme bilgisi geçersiz.");
        RequireHttps(update.Url);
        if (update.Support is { } support) receiveSupport?.Invoke(support);
        return (beta || !update.Version.Contains('-')) && Compare(update.Version, current) > 0 &&
            Compare(current, update.MinVersion) >= 0 ? update : null;
    }

    internal async Task DownloadAsync(UpdateManifest update, string path, CancellationToken cancellation, IProgress<long>? progress = null)
    {
        RequireHttps(update.Url);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            RequireHttps(response.RequestMessage!.RequestUri!);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[65536];
                long length = 0;
                long reported = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellation)) != 0)
                {
                    length += count;
                    if (length > update.Size) throw new InvalidDataException("Güncelleme boyutu eşleşmiyor.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
                    if (length - reported >= 524288 || length == update.Size)
                    {
                        progress?.Report(length);
                        reported = length;
                    }
                }
                if (length != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Güncelleme SHA-256 veya boyut doğrulamasından geçmedi.");
                output.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    private static void RequireHttps(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Güncelleme adresi HTTPS olmalı.");
    }

    internal static int Compare(string left, string right)
    {
        static (Version Number, string[] Pre) Parse(string value)
        {
            var parts = value.TrimStart('v').Split('+', 2)[0].Split('-', 2);
            if (!Version.TryParse(parts[0], out var version) || version.Build < 0 || version.Revision >= 0)
                throw new InvalidDataException("Sürüm numarası geçersiz.");
            var pre = parts.Length > 1 ? parts[1].Split('.') : [];
            if (pre.Any(part => part.Length == 0 || !part.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
                throw new InvalidDataException("Ön sürüm numarası geçersiz.");
            return (version, pre);
        }
        var a = Parse(left);
        var b = Parse(right);
        var number = a.Number.CompareTo(b.Number);
        if (number != 0) return number;
        if (a.Pre.Length == 0 || b.Pre.Length == 0) return b.Pre.Length.CompareTo(a.Pre.Length);
        foreach (var (x, y) in a.Pre.Zip(b.Pre))
        {
            var xn = x.All(char.IsAsciiDigit);
            var yn = y.All(char.IsAsciiDigit);
            var compare = xn && yn ? x.Length.CompareTo(y.Length) : 0;
            if (compare == 0) compare = xn == yn ? string.CompareOrdinal(x, y) : xn ? -1 : 1;
            if (compare != 0) return compare;
        }
        return a.Pre.Length.CompareTo(b.Pre.Length);
    }
}
