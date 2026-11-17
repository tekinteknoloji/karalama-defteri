using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KaralamaDefteri;

internal sealed record OAuthConfiguration(Uri Authorize, Uri Token, string ClientId, string Scope, bool Google, string? DesktopSecret = null);
internal sealed record OAuthTokens(string Access, string Refresh, DateTimeOffset Expires);

internal sealed class DesktopOAuth(HttpClient http, OAuthConfiguration settings)
{
    internal async Task<OAuthTokens> LoginAsync(Action<Uri> openBrowser, CancellationToken cancellation)
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var redirect = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/oauth/";
        var state = RandomToken();
        var verifier = RandomToken();
        var query = new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId, ["response_type"] = "code", ["scope"] = settings.Scope,
            ["redirect_uri"] = redirect, ["state"] = state,
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"] = "S256"
        };
        if (settings.Google) { query["access_type"] = "offline"; query["prompt"] = "consent"; }
        openBrowser(new UriBuilder(settings.Authorize) { Query = string.Join("&", query.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))) }.Uri);
        while (true)
        {
            using var socket = await server.AcceptTcpClientAsync(cancellation);
            using var stream = socket.GetStream();
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            string? line;
            using var reader = new StreamReader(stream, leaveOpen: true);
            try { line = await reader.ReadLineAsync(readTimeout.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { continue; }
            var request = line?.Split(' ', 3);
            var values = request is ["GET", var path, _] && path.StartsWith("/oauth/?", StringComparison.Ordinal)
                ? System.Web.HttpUtility.ParseQueryString(path[8..]) : null;
            var valid = values?["state"] == state;
            var code = valid ? values?["code"] : null;
            var message = code == null ? "Giris tamamlanamadi." : "Karalama Defteri'ne donebilirsiniz.";
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {(code == null ? "400 Bad Request" : "200 OK")}\r\nContent-Type: text/plain\r\nContent-Length: {message.Length}\r\nConnection: close\r\n\r\n{message}");
            await stream.WriteAsync(response, cancellation);
            if (!valid) continue;
            if (string.IsNullOrEmpty(code)) throw new InvalidDataException("OAuth girişi reddedildi.");
            return await ExchangeAsync(new()
            {
                ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier
            }, null, cancellation);
        }
    }

    internal Task<OAuthTokens> RefreshAsync(string token, CancellationToken cancellation) =>
        ExchangeAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = token }, token, cancellation);

    private async Task<OAuthTokens> ExchangeAsync(Dictionary<string, string> fields, string? previous, CancellationToken cancellation)
    {
        fields["client_id"] = settings.ClientId;
        if (settings.DesktopSecret != null) fields["client_secret"] = settings.DesktopSecret;
        using var response = await http.PostAsync(settings.Token, new FormUrlEncodedContent(fields), cancellation);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellation));
        var root = json.RootElement;
        var access = root.GetProperty("access_token").GetString();
        var refresh = root.TryGetProperty("refresh_token", out var value) ? value.GetString() : previous;
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
            throw new InvalidDataException("OAuth belirteci veya yenileme yetkisi eksik.");
        return new(access, refresh, DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
