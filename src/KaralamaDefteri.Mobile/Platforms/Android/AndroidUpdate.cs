using Android.Content;
using Android.Content.PM;
using System.Text.Json;

namespace KaralamaDefteri.Mobile;

internal static partial class AndroidUpdate
{
    internal const string AssetName = "KaralamaDefteri-Android.apk";
    private const string Releases = "https://github.com/tekinteknoloji/karalama-defteri/releases/download/";
    private const string Api = "https://api.github.com/repos/tekinteknoloji/karalama-defteri/releases";
    private static readonly HttpClient Http = CreateHttp();
    private static readonly UpdateDownload Download = new(Http);

    private static HttpClient CreateHttp()
    {
        HttpClient? client = null;
        ConfigureTest(ref client);
        var result = client ?? new HttpClient();
        result.DefaultRequestHeaders.UserAgent.ParseAdd("KaralamaDefteri-Android");
        return result;
    }

    static partial void ConfigureTest(ref HttpClient? client);

    internal static async Task<UpdateManifest?> Check(CancellationToken cancellation)
    {
        string? best = null;
        var bestVersion = AppInfo.Current.VersionString;
        var uri = new Uri(Api + "?per_page=100");
        while (true)
        {
            using var response = await Http.GetAsync(uri, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path) != Api)
                throw new InvalidDataException("Yayın adresi değişti.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
            using var releases = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
            foreach (var release in releases.RootElement.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
                var tag = release.GetProperty("tag_name").GetString();
                if (tag?.StartsWith("android-v", StringComparison.Ordinal) != true) continue;
                var version = tag[9..];
                if (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0 ||
                    parsed.ToString(3) != version || UpdateDownload.Compare(version, bestVersion) <= 0) continue;
                best = tag;
                bestVersion = version;
            }
            if (!response.Headers.TryGetValues("Link", out var headers)) break;
            var next = string.Join(",", headers).Split(',').Select(part => part.Split(';', 2))
                .FirstOrDefault(parts => parts.Length == 2 && parts[1].Contains("rel=\"next\""));
            if (next is null) break;
            if (!Uri.TryCreate(next[0].Trim().Trim('<', '>'), UriKind.Absolute, out uri!) || uri.GetLeftPart(UriPartial.Path) != Api)
                throw new InvalidDataException("Yayın adresi geçersiz.");
        }
        if (best is null) return null;
        var root = Releases + best + "/";
        var update = await Download.CheckAsync(new Uri(root + "latest.json"), AppInfo.Current.VersionString, false, cancellation);
        if (update is not null && (update.Version != bestVersion || update.Url.AbsoluteUri != root + AssetName))
            throw new InvalidDataException("Android sürüm bilgisi yayınla eşleşmiyor.");
        return update;
    }

    internal static async Task<string> Fetch(UpdateManifest manifest, CancellationToken cancellation, IProgress<long> progress)
    {
        var directory = Path.Combine(FileSystem.CacheDirectory, "updates");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AssetName);
        try
        {
            await Download.DownloadAsync(manifest, path, cancellation, progress);
            await Task.Run(() => VerifyPackage(path, manifest.Version), cancellation);
            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private static void VerifyPackage(string path, string version)
    {
        var context = Android.App.Application.Context;
        var manager = context.PackageManager ?? throw new InvalidOperationException();
#pragma warning disable CA1422 // The legacy flags overload supports Android before API 33.
        var flags = OperatingSystem.IsAndroidVersionAtLeast(28) ? PackageInfoFlags.SigningCertificates : PackageInfoFlags.Signatures;
        var installed = manager.GetPackageInfo(context.PackageName!, flags) ?? throw new InvalidDataException();
        var next = manager.GetPackageArchiveInfo(path, flags) ?? throw new InvalidDataException("APK açılamadı.");
#pragma warning restore CA1422
        if (next.PackageName != context.PackageName || next.VersionName != version ||
            VersionCode(next) <= VersionCode(installed) || !Signers(installed).SequenceEqual(Signers(next)))
            throw new InvalidDataException("APK paketi, sürümü veya imzası eşleşmiyor.");
    }

    private static long VersionCode(PackageInfo info)
    {
#pragma warning disable CA1422
        return OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode;
#pragma warning restore CA1422
    }

    private static string[] Signers(PackageInfo info)
    {
#pragma warning disable CA1422
        var signatures = OperatingSystem.IsAndroidVersionAtLeast(28) ? info.SigningInfo?.GetApkContentsSigners() : info.Signatures;
#pragma warning restore CA1422
        if (signatures is not { Count: > 0 }) throw new InvalidDataException("APK imzası yok.");
        return signatures.Select(signature => Convert.ToHexString(signature.ToByteArray()!)).Order(StringComparer.Ordinal).ToArray();
    }

    internal static bool Install(string path)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException();
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && activity.PackageManager?.CanRequestPackageInstalls() != true)
        {
            using var settings = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
                Android.Net.Uri.Parse("package:" + activity.PackageName));
            activity.StartActivity(settings);
            return false;
        }
        using var file = new Java.IO.File(path);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, activity.PackageName + ".updates", file);
        using var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        activity.StartActivity(intent);
        return true;
    }
}
