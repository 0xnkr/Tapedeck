using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tapedeck.Core.Services;

namespace Tapedeck.Core.Audio;

/// <summary>
/// Decodes a file once with the bundled ffmpeg to mono PCM at <see cref="AudioAnalysis.SAMPLE_RATE"/> and measures
/// it with <see cref="AudioAnalysis"/>. The features go into the file as <c>TAPEDECK_AUDIO</c> for a phone player's
/// recommendations.
/// </summary>
public sealed class AudioFeatureExtractor(ToolService tools)
{
    // a long DJ mix would need hundreds of MB; the first quarter hour says enough about it
    private const int MAX_SECONDS = 900;

    /// <summary>ffmpeg can run; without it nothing is measured.</summary>
    public bool IsAvailable => File.Exists(tools.FFmpegPath);

    /// <returns>The features; <c>null</c> when the file has only a few seconds of sound.</returns>
    /// <exception cref="InvalidOperationException">ffmpeg is missing or failed.</exception>
    public async Task<AudioFeatures?> ExtractAsync(string filePath, CancellationToken cancellation = default)
    {
        var samples = await this.DecodeAsync(filePath, cancellation);
        return await Task.Run(() => AudioAnalysis.Analyze(samples), cancellation);
    }

    private async Task<float[]> DecodeAsync(string filePath, CancellationToken cancellation)
    {
        var psi = new ProcessStartInfo(tools.FFmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-nostats", "-nostdin", "-loglevel", "error",
                     "-i", filePath,
                     "-map", "0:a:0",
                     "-t", MAX_SECONDS.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-ac", "1",
                     "-ar", AudioAnalysis.SAMPLE_RATE.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-f", "f32le", "-",
                 })
            psi.ArgumentList.Add(argument);

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
            using var pcm = new MemoryStream();
            var output = process.StandardOutput.BaseStream.CopyToAsync(pcm, cancellation);
            var errors = process.StandardError.ReadToEndAsync(cancellation);
            try
            {
                await output;
                await process.WaitForExitAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            var log = await errors;

            if (process.ExitCode != 0)
            {
                var last = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}: {last}");
            }

            var bytes = pcm.GetBuffer().AsSpan(0, (int)pcm.Length);
            return MemoryMarshal.Cast<byte, float>(bytes[..(bytes.Length / sizeof(float) * sizeof(float))]).ToArray();
        }
    }
}
