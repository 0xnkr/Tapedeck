using System.Text.Json.Serialization;

namespace Tapedeck.Core.Models;

/// <summary>A video of a YouTube search as read by <c>yt-dlp --flat-playlist -J "ytsearchN:…"</c>.</summary>
/// <param name="Description">The start of the description; a search flattens it to one line.</param>
/// <param name="Artists">The artists YouTube lists for a song; only known after fetching the video itself.</param>
public sealed record YtSearchResult(string VideoId, string Title, string? Channel, int? DurationS, long? ViewCount, string? Description,
    IReadOnlyList<string>? Artists = null)
{
    private const string TOPIC_MARK = "Provided to YouTube by";

    // some labels write their own description; the channel still gives it away
    private const string TOPIC_CHANNEL = " - Topic";

    /// <summary>
    /// An upload generated from the label's delivery (the "Artist - Topic" channel): the plain song with the
    /// release's title and length. Its channel carries the artist's name, without " - Topic".
    /// </summary>
    [JsonIgnore]
    public bool IsTopic =>
        this.Description?.StartsWith(TOPIC_MARK, StringComparison.Ordinal) == true
        || this.Channel?.EndsWith(TOPIC_CHANNEL, StringComparison.Ordinal) == true;
}
