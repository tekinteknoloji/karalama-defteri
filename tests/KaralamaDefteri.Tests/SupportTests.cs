using System.Text.Json;
using KaralamaDefteri;
using Xunit;

public sealed class SupportTests
{
    [Theory]
    [InlineData(null, "github.com", false)]
    [InlineData("", "github.com", false)]
    [InlineData("https://github.com/sponsors/tekinteknoloji", "github.com", true)]
    [InlineData("http://github.com", "github.com", false)]
    [InlineData("https://github.com.example.org", "github.com", false)]
    [InlineData("https://outside@github.com", "github.com", false)]
    [InlineData("https://github.com:8443", "github.com", false)]
    [InlineData("https://pay.example.org", null, false)]
    [InlineData("https://pay.example.org", "pay.example.org", true)]
    public void LinksRequireConfiguredHttpsHost(string? value, string? host, bool accepted) =>
        Assert.Equal(accepted, SupportPolicy.Link(value, host) != null);

    [Theory]
    [InlineData(2592000, 101, false, false)]
    [InlineData(2592001, 100, false, false)]
    [InlineData(2592001, 101, true, false)]
    [InlineData(2592001, 101, false, true)]
    public void BothThresholdsMustBeExceeded(long age, long notes, bool shown, bool expected) =>
        Assert.Equal(expected, SupportPolicy.Eligible(100, 100 + age, notes, shown));

    [Fact]
    public void ManifestReadsSupportKeys()
    {
        const string json = """{"support":{"github_sponsors_url":"https://github.com/sponsors/example","local_payment_url":"","label_tr":"Destek"}}""";
        var support = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Support!;
        Assert.Equal("https://github.com/sponsors/example", support.Sponsors);
        Assert.Equal("", support.Payment);
        Assert.Equal("Destek", support.Label);
    }
}
