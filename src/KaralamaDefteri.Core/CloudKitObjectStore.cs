using System.Net.Http.Json;
using System.Text.Json;

namespace KaralamaDefteri;

internal sealed class CloudKitObjectStore : CloudObjectStore
{
    private const string RecordType = "KaralamaSyncObject";
    private readonly HttpClient client;
    private readonly string root;
    private readonly string apiToken;
    private readonly Func<string> webToken;

    internal CloudKitObjectStore(HttpClient client, string container, bool production, string apiToken, Func<string> webToken)
        : base(client, webToken)
    {
        if (!container.StartsWith("iCloud.", StringComparison.Ordinal) || container.Length <= 7)
            throw new ArgumentException("CloudKit kapsayıcı kimliği geçersiz.", nameof(container));
        this.client = client;
        this.apiToken = apiToken;
        this.webToken = webToken;
        root = $"https://api.apple-cloudkit.com/database/1/{Uri.EscapeDataString(container)}/{(production ? "production" : "development")}/private/records/";
    }

    internal override IEnumerable<string> List()
    {
        string? cursor = null;
        do
        {
            using var result = Post("query", new { query = new { recordType = RecordType }, desiredKeys = Array.Empty<string>(), continuationMarker = cursor });
            foreach (var record in result.RootElement.GetProperty("records").EnumerateArray())
            {
                Check(record);
                yield return record.GetProperty("recordName").GetString()!;
            }
            cursor = result.RootElement.TryGetProperty("continuationMarker", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrEmpty(cursor));
    }

    internal override byte[]? Read(string name)
    {
        using var result = Post("lookup", new { records = new[] { new { recordName = name } } });
        var record = result.RootElement.GetProperty("records").EnumerateArray().Single();
        if (!Check(record, allowMissing: true)) return null;
        return Convert.FromBase64String(record.GetProperty("fields").GetProperty("payload").GetProperty("value").GetString()!);
    }

    internal override void Write(string name, byte[] value) => Modify("forceReplace", new
    {
        recordName = name, recordType = RecordType,
        fields = new { payload = new { value = Convert.ToBase64String(value), type = "BYTES" } }
    });

    internal override void Delete(string name) => Modify("forceDelete", new { recordName = name }, allowMissing: true);

    private void Modify(string operationType, object record, bool allowMissing = false)
    {
        using var result = Post("modify", new { operations = new[] { new { operationType, record } } });
        Check(result.RootElement.GetProperty("records").EnumerateArray().Single(), allowMissing);
    }

    private JsonDocument Post(string operation, object body)
    {
        var token = webToken();
        if (string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("CloudKit API ve web oturumu belirteçleri gerekli.");
        using var request = new HttpRequestMessage(HttpMethod.Post, root + operation
            + "?ckAPIToken=" + Uri.EscapeDataString(apiToken) + "&ckWebAuthToken=" + Uri.EscapeDataString(token))
        {
            Content = JsonContent.Create(body, options: new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })
        };
        using var response = client.Send(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("CloudKit isteği başarısız.", null, response.StatusCode);
        var result = JsonDocument.Parse(response.Content.ReadAsStream());
        try { Check(result.RootElement); return result; }
        catch { result.Dispose(); throw; }
    }

    private static bool Check(JsonElement result, bool allowMissing = false)
    {
        if (!result.TryGetProperty("serverErrorCode", out var code)) return true;
        if (allowMissing && code.GetString() == "NOT_FOUND") return false;
        throw new IOException(code.GetString() == "AUTHENTICATION_REQUIRED" ? "CloudKit oturumu yenilenmeli." : "CloudKit kayıt işlemi başarısız.");
    }
}
