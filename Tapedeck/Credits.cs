using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Tapedeck.Resources;

namespace Tapedeck;

/// <summary>
/// An open source project Tapedeck uses, shown in the settings. <see cref="LicenseText"/> names the license text
/// embedded from <c>Licenses/</c>, so it can be read offline.
/// </summary>
public sealed record Credit(string Name, string Purpose, string License, string Url, string? Copyright, string LicenseText);

/// <summary>
/// Tapedeck's own license and the open source projects it builds on: the downloaded tools first, then the libraries and
/// the typeface, then the libraries that come along with those. Licenses and copyright notices as published by the
/// projects (NuGet metadata for the packages); THIRD-PARTY-NOTICES.md lists the same.
/// </summary>
public static class Credits
{
    private const string MIT = "mit";
    private const string APACHE = "apache-2.0";

    public static Credit TAPEDECK => new("Tapedeck", Strings.Credit_Tapedeck, "MIT", "https://github.com/nilskruthoff/Tapedeck",
        "Copyright (c) 2026 Nils Kruthoff", "tapedeck");

    public static IReadOnlyList<Credit> ALL =>
    [
        new("yt-dlp", Strings.Credit_YtDlp, "Unlicense", "https://github.com/yt-dlp/yt-dlp", null, "unlicense"),
        new("FFmpeg", Strings.Credit_FFmpeg, "LGPL / GPL", "https://ffmpeg.org",
            "Copyright (c) 2000-2023 the FFmpeg developers", "gpl-3.0"),
        new("Deno", Strings.Credit_Deno, "MIT", "https://deno.com", "Copyright 2018-2026 the Deno authors", MIT),
        new("Avalonia", Strings.Credit_Avalonia, "MIT", "https://avaloniaui.net",
            "Copyright 2013-2026 © The AvaloniaUI Project", MIT),
        new("YoutubeDLSharp", Strings.Credit_YoutubeDLSharp, "BSD-3-Clause", "https://github.com/Bluegrams/YoutubeDLSharp",
            "Copyright (c) 2020-2024, Bluegrams", "bsd-3-clause-youtubedlsharp"),
        new("TagLibSharp", Strings.Credit_TagLibSharp, "LGPL-2.1", "https://github.com/mono/taglib-sharp",
            "Copyright (c) 2006-2007 Brian Nickel, Copyright (c) 2009-2020 other contributors", "lgpl-2.1"),
        new("Concentus", Strings.Credit_Concentus, "BSD-3-Clause", "https://github.com/lostromb/concentus",
            "© Xiph.Org Foundation, Skype Limited, CSIRO, Microsoft Corp., Logan Stromberg", "bsd-3-clause-concentus"),
        new("Concentus.OggFile", Strings.Credit_ConcentusOggFile, "MIT", "https://github.com/lostromb/concentus.oggfile",
            "Copyright © Logan Stromberg, Andrew Ward", MIT),
        new("OpenAL Soft", Strings.Credit_OpenAlSoft, "LGPL-2.0-or-later", "https://openal-soft.org",
            "Copyright (C) 1999-2000 Loki Software, OpenAL Soft authors", "lgpl-2.1"),
        new("Silk.NET", Strings.Credit_SilkNet, "MIT", "https://github.com/dotnet/Silk.NET",
            "© .NET Foundation and Contributors", MIT),
        new("SQLite", Strings.Credit_SQLite, "Public Domain", "https://sqlite.org", null, "public-domain-sqlite"),
        new("Microsoft.Data.Sqlite", Strings.Credit_MicrosoftDataSqlite, "MIT", "https://learn.microsoft.com/dotnet/standard/data/sqlite/",
            "© Microsoft Corporation", MIT),
        new("Dapper", Strings.Credit_Dapper, "Apache-2.0", "https://github.com/DapperLib/Dapper",
            "Copyright 2019 Stack Exchange, Inc.", APACHE),
        new("CommunityToolkit.Mvvm", Strings.Credit_CommunityToolkit, "MIT", "https://github.com/CommunityToolkit/dotnet",
            "(c) .NET Foundation and Contributors", MIT),
        new(".NET", Strings.Credit_DotNet, "MIT", "https://dot.net", "© .NET Foundation and Contributors", MIT),
        new("Inter", Strings.Credit_Inter, "OFL-1.1", "https://rsms.me/inter/",
            "Copyright (c) 2016 The Inter Project Authors", "ofl-1.1-inter"),
    ];

    /// <summary>Libraries that come along with the ones above and are bundled into Tapedeck.exe as well.</summary>
    public static IReadOnlyList<Credit> ALSO_INCLUDED =>
    [
        new("SkiaSharp", Strings.Credit_SkiaSharp, "MIT", "https://github.com/mono/SkiaSharp", "© Microsoft Corporation", MIT),
        new("Skia", Strings.Credit_Skia, "BSD-3-Clause", "https://skia.org", "Copyright (c) 2011 Google Inc.", "bsd-3-clause-skia"),
        new("HarfBuzzSharp", Strings.Credit_HarfBuzzSharp, "MIT", "https://github.com/mono/SkiaSharp", "© Microsoft Corporation", MIT),
        new("HarfBuzz", Strings.Credit_HarfBuzz, "Old MIT", "https://harfbuzz.github.io", null, "old-mit-harfbuzz"),
        new("ANGLE", Strings.Credit_Angle, "BSD-3-Clause", "https://chromium.googlesource.com/angle/angle",
            "Copyright 2018 The ANGLE Project Authors", "bsd-3-clause-angle"),
        new("SQLitePCLRaw", Strings.Credit_SQLitePCLRaw, "Apache-2.0", "https://github.com/ericsink/SQLitePCL.raw",
            "Copyright 2014-2024 SourceGear, LLC", APACHE),
        new("Newtonsoft.Json", Strings.Credit_NewtonsoftJson, "MIT", "https://www.newtonsoft.com/json",
            "Copyright (c) 2007 James Newton-King", MIT),
        new("MicroCom.Runtime", Strings.Credit_MicroCom, "MIT", "https://github.com/kekekeks/MicroCom",
            "Copyright 2021 © Nikita Tsukanov", MIT),
        new("Tmds.DBus.Protocol", Strings.Credit_TmdsDBus, "MIT", "https://github.com/tmds/Tmds.DBus", "Copyright Tom Deseyn", MIT),
    ];

    /// <summary>Reads an embedded license text, off the UI thread.</summary>
    public static Task<string> ReadLicenseAsync(string licenseText) => Task.Run(() =>
    {
        var name = $"Tapedeck.Licenses.{licenseText}.txt";
        using var stream = typeof(Credits).Assembly.GetManifestResourceStream(name)
                           ?? throw new FileNotFoundException($"The license text '{name}' is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
