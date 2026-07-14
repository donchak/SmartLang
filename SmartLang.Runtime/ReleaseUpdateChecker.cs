using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SmartLang;

internal sealed record ReleaseUpdate(string Version, Uri ReleasePage);

internal sealed class ReleaseUpdateChecker {
    static readonly Uri LatestReleaseEndpoint = new(
        "https://api.github.com/repos/donchak/SmartLang/releases/latest");

    readonly HttpClient httpClient;

    internal ReleaseUpdateChecker(HttpClient httpClient) {
        this.httpClient = httpClient;
    }

    internal async Task<ReleaseUpdate?> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default) {
        if(!TryParseVersion(currentVersion, out var installedVersion)) {
            throw new InvalidDataException($"The installed version '{currentVersion}' is invalid.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("SmartLang-UpdateChecker");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken);
        if(release is null ||
            !TryParseVersion(release.TagName, out var releaseVersion) ||
            releaseVersion <= installedVersion ||
            !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releasePage) ||
            releasePage.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(releasePage.Host, "github.com", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return new ReleaseUpdate(releaseVersion.ToString(), releasePage);
    }

    static bool TryParseVersion(string value, out Version version) {
        var candidate = value.Trim();
        if(candidate.StartsWith('v') || candidate.StartsWith('V')) {
            candidate = candidate[1..];
        }

        return Version.TryParse(candidate, out version!);
    }

    sealed class GitHubRelease {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;
    }
}
