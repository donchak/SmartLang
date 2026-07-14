using System.Net;
using System.Text;

namespace SmartLang.Tests;

public sealed class ReleaseUpdateCheckerTests {
    [Fact]
    public async Task NewerReleaseIsReturnedWithItsGitHubPage() {
        var handler = new StubHttpMessageHandler("""
            {
              "tag_name": "v1.2.4",
              "html_url": "https://github.com/donchak/SmartLang/releases/tag/v1.2.4"
            }
            """);
        using var httpClient = new HttpClient(handler);
        var checker = new ReleaseUpdateChecker(httpClient);

        var update = await checker.CheckAsync("1.2.3");

        Assert.NotNull(update);
        Assert.Equal("1.2.4", update.Version);
        Assert.Equal(
            "https://github.com/donchak/SmartLang/releases/tag/v1.2.4",
            update.ReleasePage.AbsoluteUri);
        Assert.Equal("SmartLang-UpdateChecker", handler.Request!.Headers.UserAgent.ToString());
        Assert.Equal("application/vnd.github+json", handler.Request.Headers.Accept.ToString());
        Assert.Equal(
            "2022-11-28",
            Assert.Single(handler.Request.Headers.GetValues("X-GitHub-Api-Version")));
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.2")]
    public async Task CurrentOrOlderReleaseIsIgnored(string releaseVersion) {
        using var httpClient = CreateClient(releaseVersion);
        var checker = new ReleaseUpdateChecker(httpClient);

        var update = await checker.CheckAsync("1.2.3");

        Assert.Null(update);
    }

    [Theory]
    [InlineData("not-a-version", "https://github.com/donchak/SmartLang/releases/tag/not-a-version")]
    [InlineData("1.2.4", "http://github.com/donchak/SmartLang/releases/tag/v1.2.4")]
    [InlineData("1.2.4", "https://example.com/releases/tag/v1.2.4")]
    public async Task InvalidReleaseMetadataIsIgnored(string releaseVersion, string releaseUrl) {
        using var httpClient = CreateClient(releaseVersion, releaseUrl);
        var checker = new ReleaseUpdateChecker(httpClient);

        var update = await checker.CheckAsync("1.2.3");

        Assert.Null(update);
    }

    [Fact]
    public async Task InvalidInstalledVersionIsRejected() {
        using var httpClient = CreateClient("1.2.4");
        var checker = new ReleaseUpdateChecker(httpClient);

        await Assert.ThrowsAsync<InvalidDataException>(() => checker.CheckAsync("invalid"));
    }

    static HttpClient CreateClient(
        string version,
        string? url = null) => new(new StubHttpMessageHandler($$"""
            {
              "tag_name": "{{version}}",
              "html_url": "{{url ?? $"https://github.com/donchak/SmartLang/releases/tag/v{version}"}}"
            }
            """));

    sealed class StubHttpMessageHandler(string responseContent): HttpMessageHandler {
        internal HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            });
        }
    }
}
