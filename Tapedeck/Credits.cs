using System.Collections.Generic;
using Tapedeck.Resources;

namespace Tapedeck;

/// <summary>An open source project Tapedeck uses, shown in the settings.</summary>
public sealed record Credit(string Name, string Purpose, string License, string Url);

/// <summary>
/// The open source projects Tapedeck builds on: bundled tools first, then libraries and the typeface.
/// Licenses as published by the projects (NuGet metadata for the packages).
/// </summary>
public static class Credits
{
    public static IReadOnlyList<Credit> ALL =>
    [
        new("yt-dlp", Strings.Credit_YtDlp, "Unlicense", "https://github.com/yt-dlp/yt-dlp"),
        new("FFmpeg", Strings.Credit_FFmpeg, "LGPL / GPL", "https://ffmpeg.org"),
        new("Deno", Strings.Credit_Deno, "MIT", "https://deno.com"),
        new("Avalonia", Strings.Credit_Avalonia, "MIT", "https://avaloniaui.net"),
        new("YoutubeDLSharp", Strings.Credit_YoutubeDLSharp, "BSD-3-Clause", "https://github.com/Bluegrams/YoutubeDLSharp"),
        new("TagLibSharp", Strings.Credit_TagLibSharp, "LGPL-2.1", "https://github.com/mono/taglib-sharp"),
        new("SQLite", Strings.Credit_SQLite, "Public Domain", "https://sqlite.org"),
        new("Microsoft.Data.Sqlite", Strings.Credit_MicrosoftDataSqlite, "MIT", "https://learn.microsoft.com/dotnet/standard/data/sqlite/"),
        new("Dapper", Strings.Credit_Dapper, "Apache-2.0", "https://github.com/DapperLib/Dapper"),
        new("CommunityToolkit.Mvvm", Strings.Credit_CommunityToolkit, "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new(".NET", Strings.Credit_DotNet, "MIT", "https://dot.net"),
        new("Inter", Strings.Credit_Inter, "OFL-1.1", "https://rsms.me/inter/"),
    ];
}
