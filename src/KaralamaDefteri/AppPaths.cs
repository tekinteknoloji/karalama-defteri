using System.Security.Cryptography;
using System.Text;

namespace KaralamaDefteri;

public static class AppPaths
{
    public static string Executable { get; } = Environment.ProcessPath ?? throw new InvalidOperationException("Exe yolu bulunamadı.");
    public static string Root { get; } = Path.GetDirectoryName(Executable)!;
    public static string Data { get; } = Path.Combine(Root, "Veri");
    public static string Database { get; } = Path.Combine(Data, "notes.db");
    public static string Updates { get; } = Path.Combine(Data, "Guncelleme");
    internal static string SupportConfiguration { get; } = Path.Combine(Root, "support.json");
    public static string CloudSession(bool google) => Path.Combine(Data, google ? "google.oauth" : "onedrive.oauth");
    internal static string GoogleOAuthConfiguration => Path.Combine(Data, "google-oauth.json");
    public static string Recovery { get; } = Path.Combine(Data, "recovery.key");
    public static string PendingRecovery { get; } = Path.Combine(Data, "recovery.pending");
    public static string HelloEnvelope { get; } = Path.Combine(Data, "hello.key");
    public static string EncryptionTransition { get; } = Path.Combine(Data, "encryption.pending");
    public static string LockedShortcuts { get; } = Path.Combine(Data, "locked-shortcuts.dat");
    public static string Backups { get; } = Path.Combine(Data, "Yedekler");
    public static string InstanceId { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Root.ToUpperInvariant())))[..24];
}
