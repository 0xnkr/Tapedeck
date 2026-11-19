using System.ComponentModel;
using System.Diagnostics;

namespace Tapedeck.Core.Services;

/// <summary>Starting the external tools the same way everywhere: no window, output and errors redirected.</summary>
public static class ToolProcess
{
    /// <summary>A start info for the executable; arguments and environment are up to the caller.</summary>
    public static ProcessStartInfo StartInfo(string fileName) => new(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    /// <summary>The start info with the arguments added.</summary>
    public static ProcessStartInfo With(this ProcessStartInfo psi, IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        return psi;
    }

    /// <summary>The first line the tool prints, e.g. for <c>--version</c>; <c>null</c> when it does not start.</summary>
    public static async Task<string?> FirstLineAsync(ProcessStartInfo psi)
    {
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;
            var line = await process.StandardOutput.ReadLineAsync();
            // the rest is not needed, but must be read so the tool never blocks on a full pipe
            var rest = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await rest;
            await errors;
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
