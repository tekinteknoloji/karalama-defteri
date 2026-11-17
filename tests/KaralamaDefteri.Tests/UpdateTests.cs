using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using KaralamaDefteri;
using Xunit;

public sealed class UpdateTests
{
    [Fact]
    public async Task ManifestChannelsAndDownloadValidation()
    {
        byte[] payload = [1, 3, 5, 7];
        var manifest = new UpdateManifest("2.1.0-beta.10", new Uri("https://example.test/next.exe"),
            Convert.ToHexString(SHA256.HashData(payload)), payload.Length, "Deneme", "2.0.0");
        using var http = new HttpClient(new ReleaseServer(manifest, payload));
        var client = new UpdateDownload(http);
        var feed = new Uri("https://example.test/latest.json");
        Assert.Null(await client.CheckAsync(feed, "2.0.0", false, default));
        Assert.Null(await client.CheckAsync(feed, "1.9.0", true, default));
        Assert.Equal(manifest, await client.CheckAsync(feed, "2.1.0-beta.9", true, default));
        Assert.Null(await client.CheckAsync(feed, "2.1.0", true, default));

        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var target = Path.Combine(directory.FullName, "next.exe");
            await client.DownloadAsync(manifest, target, default);
            Assert.Equal(payload, File.ReadAllBytes(target));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.DownloadAsync(manifest with { Sha256 = new string('0', 64) }, target, default));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.DownloadAsync(manifest with { Size = 2 }, target, default));
            Assert.Equal(payload, File.ReadAllBytes(target));
            Assert.Single(directory.GetFiles());
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SuccessfulLaunchCommitsAndFailedLaunchRestores(bool healthy)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var executable = Path.Combine(directory.FullName, "KaralamaDefteri.exe");
            var staged = Path.Combine(directory.FullName, "next.exe");
            var database = Path.Combine(directory.CreateSubdirectory("Veri").FullName, "notes.db");
            File.WriteAllText(executable, "old");
            File.WriteAllText(staged, "new");
            File.WriteAllText(database, "notes");
            UpdateFiles.Install(executable, staged);
            Assert.Equal("new", File.ReadAllText(executable));
            Assert.Equal(!healthy, UpdateFiles.Recover(executable, healthy));
            Assert.Equal(healthy ? "new" : "old", File.ReadAllText(executable));
            Assert.Equal("notes", File.ReadAllText(database));
            Assert.False(File.Exists(UpdateFiles.Previous(executable)));
        }
        finally { directory.Delete(true); }
    }

    private sealed class ReleaseServer(UpdateManifest manifest, byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = request.RequestUri == manifest.Url ? new ByteArrayContent(payload) : JsonContent.Create(manifest)
            });
        }
    }
}
