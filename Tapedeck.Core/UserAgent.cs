using System.Net.Http.Headers;
using System.Reflection;

namespace Tapedeck.Core;

/// <summary>How Tapedeck names itself to web services: <c>Tapedeck/1.2.3</c>.</summary>
public static class UserAgent
{
    public static readonly ProductInfoHeaderValue PRODUCT = new("Tapedeck", Version());

    /// <summary>
    /// The app's version with three parts. On a phone there is no entry assembly, and a version may have only two
    /// parts, where <see cref="System.Version.ToString(int)"/> with three would throw.
    /// </summary>
    private static string Version()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0);
        return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
