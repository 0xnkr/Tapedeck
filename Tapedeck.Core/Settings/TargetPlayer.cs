namespace Tapedeck.Core.Settings;

/// <summary>
/// The phone player the files are written for. Some players read tags differently from the standard; the files
/// get what the chosen one needs, and choosing another one rewrites them (see <see cref="PlayerQuirks"/>).
/// </summary>
public enum TargetPlayer
{
    /// <summary>Players that follow the Opus standard (RFC 7845).</summary>
    Standard,

    /// <summary>PixelPlayer reads <c>R128_TRACK_GAIN</c> as decibels instead of 1/256 dB, which mutes the track.</summary>
    PixelPlayer,
}

/// <summary>
/// What a target player needs beyond the standard tags. <see cref="Services.TagWriter"/> sets or removes every
/// field on each write, so nothing stays behind when the player changes.
/// </summary>
/// <param name="ReplayGain">
/// Also writes <c>REPLAYGAIN_TRACK_GAIN</c>. Players that look for it first get the right volume, even when they
/// misread <c>R128_TRACK_GAIN</c>.
/// </param>
public sealed record PlayerQuirks(bool ReplayGain)
{
    public static readonly PlayerQuirks NONE = new(ReplayGain: false);

    public static PlayerQuirks Of(TargetPlayer player) => player switch
    {
        TargetPlayer.PixelPlayer => new PlayerQuirks(ReplayGain: true),
        _ => NONE,
    };
}

/// <summary>The codes in <c>settings.json</c>; <c>null</c> is the standard.</summary>
public static class TargetPlayerCode
{
    private const string PIXEL_PLAYER = "pixelplayer";

    public static TargetPlayer Parse(string? code) => code switch
    {
        PIXEL_PLAYER => TargetPlayer.PixelPlayer,
        _ => TargetPlayer.Standard,
    };

    public static string? Of(TargetPlayer player) => player switch
    {
        TargetPlayer.PixelPlayer => PIXEL_PLAYER,
        _ => null,
    };
}
