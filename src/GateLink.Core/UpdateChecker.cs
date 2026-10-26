using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GateLink.Core;

/// <summary>
/// Checks GitHub Releases for a newer version of GateLink. Returns the latest
/// release metadata so the caller can decide whether to prompt/download.
/// </summary>
public static class UpdateChecker
{
    /// <summary>
    /// Current app version, read from the assembly at runtime. CI sets this
    /// automatically from the git tag (e.g. v0.1.3 → AssemblyVersion 0.1.3).
    /// Local dev builds default to 0.0.0 which is always "older" than any release.
    /// </summary>
    public static readonly Version CurrentVersion =
        typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private const string ReleasesApi =
        "https://api.github.com/repos/abdullahPrasetio/gatelink/releases/latest";

    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GateLink-UpdateChecker/1.0");
            http.Timeout = TimeSpan.FromSeconds(10);

            var release = await http.GetFromJsonAsync<GitHubRelease>(ReleasesApi, ct);
            if (release is null || string.IsNullOrEmpty(release.TagName)) return null;

            var tag = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var remote)) return null;

            return new ReleaseInfo
            {
                Version = remote,
                TagName = release.TagName,
                IsNewer = remote > CurrentVersion,
                HtmlUrl = release.HtmlUrl ?? "",
                MacAssetUrl = FindAsset(release, "GateLink.dmg", "macos"),
                WindowsAssetUrl = FindAsset(release, "Setup.exe", "windows"),
            };
        }
        catch
        {
            return null; // network error, GitHub down, etc. — silently ignore
        }
    }

    private static string? FindAsset(GitHubRelease r, string contains1, string contains2)
    {
        if (r.Assets is null) return null;
        foreach (var a in r.Assets)
        {
            if (a.BrowserDownloadUrl is null) continue;
            var name = a.Name ?? "";
            if (name.Contains(contains1, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(contains2, StringComparison.OrdinalIgnoreCase))
                return a.BrowserDownloadUrl;
        }
        return null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

public sealed record ReleaseInfo
{
    public required Version Version { get; init; }
    public required string TagName { get; init; }
    public required bool IsNewer { get; init; }
    public required string HtmlUrl { get; init; }
    public string? MacAssetUrl { get; init; }
    public string? WindowsAssetUrl { get; init; }
}
