using System.Globalization;
using System.Reflection;

namespace KaralamaDefteri;

internal static class BuildInfo
{
    private static readonly Assembly Assembly = typeof(BuildInfo).Assembly;
    public static string Version { get; } = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    public static DateTimeOffset BuiltAt { get; } = DateTimeOffset.ParseExact(
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "BuildTimeUtc").Value!,
        "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    public static string Summary => $"v{Version} · {BuiltAt.ToLocalTime():dd.MM.yyyy HH:mm}";
    public static string ClipboardText => $"Karalama Defteri {Version} | Derleme: {BuiltAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC";
}
