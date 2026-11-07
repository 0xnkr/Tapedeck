using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Services;

namespace Tapedeck.Core.Csv;

/// <summary>A CSV row that could not be imported.</summary>
public sealed record CsvImportError(int Line, string Message);

/// <param name="TrackIds">The tracks of the imported rows, in file order.</param>
public sealed record CsvImportResult(IReadOnlyList<long> TrackIds, IReadOnlyList<CsvImportError> Errors);

/// <summary>
/// Adds the tracks of a CSV file to the library and applies its corrections, genres and ratings.
/// Known tracks are not fetched again; rows that fail are reported and skipped.
/// </summary>
public sealed class CsvImportService(
    ITrackRepository repository,
    TrackImportService importer,
    ILogger<CsvImportService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    /// <param name="progress">Number of rows handled so far.</param>
    public async Task<CsvImportResult> ImportAsync(IReadOnlyList<CsvTrack> rows, IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var known = (await repository.ListDisplayAsync()).ToDictionary(t => t.VideoId, t => t.Id);
        foreach (var (videoId, trackId) in await repository.ListAliasesAsync())
            known.TryAdd(videoId, trackId);
        var ids = new List<long>();
        var errors = new List<CsvImportError>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            progress?.Report(i + 1);
            try
            {
                if (!known.TryGetValue(row.VideoId, out var id))
                {
                    id = await importer.AddAsync(row.Url, ct);
                    known[row.VideoId] = id;
                }

                if (await repository.GetDisplayAsync(id) is { } current)
                {
                    var corrections = LibraryCsv.Corrections(row, current);
                    if (corrections.Fields is { } fields)
                        await repository.SetUserFieldsAsync(id, fields);
                    if (corrections.Genres is { } genres)
                        await repository.SetUserTagsAsync(id, genres);
                    if (corrections.Rating is { } rating)
                        await repository.SetRatingAsync(id, rating);
                }
                ids.Add(id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new CsvImportError(row.Line, ex.Message));
            }
        }

        this.log.LogInformation("CSV import: {Rows} rows, {Imported} imported, {Failed} failed", rows.Count, ids.Count, errors.Count);
        foreach (var error in errors)
            this.log.LogWarning("CSV line {Line} skipped: {Error}", error.Line, error.Message);
        return new CsvImportResult(ids, errors);
    }
}
