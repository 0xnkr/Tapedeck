using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core;
using Tapedeck.Core.Services;
using AndroidApplication = Android.App.Application;

namespace Tapedeck.Android.Services;

/// <summary>
/// The phone's tools, shipped inside the APK (tools/android-natives.ps1): Python, ffmpeg and QuickJS as lib*.so in the
/// native library folder, the only place an app may start programs from. Their shared libraries come zipped
/// (lib*.zip.so) and are unpacked once into the app's own storage, found through <c>LD_LIBRARY_PATH</c>.
/// yt-dlp is plain Python code: it lives unpacked in the tools folder, so Python can cache its compiled modules
/// (0.8 s instead of 3 s per call), and updates itself there.
/// </summary>
public sealed class AndroidToolHost : IToolHost
{
    private const string YT_DLP_ASSET = "tools/yt-dlp";
    private const string STAMP = ".stamp";
    private const int SYMLINK = 0xA000;
    private const int FILE_TYPE_MASK = 0xF000;
    // right after a cold start Android blocks the process from the network for a moment: DNS fails for ~2 s
    private static readonly TimeSpan NETWORK_GRACE = TimeSpan.FromSeconds(3);

    private readonly AppPaths paths;
    private readonly ILogger log;
    private readonly string nativeDir;
    private readonly string packagesDir;
    private readonly string cacheDir;
    private readonly long created = Stopwatch.GetTimestamp();
    private readonly SemaphoreSlim setup = new(1, 1);

