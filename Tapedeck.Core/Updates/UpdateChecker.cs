using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tapedeck.Core.Updates;

/// <summary>A newer release on GitHub, with the downloads it offers.</summary>
/// <param name="Version">The version without the tag's <c>v</c>, e.g. <c>1.5.0</c>.</param>
/// <param name="PageUrl">The release page with the notes.</param>
/// <param name="ApkUrl">The Android app of the release, if it has one.</param>
public sealed record AvailableUpdate(string Version, string PageUrl, string? ApkUrl);

/// <summary>
/// Asks GitHub whether a newer Tapedeck was released. There is no app store to tell the user, and the phone app is
/// installed by hand. Sends nothing but the request itself; a local build (<c>0.0.0-dev</c>) never asks.
/// </summary>
public sealed class UpdateChecker(HttpClient http, ILogger<UpdateChecker>? logger = null)
{
    private const string RELEASES_URL = "https://api.github.com/repos/0xnkr/Tapedeck/releases?per_page=20";

    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    /// <returns>
    /// The newest release above <paramref name="currentVersion"/>; <c>null</c> when there is none or GitHub cannot be
    /// asked right now, which is no reason to bother the user.
    /// </returns>
    public async Task<AvailableUpdate?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var current) || current.IsLocalBuild)
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, RELEASES_URL);
            request.Headers.UserAgent.Add(UserAgent.PRODUCT);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var update = Pick(await response.Content.ReadAsStringAsync(ct), current);
            this.log.LogInformation("Update check: {Result}", update is null ? "up to date" : $"{update.Version} available");
            return update;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            this.log.LogInformation("Update check failed: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// The newest published release above <paramref name="current"/>. Pre-releases count only while a pre-release
    /// runs, so whoever tests a beta hears of the next one, and everyone else only of final versions.
    /// </summary>
    public static AvailableUpdate? Pick(string json, ReleaseVersion current)
    {
        if (JsonNode.Parse(json) is not JsonArray releases)
            return null;

        (ReleaseVersion Version, JsonObject Release)? best = null;
        foreach (var release in releases.OfType<JsonObject>())
        {
            if (Bool(release, "draft") || (Bool(release, "prerelease") && !current.IsPrerelease))
                continue;
            if (Str(release, "tag_name") is not { } tag || !ReleaseVersion.TryParse(tag.TrimStart('v'), out var version))
                continue;
            if (version.CompareTo(current) > 0 && (best is null || version.CompareTo(best.Value.Version) > 0))
                best = (version, release);
        }
        if (best is not { } found)
            return null;

        var apk = (found.Release["assets"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(a => Str(a, "name")?.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) == true);
        return new AvailableUpdate(found.Version.ToString(), Str(found.Release, "html_url") ?? string.Empty,
            apk is null ? null : Str(apk, "browser_download_url"));
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static bool Bool(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

/// <summary>A Tapedeck version like <c>1.5.0</c> or <c>1.5.0-beta.2</c>, ordered as Semantic Versioning orders them.</summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<ReleaseVersion>
{
    public bool IsPrerelease => this.Prerelease is not null;

    /// <summary>A build from a working copy, which has no release to compare with.</summary>
    public bool IsLocalBuild => this.Prerelease == "dev";

    public static bool TryParse(string text, out ReleaseVersion version)
    {
        version = null!;
        var core = text.Split('+')[0];
        var dash = core.IndexOf('-');
        var numbers = (dash < 0 ? core : core[..dash]).Split('.');
        if (numbers.Length != 3 || !numbers.All(n => int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return false;
        var pre = dash < 0 ? null : core[(dash + 1)..];
        if (pre is { Length: 0 })
            return false;
        version = new ReleaseVersion(int.Parse(numbers[0], CultureInfo.InvariantCulture),
            int.Parse(numbers[1], CultureInfo.InvariantCulture), int.Parse(numbers[2], CultureInfo.InvariantCulture), pre);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
            return 1;
        var byNumbers = (this.Major, this.Minor, this.Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (byNumbers != 0)
            return byNumbers;
        // a final version is newer than its pre-releases
        if (this.Prerelease is null || other.Prerelease is null)
            return (this.Prerelease is null).CompareTo(other.Prerelease is null);

        var mine = this.Prerelease.Split('.');
        var theirs = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            var bothNumbers = int.TryParse(mine[i], out var a) & int.TryParse(theirs[i], out var b);
            var part = bothNumbers ? a.CompareTo(b) : string.CompareOrdinal(mine[i], theirs[i]);
            if (part != 0)
                return part;
        }
        return mine.Length.CompareTo(theirs.Length);
    }

    public override string ToString() =>
        this.Prerelease is null ? $"{this.Major}.{this.Minor}.{this.Patch}" : $"{this.Major}.{this.Minor}.{this.Patch}-{this.Prerelease}";
}
