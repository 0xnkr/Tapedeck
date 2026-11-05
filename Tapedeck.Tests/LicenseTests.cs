using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Tapedeck.Tests;

/// <summary>
/// The credits in the app, the embedded license texts and THIRD-PARTY-NOTICES.md must not drift apart.
/// Reads the files from the repository, because the tests do not reference the app.
/// </summary>
public sealed partial class LicenseTests
{
    // the downloaded tools are credited in the app, but they are not part of Tapedeck.exe and not in the notices
    private static readonly string[] TOOL_LICENSES = ["gpl-3.0", "unlicense"];

    // the last argument of each Credit: a license text id or one of the constants MIT and APACHE
    [GeneratedRegex(@",\s*(?:""(?<id>[a-z0-9.\-]+)""|(?<constant>MIT|APACHE))\)[,;]?\s*$", RegexOptions.Multiline)]
    private static partial Regex LicenseTextArgument();

    // the path of this source file at compile time, so the repository is found wherever the tests are built to
    private static string RepoRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(sourceFile))
        ?? throw new DirectoryNotFoundException("The repository root could not be derived from the source path.");

    private static string LicensesDir => Path.Combine(RepoRoot(), "Tapedeck", "Licenses");

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static HashSet<string> UsedLicenseTexts()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Tapedeck", "Credits.cs"));
        return LicenseTextArgument().Matches(source)
            .Select(m => m.Groups["id"].Success ? m.Groups["id"].Value : m.Groups["constant"].Value == "MIT" ? "mit" : "apache-2.0")
            .ToHashSet();
    }

    [Fact]
    public void Every_credit_has_an_embedded_license_text()
    {
        var embedded = Directory.GetFiles(LicensesDir, "*.txt").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        // Tapedeck's own text is the LICENSE file in the repository root
        embedded.Add("tapedeck");

        var used = UsedLicenseTexts();

        Assert.True(used.Count > 10, "The credits could not be parsed.");
        Assert.Empty(used.Except(embedded));
        Assert.Empty(embedded.Except(used));
    }

    [Fact]
    public void Notices_contain_every_bundled_license_text()
    {
        var notices = Normalize(File.ReadAllText(Path.Combine(RepoRoot(), "THIRD-PARTY-NOTICES.md")));

        foreach (var file in Directory.GetFiles(LicensesDir, "*.txt"))
        {
            if (TOOL_LICENSES.Contains(Path.GetFileNameWithoutExtension(file)))
                continue;
            Assert.True(notices.Contains(Normalize(File.ReadAllText(file))), $"{Path.GetFileName(file)} is missing in THIRD-PARTY-NOTICES.md");
        }
    }
}
