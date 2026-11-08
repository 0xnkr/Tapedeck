using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Core.Genres;

/// <summary>A stable genre to sort a mixed library by, with the words of the fine genres that belong to it.</summary>
/// <param name="Name">Written into the file's GENRE tag.</param>
/// <param name="Scene">
/// A scene that says more than the style, like K-Pop or Schlager: it comes first even when the song's other genres
/// (Dance-Pop, Electropop) point to a broader one more often.
/// </param>
/// <param name="Words">Normalized words or phrases; a fine genre containing one of them belongs here.</param>
public sealed record MainGenre(string Name, bool Scene, IReadOnlyList<string> Words);

/// <summary>
/// Maps the fine genres of MusicBrainz, Last.fm or the user ("G-Funk", "Deep House", "Dance-Pop") onto a small fixed
/// list of main genres, so a phone sorts a mixed library into a few stable groups. Fine genres stay in the database;
/// only the main genres go into the file.
/// </summary>
public static class GenreMap
{
    /// <summary>At most this many main genres per track.</summary>
    public const int MAX_MAIN = 2;

    /// <summary>
    /// Specific before broad: a fine genre takes the first main genre whose words it contains, so "Pop Punk" is Punk,
    /// "Pop Rock" is Rock and only what is left of "pop" is Pop.
    /// </summary>
    public static readonly IReadOnlyList<MainGenre> ALL =
    [
        new("K-Pop", true, ["k pop", "kpop", "korean"]),
        new("Anime & J-Pop", true, ["anime", "j pop", "jpop", "j rock", "vocaloid", "japanese", "city pop", "anison"]),
        new("Soundtrack", false, ["soundtrack", "film score", "video game music", "musical", "ost", "score"]),
        new("Schlager", true, ["schlager", "volksmusik", "neue deutsche welle", "ndw"]),
        new("Latin", true, ["latin", "reggaeton", "salsa", "bachata", "cumbia", "bossa nova", "samba", "dembow", "funk carioca", "sertanejo", "mpb", "corrido", "flamenco", "tango", "merengue"]),
        new("Reggae", false, ["reggae", "dancehall", "ska", "rocksteady", "dub"]),
        new("Metal", false, ["metal", "metalcore", "deathcore", "djent", "grindcore"]),
        new("Punk", false, ["punk", "emo", "post hardcore", "hardcore punk", "screamo"]),
        new("Hip-Hop", false, ["hip hop", "hiphop", "rap", "trap", "drill", "grime", "boom bap", "g funk", "crunk", "hyphy", "phonk", "horrorcore", "turntablism", "deutschrap"]),
        new("R&B", false, ["r and b", "rnb", "new jack swing"]),
        new("Soul & Funk", false, ["soul", "funk", "disco", "motown", "gospel"]),
        new("Electronic", false, ["electronic", "electronica", "edm", "house", "techno", "trance", "electro", "dubstep", "drum and bass", "drum n bass", "dnb", "jungle", "bass music", "future bass", "big room", "hardstyle", "garage", "breakbeat", "synthwave", "eurodance", "downtempo", "ambient", "chillout", "chillwave", "lo fi", "idm", "trip hop", "club", "electroclash", "bassline", "indietronica", "dance"]),
        // indie and alternative rock are rock; "Indie Pop" and "Indie Folk" go to Pop and Country & Folk (see EXACT)
        new("Rock", false, ["rock", "grunge", "rockabilly", "aor", "indie", "alternative", "britpop", "shoegaze", "new wave", "psychedelia", "neo psychedelia"]),
        new("Country & Folk", false, ["country", "folk", "bluegrass", "americana", "singer songwriter"]),
        new("Jazz & Blues", false, ["jazz", "blues", "swing", "bebop", "big band"]),
        new("Classical", false, ["classical", "baroque", "opera", "orchestral", "chamber music", "symphony"]),
        new("World", false, ["world", "afrobeat", "afrobeats", "afropop", "amapiano", "highlife"]),
        new("Pop", false, ["pop", "electropop", "synthpop", "europop", "deutschpop", "bubblegum", "teen pop", "ballad", "chanson"]),
    ];

    private static readonly Dictionary<string, MainGenre> BY_NAME =
        ALL.ToDictionary(g => MbNormalizer.Normalize(g.Name), StringComparer.Ordinal);

    // fine genres whose words point to the wrong main genre
    private static readonly Dictionary<string, string> EXACT = new(StringComparer.Ordinal)
    {
        ["dance pop"] = "Pop",
        ["garage rock"] = "Rock",
        ["emo rap"] = "Hip-Hop",
        ["latin trap"] = "Latin",
        ["electro swing"] = "Electronic",
        ["pop soul"] = "Soul & Funk",
        ["dance rock"] = "Rock",
        ["funk rock"] = "Rock",
        ["bubblegum bass"] = "Electronic",
        ["trip hop"] = "Electronic",
        ["post punk"] = "Rock",
        ["indie pop"] = "Pop",
        ["alternative pop"] = "Pop",
        ["dream pop"] = "Pop",
        ["indie folk"] = "Country & Folk",
    };

    /// <summary>The main genre of one fine genre, or <c>null</c> for noise like "seen live" or "favorites".</summary>
    public static MainGenre? Of(string fine)
    {
        var normalized = MbNormalizer.Normalize(fine);
        if (normalized.Length == 0)
            return null;
        // a main genre typed by the user stays what it is
        if (BY_NAME.TryGetValue(normalized, out var named))
            return named;
        if (EXACT.TryGetValue(normalized, out var exact))
            return BY_NAME[MbNormalizer.Normalize(exact)];
        return ALL.FirstOrDefault(g => g.Words.Any(w => MbNormalizer.ContainsWords(normalized, w)));
    }

    /// <summary>
    /// The main genres of a track: scenes first, then the one most of its fine genres point to. A second one is only
    /// added when at least half as many fine genres point to it, so one stray tag does not make a track two things.
    /// </summary>
    public static IReadOnlyList<string> MainGenres(IReadOnlyList<string> fine)
    {
        var counts = fine
            .Select(Of)
            .OfType<MainGenre>()
            .GroupBy(g => g)
            .Select(g => (Genre: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Genre.Scene)
            .ThenByDescending(g => g.Count)
            .ThenBy(g => Priority(g.Genre))
            .ToList();
        if (counts.Count == 0)
            return [];

        var strongest = counts.Max(c => c.Count);
        return counts
            .Where((c, i) => i == 0 || c.Count * 2 >= strongest)
            .Take(MAX_MAIN)
            .Select(c => c.Genre.Name)
            .ToList();
    }

    private static int Priority(MainGenre genre)
    {
        for (var i = 0; i < ALL.Count; i++)
        {
            if (ReferenceEquals(ALL[i], genre))
                return i;
        }
        return ALL.Count;
    }
}