    public AndroidToolHost(AppPaths paths, ILogger<AndroidToolHost>? logger = null)
    {
        var context = AndroidApplication.Context;
        this.paths = paths;
        this.log = (ILogger?)logger ?? NullLogger.Instance;
        this.nativeDir = context.ApplicationInfo!.NativeLibraryDir!;
        // no backup: the unpacked tools come back from the APK anyway
        this.packagesDir = Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "packages");
        this.cacheDir = context.CacheDir!.AbsolutePath;
    }

    public string FFmpegPath => Path.Combine(this.nativeDir, "libffmpeg.so");

    public string JsRuntimeName => "QuickJS";

    public bool IsReady => this.HasPython && this.HasFFmpeg && File.Exists(Path.Combine(this.YtDlpDir, "__main__.py"))
                           && Stopwatch.GetElapsedTime(this.created) >= NETWORK_GRACE;

    public bool HasFFmpeg => IsUnpacked(this.FFmpegDir, this.Zip("libffmpeg.zip.so"));

    private bool HasPython => IsUnpacked(this.PythonDir, this.Zip("libpython.zip.so"));

    private string PythonPath => Path.Combine(this.nativeDir, "libpython.so");

    private string QuickJsPath => Path.Combine(this.nativeDir, "libqjs.so");

    private string PythonDir => Path.Combine(this.packagesDir, "python");

    private string FFmpegDir => Path.Combine(this.packagesDir, "ffmpeg");

    private string ZipApp => Path.Combine(this.paths.ToolsDir, "yt-dlp");

    private string YtDlpDir => Path.Combine(this.paths.ToolsDir, "yt-dlp-dir");

    public async Task SetupAsync()
    {
        await this.setup.WaitAsync();
        try
        {
            var watch = Stopwatch.StartNew();
            await Task.Run(() =>
            {
                this.Extract(this.Zip("libpython.zip.so"), this.PythonDir);
                this.Extract(this.Zip("libffmpeg.zip.so"), this.FFmpegDir);
                if (!File.Exists(this.ZipApp))
                {
                    Directory.CreateDirectory(this.paths.ToolsDir);
                    using var input = AndroidApplication.Context.Assets!.Open(YT_DLP_ASSET);
                    using var output = File.Create(this.ZipApp);
                    input.CopyTo(output);
                }
            });
            if (!File.Exists(Path.Combine(this.YtDlpDir, "__main__.py")))
                await this.UnpackYtDlpAsync();
            this.log.LogInformation("Tools ready in {Seconds:0.0} s", watch.Elapsed.TotalSeconds);

            var wait = NETWORK_GRACE - Stopwatch.GetElapsedTime(this.created);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait);
        }
        finally
        {
            this.setup.Release();
        }
    }

    public ProcessStartInfo YtDlp() => this.WithEnvironment(ToolProcess.StartInfo(this.PythonPath))
        .With([this.YtDlpDir, "--js-runtimes", $"quickjs:{this.QuickJsPath}"]);

    public ProcessStartInfo FFmpeg() => this.WithEnvironment(ToolProcess.StartInfo(this.FFmpegPath));

    public async Task<string> UpdateYtDlpAsync()
    {
        // the zipapp updates itself; the unpacked folder Python runs is made from it again afterwards
        using var process = Process.Start(this.WithEnvironment(ToolProcess.StartInfo(this.PythonPath)).With([this.ZipApp, "-U"]))
                            ?? throw new InvalidOperationException("Could not start yt-dlp.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var text = (await output).Trim();
        var error = (await errors).Trim();
        this.log.LogInformation("yt-dlp update: {Output}", text);
        // a failed update ends with exit code 100 (seen when GitHub timed out)
        if (process.ExitCode != 0)
        {
            this.log.LogWarning("yt-dlp update failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
            throw new InvalidOperationException(error.Length > 0 ? error : text);
        }

        await this.setup.WaitAsync();
        try
        {
            await this.UnpackYtDlpAsync();
        }
        finally
        {
            this.setup.Release();
        }
        return text;
    }

    public async Task<ToolVersions> GetVersionsAsync()
    {
        if (!this.HasPython || !this.HasFFmpeg)
            return new ToolVersions(null, null, null);
        var ffmpeg = await ToolProcess.FirstLineAsync(this.FFmpeg().With(["-version"]));
        return new ToolVersions(
            await ToolProcess.FirstLineAsync(this.YtDlp().With(["--version"])),
            ffmpeg is null ? null : ToolService.ShortVersion(ffmpeg),
            await ToolProcess.FirstLineAsync(ToolProcess.StartInfo(this.QuickJsPath).With(["--version"])));
    }

    /// <summary>The environment youtubedl-android uses for its Termux builds of Python and ffmpeg.</summary>
    private ProcessStartInfo WithEnvironment(ProcessStartInfo psi)
    {
        var usr = Path.Combine(this.PythonDir, "usr");
        psi.WorkingDirectory = this.paths.ToolsDir;
        psi.Environment["LD_LIBRARY_PATH"] = $"{usr}/lib:{this.FFmpegDir}/usr/lib";
        psi.Environment["SSL_CERT_FILE"] = $"{usr}/etc/tls/cert.pem";
        psi.Environment["PYTHONHOME"] = usr;
        psi.Environment["HOME"] = usr;
        psi.Environment["TMPDIR"] = this.cacheDir;
        psi.Environment["PATH"] = $"{System.Environment.GetEnvironmentVariable("PATH")}:{this.nativeDir}";
        return psi;
    }

    private string Zip(string name) => Path.Combine(this.nativeDir, name);

    /// <summary>Whether the folder holds the zip of this APK; a new APK with other libraries unpacks again.</summary>
    private static bool IsUnpacked(string target, string zip) =>
        File.Exists(zip) && File.Exists(Path.Combine(target, STAMP))
        && File.ReadAllText(Path.Combine(target, STAMP)) == new FileInfo(zip).Length.ToString();

    /// <summary>Unpacks the zip once, with its symbolic links (the libraries' version names point to the files).</summary>
    private void Extract(string zip, string target)
    {
        if (IsUnpacked(target, zip))
            return;

        var watch = Stopwatch.StartNew();
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;

        int files = 0, links = 0;
        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var entry in archive.Entries)
            {
                var path = Path.GetFullPath(Path.Combine(target, entry.FullName));
                if (!path.StartsWith(root, StringComparison.Ordinal))
                    throw new InvalidDataException($"{Path.GetFileName(zip)} has an entry outside its folder: {entry.FullName}");
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var mode = (entry.ExternalAttributes >> 16) & 0xFFFF;
                if ((mode & FILE_TYPE_MASK) == SYMLINK)
                {
                    using var reader = new StreamReader(entry.Open());
                    File.CreateSymbolicLink(path, reader.ReadToEnd());
                    links++;
                }
                else
                {
                    entry.ExtractToFile(path, overwrite: true);
                    files++;
                }
            }
        }

        File.WriteAllText(Path.Combine(target, STAMP), new FileInfo(zip).Length.ToString());
        this.log.LogInformation("Unpacked {Zip}: {Files} files, {Links} links in {Milliseconds} ms",
            Path.GetFileName(zip), files, links, watch.ElapsedMilliseconds);
    }

    /// <summary>Makes the folder Python runs yt-dlp from out of the zipapp; replaced only once complete.</summary>
    private async Task UnpackYtDlpAsync()
    {
        var temp = this.YtDlpDir + ".new";
        if (Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
        // Python's zipfile reads the zipapp with its "#!" line in front, .NET's ZipArchive may not
        var psi = this.WithEnvironment(ToolProcess.StartInfo(this.PythonPath)).With(
            ["-c", "import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])", this.ZipApp, temp]);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Python.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        var error = (await errors).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Could not unpack yt-dlp: {error}");

        if (Directory.Exists(this.YtDlpDir))
            Directory.Delete(this.YtDlpDir, recursive: true);
        Directory.Move(temp, this.YtDlpDir);
        this.log.LogInformation("Unpacked yt-dlp into {Folder}", this.YtDlpDir);
    }
}
