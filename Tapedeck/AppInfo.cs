using System.Reflection;

namespace Tapedeck;

/// <summary>
/// Version and build of the running app. A release gets its version from the git tag (release.yml passes
/// <c>-p:Version</c>), local builds are <c>0.0.0-dev</c> (Tapedeck.csproj). The .NET SDK appends the git commit
/// to the informational version (<c>1.0.0+2e02d19…</c>), so every build can be traced to its source.
/// </summary>
public static class AppInfo
{
    private const int SHORT_COMMIT = 7;

    private static readonly string INFORMATIONAL =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>The version without the commit, e.g. <c>1.0.0</c> or <c>0.0.0-dev</c>.</summary>
    public static readonly string VERSION = INFORMATIONAL.Split('+')[0];

    /// <summary>The short git commit the app was built from; <c>null</c> when built outside a repository.</summary>
    public static readonly string? COMMIT = INFORMATIONAL.Split('+') is [_, var commit, ..] && commit.Length > 0
        ? commit[..System.Math.Min(SHORT_COMMIT, commit.Length)]
        : null;

    /// <summary>For the settings and the log: <c>1.0.0 · 2e02d19</c>.</summary>
    public static readonly string DISPLAY = COMMIT is null ? VERSION : $"{VERSION} · {COMMIT}";
}
