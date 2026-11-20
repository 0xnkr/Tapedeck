namespace Tapedeck.Core.Apple;

/// <summary>A copy in the Apple folder and the state of the library file it was made from.</summary>
/// <param name="FilePath">Relative to the Apple folder.</param>
/// <param name="SourcePath">The library file (<c>tracks.file_path</c>) at the time of the export.</param>
public sealed record AppleExportRecord(
    long TrackId,
    string FilePath,
    string SourcePath,
    string? SourceDownloadedAt,
    string? SourceEditedAt);

/// <summary>A downloaded track whose copy is missing or outdated.</summary>
/// <param name="Existing">The current copy, <c>null</c> if there is none yet.</param>
public sealed record AppleExportCandidate(
    long TrackId,
    string SourcePath,
    string? DownloadedAt,
    string? EditedAt,
    AppleExportRecord? Existing);

/// <summary>How far the Apple folder is: copies that are up to date out of all downloaded tracks.</summary>
public sealed record AppleExportCounts(int Current, int Total)
{
    public int Pending => this.Total - this.Current;
}

public interface IAppleExportRepository
{
    /// <summary>Downloaded tracks without a copy, or whose file, download or values changed since.</summary>
    Task<IReadOnlyList<AppleExportCandidate>> ListPendingAsync();

    /// <summary>Copies whose track no longer exists or has no file any more.</summary>
    Task<IReadOnlyList<AppleExportRecord>> ListOrphansAsync();

    /// <summary>All copies by track id.</summary>
    Task<IReadOnlyDictionary<long, AppleExportRecord>> ListAsync();

    Task<AppleExportRecord?> GetAsync(long trackId);

    /// <summary>Whether the track's copy matches its library file, download and values.</summary>
    Task<bool> IsCurrentAsync(long trackId);

    Task SaveAsync(AppleExportRecord record);

    Task DeleteAsync(long trackId);

    /// <summary>Forgets all copies, e.g. after the Apple folder changed.</summary>
    Task ClearAsync();

    Task<AppleExportCounts> CountAsync();
}
