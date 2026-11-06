using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tapedeck.Core;
using Tapedeck.Core.Services;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>
/// Cuts square covers out of the thumbnails of tracks that have no cover, one after another in the background:
/// tracks without a MusicBrainz match, matches without a cover in the Cover Art Archive, and declined matches.
/// A later Cover Art Archive cover replaces the cut one. Each new cover marks the tags as outdated, so
/// "Write tags" takes it to the file.
/// </summary>
public sealed class CoverQueue(ThumbnailCoverService cutter, ITrackRepository repository, ActivityService activity)
{
    // rows are refreshed in groups, so a large library does not rebuild the table for every cover
    private const int REPORT_EVERY = 25;

    // thumbnails that could not be cut are not tried again until the next start
    private readonly HashSet<long> failed = [];
    private bool running;
    private bool checkAgain;

    /// <summary>Tracks got a cover; their tags are outdated now.</summary>
    public event Action<IReadOnlyList<long>>? Covered;

    /// <summary>Starts cutting covers for all tracks without one, unless a run is going already.</summary>
    public void EnqueueMissing()
    {
        if (this.running)
        {
            this.checkAgain = true;
            return;
        }
        if (!cutter.IsAvailable)
            return;
        _ = this.RunAsync();
    }

    private async Task RunAsync()
    {
        this.running = true;
        try
        {
            do
            {
                this.checkAgain = false;
                var ids = (await repository.ListWithoutCoverAsync()).Where(id => !this.failed.Contains(id)).ToList();
                if (ids.Count > 0)
                    await this.CutAsync(ids);
            }
            while (this.checkAgain);
        }
        finally
        {
            this.running = false;
        }
    }

    private async Task CutAsync(List<long> ids)
    {
        var covered = new List<long>();
        var total = 0;
        using (var progress = activity.Begin(string.Format(Strings.Activity_CuttingCovers, 0, ids.Count)))
        {
            for (var i = 0; i < ids.Count; i++)
            {
                progress.Update(string.Format(Strings.Activity_CuttingCovers, i + 1, ids.Count));
                var id = ids[i];
                try
                {
                    // a MusicBrainz cover may have arrived meanwhile
                    if (await repository.GetAsync(id) is not { ThumbPath: { } thumb, CoverPath: null } track)
                        continue;
                    var cover = await cutter.CutAsync(thumb, track.VideoId);
                    // only if still without cover: the MusicBrainz queue runs at the same time
                    if (await repository.GetAsync(id) is { CoverPath: null })
                    {
                        await repository.SetCoverAsync(id, cover, "thumb");
                        covered.Add(id);
                        total++;
                    }
                }
                catch (InvalidOperationException)
                {
                    this.failed.Add(id);
                }

                if (covered.Count >= REPORT_EVERY)
                {
                    this.Covered?.Invoke(covered);
                    covered = [];
                }
            }
        }

        if (covered.Count > 0)
            this.Covered?.Invoke(covered);
        if (total > 0)
            activity.Confirm(string.Format(Strings.Confirm_CoversCut, total));
    }
}
