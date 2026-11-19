using Tapedeck.Core.Updates;

namespace Tapedeck.Tests;

public sealed class UpdateCheckerTests
{
    // shortened from GitHub's /releases answer; newest first, as GitHub sends it
    private const string RELEASES = """
        [
          { "tag_name": "v1.6.0", "draft": true, "prerelease": false, "html_url": "https://github.com/0xnkr/Tapedeck/releases/tag/v1.6.0", "assets": [] },
          { "tag_name": "v1.5.0-beta.2", "draft": false, "prerelease": true, "html_url": "https://github.com/0xnkr/Tapedeck/releases/tag/v1.5.0-beta.2",
            "assets": [ { "name": "Tapedeck.exe", "browser_download_url": "https://example.org/Tapedeck.exe" },
                        { "name": "Tapedeck-1.5.0-beta.2.apk", "browser_download_url": "https://example.org/Tapedeck-1.5.0-beta.2.apk" } ] },
          { "tag_name": "v1.5.0-beta.1", "draft": false, "prerelease": true, "html_url": "https://github.com/0xnkr/Tapedeck/releases/tag/v1.5.0-beta.1", "assets": [] },
          { "tag_name": "v1.4.1", "draft": false, "prerelease": false, "html_url": "https://github.com/0xnkr/Tapedeck/releases/tag/v1.4.1",
            "assets": [ { "name": "Tapedeck.exe", "browser_download_url": "https://example.org/Tapedeck.exe" } ] },
          { "tag_name": "v1.4.0", "draft": false, "prerelease": false, "html_url": "https://github.com/0xnkr/Tapedeck/releases/tag/v1.4.0", "assets": [] }
        ]
        """;

    private static ReleaseVersion Version(string text) =>
        ReleaseVersion.TryParse(text, out var version) ? version : throw new ArgumentException(text);

    [Fact]
    public void A_beta_hears_of_the_next_beta_with_its_apk()
    {
        var update = UpdateChecker.Pick(RELEASES, Version("1.5.0-beta.1"));

        Assert.NotNull(update);
        Assert.Equal("1.5.0-beta.2", update.Version);
        Assert.Equal("https://example.org/Tapedeck-1.5.0-beta.2.apk", update.ApkUrl);
        Assert.Equal("https://github.com/0xnkr/Tapedeck/releases/tag/v1.5.0-beta.2", update.PageUrl);
    }

    [Fact]
    public void A_final_version_hears_only_of_final_versions_and_never_of_drafts()
    {
        var update = UpdateChecker.Pick(RELEASES, Version("1.4.0"));

        Assert.NotNull(update);
        Assert.Equal("1.4.1", update.Version);
        Assert.Null(update.ApkUrl);
        Assert.Null(UpdateChecker.Pick(RELEASES, Version("1.4.1")));
    }

    [Fact]
    public void The_newest_version_is_up_to_date()
    {
        Assert.Null(UpdateChecker.Pick(RELEASES, Version("1.5.0-beta.2")));
        Assert.Null(UpdateChecker.Pick(RELEASES, Version("1.5.0")));
    }

    [Theory]
    [InlineData("1.5.0-beta.1", "1.5.0-beta.2")]
    [InlineData("1.5.0-beta.2", "1.5.0-beta.10")]
    [InlineData("1.5.0-beta.9", "1.5.0")]
    [InlineData("1.5.0-alpha.1", "1.5.0-beta.1")]
    [InlineData("1.4.9", "1.5.0-beta.1")]
    [InlineData("1.9.0", "1.10.0")]
    public void Versions_are_ordered_like_semantic_versioning(string older, string newer)
    {
        Assert.True(Version(older).CompareTo(Version(newer)) < 0);
        Assert.True(Version(newer).CompareTo(Version(older)) > 0);
    }

    [Theory]
    [InlineData("0.0.0-dev+2e02d19", true)]
    [InlineData("1.5.0+2e02d19", false)]
    public void Local_builds_are_recognized(string text, bool local)
    {
        Assert.Equal(local, Version(text).IsLocalBuild);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1.5.0-")]
    [InlineData("v1.5.0")]
    [InlineData("1.5.x")]
    public void Other_texts_are_no_versions(string text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }
}
