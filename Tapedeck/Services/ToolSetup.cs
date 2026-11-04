using System.Threading.Tasks;
using Tapedeck.Core.Services;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>Installs yt-dlp, ffmpeg and Deno once on first use, with the activity toast; shared by all pages that call yt-dlp.</summary>
public sealed class ToolSetup(ToolService tools, ActivityService activity)
{
    private Task? ready;

    public async Task EnsureAsync()
    {
        try
        {
            if (tools.HasAllTools)
                return;
            using (activity.Begin(Strings.Activity_InstallingTools))
                await (this.ready ??= tools.EnsureToolsAsync());
        }
        catch
        {
            this.ready = null;
            throw;
        }
    }
}
