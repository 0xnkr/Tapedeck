using Tapedeck.Core.Services;
using Tapedeck.Core.Settings;

namespace Tapedeck.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), $"tapedeck-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.dir))
            Directory.Delete(this.dir, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults_and_saved_values_come_back()
    {
        var store = new SettingsStore(this.dir);
        Assert.Equal(new AppSettings(), store.Load());

        store.Update(s => s with { Language = "de", Accent = "#C8B6FF" });
        Assert.Equal(new AppSettings("de", "#C8B6FF"), new SettingsStore(this.dir).Load());
    }

    [Fact]
    public void Broken_file_gives_defaults()
    {
        Directory.CreateDirectory(this.dir);
        var store = new SettingsStore(this.dir);
        File.WriteAllText(store.FilePath, "{ not json");
        Assert.Equal(new AppSettings(), store.Load());
    }

    [Theory]
    [InlineData("2025.10.22", "2025.10.22")]
    [InlineData("ffmpeg version 7.1-full_build-www.gyan.dev Copyright (c) 2000-2024", "7.1-full_build-www.gyan.dev")]
    [InlineData("deno 2.1.4 (stable, release, x86_64-pc-windows-msvc)", "2.1.4")]
    public void Tool_versions_are_shortened(string line, string expected)
    {
        Assert.Equal(expected, ToolService.ShortVersion(line));
    }
}
