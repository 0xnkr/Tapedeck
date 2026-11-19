using System.Threading.Tasks;
using Tapedeck.Core.Services;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>Gets yt-dlp, ffmpeg and the JS runtime ready once on first use, with the activity toast; shared by all pages that call yt-dlp.</summary>
public sealed class ToolSetup(IToolHost tools, ActivityService activity)
{
    private Task? ready;

    public async Task EnsureAsync()
    {
        try
        {
            if (tools.IsReady)
                return;
            using (activity.Begin(Strings.Activity_InstallingTools))
                await (this.ready ??= tools.SetupAsync());
        }
        catch
        {
            this.ready = null;
            throw;
        }
    }
}
