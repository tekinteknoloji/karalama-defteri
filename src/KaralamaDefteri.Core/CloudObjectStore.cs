using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace KaralamaDefteri;

internal abstract class CloudObjectStore(HttpClient client, Func<string> accessToken)
{
    protected byte[]? Send(HttpMethod method, string url, HttpContent? content = null, bool allowMissing = false)
    {
        // Creation can succeed on Drive even when its response is lost.
        // The next sync resolves the existing object by name before creating again.
        for (var attempt = 0; ; attempt++)
        {
            try { return SendOnce(method, url, content, allowMissing); }
            catch (Exception error) when (method == HttpMethod.Get && attempt < 4 && IsTransient(error))
            {
                Thread.Sleep(TimeSpan.FromSeconds(1 << attempt));
            }
        }
    }

    internal static bool IsTransient(Exception error) =>
        error is OperationCanceledException or IOException ||
        error is HttpRequestException request &&
        (request.StatusCode == null || (int)request.StatusCode == 429 || (int)request.StatusCode is >= 500 and <= 599);

    private byte[]? SendOnce(HttpMethod method, string url, HttpContent? content, bool allowMissing)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken());
        // Android's HTTP handler only supports asynchronous requests.
        using var response = client.SendAsync(request).GetAwaiter().GetResult();
        if (allowMissing && response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    protected static ByteArrayContent Binary(byte[] data)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    internal abstract IEnumerable<string> List();
    internal abstract byte[]? Read(string name);
    internal abstract void Write(string name, byte[] value);
    internal abstract void Delete(string name);
}

internal sealed class OneDriveObjectStore(HttpClient client, Func<string> accessToken) : CloudObjectStore(client, accessToken)
{
    private const string Root = "https://graph.microsoft.com/v1.0/me/drive/special/approot";
    private static string Item(string name) => Root + ":/" + Uri.EscapeDataString(name) + ":";

    internal override IEnumerable<string> List()
    {
        string? url = Root + "/children?$select=name";
        while (url != null)
        {
            var endpoint = new Uri(url);
            if (endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Host != "graph.microsoft.com" || !endpoint.IsDefaultPort)
                throw new InvalidDataException("OneDrive sayfalama adresi geçersiz.");
            using var page = JsonDocument.Parse(Send(HttpMethod.Get, url)!);
            foreach (var item in page.RootElement.GetProperty("value").EnumerateArray())
                yield return item.GetProperty("name").GetString()!;
            url = page.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }
    }

    internal override byte[]? Read(string name) => Send(HttpMethod.Get, Item(name) + "/content", allowMissing: true);
    internal override void Write(string name, byte[] value) => Send(HttpMethod.Put, Item(name) + "/content", Binary(value));
    internal override void Delete(string name) => Send(HttpMethod.Delete, Item(name), allowMissing: true);
}

internal sealed class GoogleDriveObjectStore(HttpClient client, Func<string> accessToken) : CloudObjectStore(client, accessToken)
{
    private const string Files = "https://www.googleapis.com/drive/v3/files";
    private const string Upload = "https://www.googleapis.com/upload/drive/v3/files";
    private Dictionary<string, string[]>? snapshot;

    private IEnumerable<(string Name, string Id)> Items(string? name = null)
    {
        var query = name == null ? "" : "&q=" + Uri.EscapeDataString("name = '" + name.Replace("\\", "\\\\").Replace("'", "\\'") + "'");
        string? token = null;
        do
        {
            var url = Files + "?spaces=appDataFolder&pageSize=1000&fields=nextPageToken,files(id,name)" + query;
            if (token != null) url += "&pageToken=" + Uri.EscapeDataString(token);
            using var page = JsonDocument.Parse(Send(HttpMethod.Get, url)!);
            foreach (var item in page.RootElement.GetProperty("files").EnumerateArray())
                yield return (item.GetProperty("name").GetString()!, item.GetProperty("id").GetString()!);
            token = page.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (token != null);
    }

    internal override IEnumerable<string> List()
    {
        snapshot = Items().GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToArray(), StringComparer.Ordinal);
        return snapshot.Keys.ToArray();
    }

    private string? FindId(string name) => snapshot == null
        ? Items(name).SingleOrDefault().Id
        : snapshot.GetValueOrDefault(name)?.SingleOrDefault();
    internal override byte[]? Read(string name)
    {
        var id = FindId(name);
        return id == null ? null : Send(HttpMethod.Get, Files + "/" + Uri.EscapeDataString(id) + "?alt=media", allowMissing: true);
    }

    internal override void Write(string name, byte[] value)
    {
        // Recheck misses before creating to narrow races with other devices.
        var id = FindId(name) ?? (snapshot == null ? null : Items(name).SingleOrDefault().Id);
        if (id != null)
        {
            Send(HttpMethod.Patch, Upload + "/" + Uri.EscapeDataString(id) + "?uploadType=media", Binary(value));
            if (snapshot != null) snapshot[name] = [id];
            return;
        }
        using var body = new MultipartContent("related");
        body.Add(new StringContent(JsonSerializer.Serialize(new { name, parents = new[] { "appDataFolder" } }), Encoding.UTF8, "application/json"));
        body.Add(Binary(value));
        var created = Send(HttpMethod.Post, Upload + "?uploadType=multipart&fields=id", body);
        if (snapshot != null && created is { Length: > 0 })
        {
            using var result = JsonDocument.Parse(created);
            if (result.RootElement.TryGetProperty("id", out var property) && property.GetString() is { } newId)
                snapshot[name] = [newId];
        }
    }

    internal override void Delete(string name)
    {
        foreach (var item in Items(name).ToArray())
            Send(HttpMethod.Delete, Files + "/" + Uri.EscapeDataString(item.Id), allowMissing: true);
        snapshot?.Remove(name);
    }
}
