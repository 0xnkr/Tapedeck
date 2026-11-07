using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tapedeck.Core.Spotify;

/// <summary>
/// Reads Spotify's extended streaming history (<c>Streaming_History_Audio_*.json</c> and
/// <c>Streaming_History_Video_*.json</c>) from the folder or the zip Spotify sends.
/// The keys stayed the same since 2018, but some values changed meaning: <c>skipped</c> is always false until
/// 2022, <c>platform</c> was a long device string and <c>offline_timestamp</c> was in milliseconds. None of them
/// is read; neither are IP address and country.
/// </summary>
public sealed partial class SpotifyHistoryReader(ILogger<SpotifyHistoryReader>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [GeneratedRegex(@"^Streaming_History_(?:Audio|Video)_.*\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtendedFile();

    // the short history from the account data: one year, without track IDs and play reasons
    [GeneratedRegex(@"^StreamingHistory.*\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex AccountDataFile();

    /// <param name="path">A folder (searched with subfolders) or the zip file of the data download.</param>
    public async Task<SpotifyHistory> ReadAsync(string path, CancellationToken ct = default)
    {
        var accumulator = new Accumulator();
        if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = await ZipFile.OpenReadAsync(path, ct);
            var entries = zip.Entries.Where(e => ExtendedFile().IsMatch(e.Name)).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
            CheckFound(entries.Count, zip.Entries.Any(e => AccountDataFile().IsMatch(e.Name)), path);
            foreach (var entry in entries)
            {
                await using var stream = await entry.OpenAsync(ct);
                await accumulator.AddFileAsync(stream, ct);
            }
        }
        else if (Directory.Exists(path))
        {
            var all = Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories).ToList();
            var files = all.Where(f => ExtendedFile().IsMatch(Path.GetFileName(f))).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList();
            CheckFound(files.Count, all.Any(f => AccountDataFile().IsMatch(Path.GetFileName(f))), path);
            foreach (var file in files)
            {
                await using var stream = File.OpenRead(file);
                await accumulator.AddFileAsync(stream, ct);
            }
        }
        else
        {
            throw new FileNotFoundException($"Neither a folder nor a zip file: {path}", path);
        }

        var history = accumulator.ToHistory();
        var stats = history.Stats;
        this.log.LogInformation(
            "Spotify history: {Files} files, {Rows} rows, {Streams} song plays; skipped {Episodes} episodes, {NoMetadata} rows without a track, {Duplicates} duplicates",
            stats.Files, stats.Rows, history.Streams.Count, stats.Episodes, stats.NoMetadata, stats.Duplicates);
        return history;
    }

    /// <summary>Reads one history file; for tests and callers that have the JSON at hand.</summary>
    public static async Task<SpotifyHistory> ReadFileAsync(Stream json, CancellationToken ct = default)
    {
        var accumulator = new Accumulator();
        await accumulator.AddFileAsync(json, ct);
        return accumulator.ToHistory();
    }

    private static void CheckFound(int count, bool accountData, string path)
    {
        if (count > 0)
            return;
        throw new InvalidDataException(accountData
            ? $"Only the extended streaming history (Streaming_History_Audio_*.json) is supported, not the account data in {path}."
            : $"No Streaming_History_Audio_*.json in {path}.");
    }

    /// <summary>Collects the rows of several files and drops exact repeats.</summary>
    private sealed class Accumulator
    {
        private readonly List<SpotifyStream> streams = [];
        private readonly HashSet<(DateTimeOffset, string, int, string?, string?)> seen = [];
        private int files;
        private int rows;
        private int episodes;
        private int noMetadata;
        private int duplicates;

        public async Task AddFileAsync(Stream json, CancellationToken ct)
        {
            this.files++;
            await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<Row>(json, JSON_OPTIONS, ct))
            {
                if (row is null)
                    continue;
                this.rows++;
                if (row.SpotifyEpisodeUri is not null || row.AudiobookUri is not null || row.AudiobookChapterUri is not null)
                {
                    this.episodes++;
                    continue;
                }
                if (row.Ts is not { } ts || string.IsNullOrEmpty(row.SpotifyTrackUri)
                    || string.IsNullOrWhiteSpace(row.MasterMetadataTrackName) || string.IsNullOrWhiteSpace(row.MasterMetadataAlbumArtistName))
                {
                    this.noMetadata++;
                    continue;
                }

                var ms = (int)Math.Clamp(row.MsPlayed ?? 0, 0, int.MaxValue);
                if (!this.seen.Add((ts, row.SpotifyTrackUri, ms, row.ReasonStart, row.ReasonEnd)))
                {
                    this.duplicates++;
                    continue;
                }

                this.streams.Add(new SpotifyStream(
                    EndedAt: ts.ToUniversalTime(),
                    TrackUri: row.SpotifyTrackUri,
                    Artist: row.MasterMetadataAlbumArtistName.Trim(),
                    Title: row.MasterMetadataTrackName.Trim(),
                    Album: string.IsNullOrWhiteSpace(row.MasterMetadataAlbumAlbumName) ? null : row.MasterMetadataAlbumAlbumName.Trim(),
                    MsPlayed: ms,
                    ReasonStart: row.ReasonStart,
                    ReasonEnd: row.ReasonEnd,
                    Incognito: row.IncognitoMode ?? false));
            }
        }

        public SpotifyHistory ToHistory() => new(
            this.streams.OrderBy(s => s.EndedAt).ToList(),
            new SpotifyReadStats(this.files, this.rows, this.episodes, this.noMetadata, this.duplicates));
    }

    /// <summary>The fields of a history row that are read; everything is optional because the values changed over the years.</summary>
    private sealed record Row(
        DateTimeOffset? Ts,
        long? MsPlayed,
        string? MasterMetadataTrackName,
        string? MasterMetadataAlbumArtistName,
        string? MasterMetadataAlbumAlbumName,
        string? SpotifyTrackUri,
        string? SpotifyEpisodeUri,
        string? AudiobookUri,
        string? AudiobookChapterUri,
        string? ReasonStart,
        string? ReasonEnd,
        bool? IncognitoMode);
}
