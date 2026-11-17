using System.Text.Json.Serialization;

namespace KaralamaDefteri;

internal sealed record SupportOptions(
    [property: JsonPropertyName("github_sponsors_url")] string? Sponsors = null,
    [property: JsonPropertyName("local_payment_url")] string? Payment = null,
    [property: JsonPropertyName("label_tr")] string? Label = null);

internal sealed record SupportConfiguration(SupportOptions? Support);

internal static class SupportPolicy
{
    internal static Uri? Link(string? value, string? allowedHost)
    {
        if (string.IsNullOrWhiteSpace(allowedHost) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            !uri.IdnHost.Equals(allowedHost, StringComparison.OrdinalIgnoreCase)) return null;
        return uri;
    }

    internal static bool Eligible(long firstUse, long now, long notes, bool shown) =>
        !shown && firstUse > 0 && now >= firstUse && now - firstUse > 30 * 86400 && notes > 100;
}
