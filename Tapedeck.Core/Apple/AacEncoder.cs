using System.ComponentModel;
using System.Diagnostics;

namespace Tapedeck.Core.Apple;

/// <summary>Turns a library file into AAC in an MP4 container (<c>.m4a</c>), which Apple Music plays.</summary>
public interface IAacEncoder
{
    /// <summary>Whether encoding is possible at all (ffmpeg is there).</summary>
    bool IsAvailable { get; }

    /// <summary>Encodes <paramref name="source"/> to <paramref name="target"/>; an existing target is replaced only on success.</summary>
    Task EncodeAsync(string source, string target, CancellationToken cancellation = default);
}

/// <summary>
/// Encodes with the bundled ffmpeg at 256 kbit/s like iTunes Plus. On macOS ffmpeg may bring Apple's own
/// encoder (<c>aac_at</c>), which sounds better than ffmpeg's built-in <c>aac</c>; it is used when listed.
/// Only the audio is written; tags and cover come from <see cref="Mp4TagWriter"/> afterwards.
/// </summary>
public sealed class AacEncoder(Services.IToolHost tools) : IAacEncoder
{
    /// <summary>Bit rate of the copies, the iTunes Store's own.</summary>
    public const string BIT_RATE = "256k";

    /// <summary>Apple's AudioToolbox encoder, only in ffmpeg builds for macOS.</summary>
    public const string APPLE_ENCODER = "aac_at";

    /// <summary>ffmpeg's built-in encoder, present in every build.</summary>
    public const string NATIVE_ENCODER = "aac";

    private string? encoder;

    public bool IsAvailable => tools.HasFFmpeg;

    public async Task EncodeAsync(string source, string target, CancellationToken cancellation = default)
    {
        this.encoder ??= await this.ChooseEncoderAsync();
        var temp = target + ".tmp";
        var psi = tools.FFmpeg();
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-nostats", "-nostdin", "-y",
                     "-i", source,
                     "-map", "0:a:0", "-vn",
                     "-map_metadata", "-1",
                     "-c:a", this.encoder, "-b:a", BIT_RATE,
                     "-movflags", "+faststart",
                     "-f", "ipod", temp,
                 })
            psi.ArgumentList.Add(argument);

        try
        {
            var log = await RunAsync(psi, cancellation);
            if (!File.Exists(temp))
                throw new InvalidOperationException($"ffmpeg wrote no file: {LastLine(log)}");
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private async Task<string> ChooseEncoderAsync()
    {
        var psi = tools.FFmpeg();
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-encoders");
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return NATIVE_ENCODER;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await errors;
            return HasEncoder(await output, APPLE_ENCODER) ? APPLE_ENCODER : NATIVE_ENCODER;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return NATIVE_ENCODER;
        }
    }

    /// <summary>Whether the output of <c>ffmpeg -encoders</c> lists the encoder (<c> A....D aac_at  …</c>).</summary>
    public static bool HasEncoder(string encoderList, string name) =>
        encoderList.Split('\n').Any(line =>
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && parts[0].StartsWith('A') && parts[1] == name;
        });

    private static async Task<string> RunAsync(ProcessStartInfo psi, CancellationToken cancellation)
    {
        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"ffmpeg could not be started: {ex.Message}", ex);
        }
        if (process is null)
            throw new InvalidOperationException("ffmpeg could not be started.");

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellation);
            var errors = process.StandardError.ReadToEndAsync(cancellation);
            try
            {
                await process.WaitForExitAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            await output;
            var log = await errors;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}: {LastLine(log)}");
            return log;
        }
    }

    private static string LastLine(string log) =>
        log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
}
