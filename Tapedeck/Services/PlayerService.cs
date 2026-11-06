using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenAL;
using Tapedeck.Core.Playback;
using Tapedeck.Core.Services;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>What the player needs to play a track and to show it in the player bar.</summary>
/// <param name="FilePath">Absolute path of the audio file.</param>
/// <param name="Cover">Square cover or thumbnail for the bar; <c>null</c> shows a placeholder.</param>
/// <param name="Duration">Known length, until the file tells its own.</param>
/// <param name="LoudnessLufs">Measured loudness, so all tracks play equally loud; <c>null</c> plays the file as it is.</param>
public sealed record PlayRequest(
    long TrackId, string FilePath, string Title, string Artist, string? Cover, TimeSpan Duration, double? LoudnessLufs = null);

/// <summary>
/// Plays one track at a time, for listening in: Opus is decoded by <see cref="OpusPcmReader"/> and streamed
/// in short blocks to OpenAL Soft, which brings its own native library for Windows, Linux and macOS.
/// A background task owns all OpenAL calls; pause, seek and stop are requests it picks up.
/// The audio file is open while a track is loaded, so callers stop it before writing or moving the file.
/// </summary>
public sealed partial class PlayerService : ObservableObject, IDisposable
{
    private const int BUFFER_COUNT = 4;
    // a quarter second per buffer: a second of audio queued, short enough for a quick pause and seek
    private const int BLOCK_SAMPLES = OpusPcmReader.SAMPLE_RATE / 4 * OpusPcmReader.CHANNELS;
    private static readonly TimeSpan TICK = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan POSITION_INTERVAL = TimeSpan.FromMilliseconds(250);
    // asking for the default device enumerates all devices, so not on every tick
    private static readonly TimeSpan DEVICE_CHECK_INTERVAL = TimeSpan.FromSeconds(2);

    // ALC constants Silk.NET has no names for
    private const int ALC_DEFAULT_ALL_DEVICES_SPECIFIER = 0x1012;
    private const int ALC_ALL_DEVICES_SPECIFIER = 0x1013;
    private const int ALC_CONNECTED = 0x313;

    // property changes are raised on the UI thread the service was created on
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private readonly object requests = new();
    // opening the device happens once, from the warm-up or the first play, whichever comes first
    private readonly object openAl = new();
    private AL? al;
    private ALContext? alc;
    private unsafe Device* device;
    private unsafe Context* context;
    // alcReopenDeviceSOFT (ALC_SOFT_reopen_device); null when OpenAL has no such function
    private unsafe delegate* unmanaged[Cdecl]<Device*, byte*, int*, byte> reopenDevice;

    private CancellationTokenSource? run;
    private Task? loop;
    // requests from the UI thread to the loop
    private bool pauseRequested;
    private TimeSpan? seekRequest;

    /// <summary>The track loaded in the player, playing or paused; <c>null</c> hides the player bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrack))]
    public partial long? CurrentTrackId { get; private set; }

    public bool HasTrack => this.CurrentTrackId is not null;

    [ObservableProperty]
    public partial bool IsPlaying { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Artist { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? Cover { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationSeconds), nameof(DurationText))]
    public partial TimeSpan Duration { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionSeconds), nameof(PositionText))]
    public partial TimeSpan Position { get; private set; }

    public double DurationSeconds => Math.Max(1, this.Duration.TotalSeconds);

    /// <summary>For the slider: reading shows the position, setting it seeks.</summary>
    public double PositionSeconds
    {
        get => this.Position.TotalSeconds;
        set
        {
            if (Math.Abs(value - this.Position.TotalSeconds) > 0.5)
                this.Seek(TimeSpan.FromSeconds(value));
        }
    }

    public string PositionText => Format(this.Position);

    public string DurationText => Format(this.Duration);

    /// <summary>The player could not play; the text says why.</summary>
    public event Action<string>? Failed;

    /// <summary>Plays the track, pauses it when it is playing, resumes it when it is paused.</summary>
    public async Task ToggleAsync(PlayRequest request)
    {
        if (this.CurrentTrackId == request.TrackId)
        {
            this.TogglePause();
            return;
        }
        await this.StopAsync();
        this.Start(request);
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (this.CurrentTrackId is null)
            return;
        lock (this.requests)
            this.pauseRequested = this.IsPlaying;
        this.IsPlaying = !this.IsPlaying;
    }

    public void Seek(TimeSpan position)
    {
        if (this.CurrentTrackId is null)
            return;
        lock (this.requests)
            this.seekRequest = position;
        this.Position = position;
    }

    /// <summary>Stops and unloads the track, which closes its file.</summary>
    [RelayCommand]
    public async Task StopAsync()
    {
        var cts = this.run;
        var task = this.loop;
        this.run = null;
        this.loop = null;
        if (cts is not null)
        {
            await cts.CancelAsync();
            try
            {
                if (task is not null)
                    await task;
            }
            catch (OperationCanceledException)
            {
                // stopped on purpose
            }
            cts.Dispose();
        }

        this.CurrentTrackId = null;
        this.IsPlaying = false;
        this.Position = TimeSpan.Zero;
    }

    /// <summary>Releases the file of this track if it is the loaded one, e.g. before its tags are written.</summary>
    public Task StopIfLoadedAsync(long trackId) =>
        this.CurrentTrackId == trackId ? this.StopAsync() : Task.CompletedTask;

    public unsafe void Dispose()
    {
        this.run?.Cancel();
        this.loop?.Wait(TimeSpan.FromSeconds(1));
        if (this.alc is { } alc)
        {
            alc.MakeContextCurrent((Context*)null);
            if (this.context != null)
                alc.DestroyContext(this.context);
            if (this.device != null)
                alc.CloseDevice(this.device);
        }
    }

    private void Start(PlayRequest request)
    {
        this.CurrentTrackId = request.TrackId;
        this.Title = request.Title;
        this.Artist = request.Artist;
        this.Cover = request.Cover;
        this.Duration = request.Duration;
        this.Position = TimeSpan.Zero;
        this.IsPlaying = true;
        lock (this.requests)
        {
            this.pauseRequested = false;
            this.seekRequest = null;
        }

        var cts = this.run = new CancellationTokenSource();
        this.loop = Task.Run(() => this.Play(request, cts.Token), cts.Token);
    }

    /// <summary>The loop on a background thread: decode, queue, play, until the end or a stop.</summary>
    private void Play(PlayRequest request, CancellationToken ct)
    {
        try
        {
            this.EnsureOpenAl();
        }
        catch (Exception ex)
        {
            // no native library, no audio device
            this.Fail(request.TrackId, string.Format(Strings.Player_NoDevice, ex.Message));
            return;
        }

        OpusPcmReader reader;
        try
        {
            reader = new OpusPcmReader(request.FilePath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            this.Fail(request.TrackId, string.Format(Strings.Player_Unreadable, ex.Message));
            return;
        }

        // a Bluetooth speaker connected since the device was opened is the default output now
        this.FollowDefaultDevice();

        var al = this.al!;
        using (reader)
        {
            if (reader.Duration > TimeSpan.Zero)
                this.Post(() => this.Duration = reader.Duration);

            var source = al.GenSource();
            // the same leveling the phone does with the track gain in the tags
            var gain = LoudnessMeter.PlaybackGain(request.LoudnessLufs);
            al.SetSourceProperty(source, SourceFloat.MaxGain, Math.Max(gain, 1));
            al.SetSourceProperty(source, SourceFloat.Gain, gain);
            var buffers = al.GenBuffers(BUFFER_COUNT);
            // sample frames of each queued buffer, to tell the position
            var queued = new Queue<int>();
            var start = TimeSpan.Zero;
            long playedFrames = 0;
            var paused = false;
            var lastPosition = DateTime.MinValue;
            var lastDeviceCheck = DateTime.UtcNow;
            var finished = false;

            try
            {
                foreach (var buffer in buffers)
                    this.Fill(al, source, buffer, reader, queued);
                al.SourcePlay(source);

                while (!ct.IsCancellationRequested)
                {
                    bool wantPause;
                    TimeSpan? seek;
                    lock (this.requests)
                    {
                        wantPause = this.pauseRequested;
                        seek = this.seekRequest;
                        this.seekRequest = null;
                    }

                    if (seek is { } target)
                    {
                        al.SourceStop(source);
                        Unqueue(al, source, queued.Count);
                        queued.Clear();
                        start = reader.Seek(target);
                        playedFrames = 0;
                        foreach (var buffer in buffers)
                            this.Fill(al, source, buffer, reader, queued);
                        if (!wantPause)
                            al.SourcePlay(source);
                    }

                    if (wantPause != paused)
                    {
                        if (wantPause)
                            al.SourcePause(source);
                        else
                            al.SourcePlay(source);
                        paused = wantPause;
                    }

                    // refill the buffers the device has played
                    al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out var processed);
                    for (var i = 0; i < processed; i++)
                    {
                        var done = Unqueue(al, source, 1);
                        if (queued.TryDequeue(out var frames))
                            playedFrames += frames;
                        this.Fill(al, source, done, reader, queued);
                    }

                    if (queued.Count == 0)
                    {
                        finished = true;
                        break;
                    }

                    // the device ran dry for a moment (slow disk, busy CPU): carry on
                    al.GetSourceProperty(source, GetSourceInteger.SourceState, out var state);
                    if (!paused && (SourceState)state == SourceState.Stopped)
                        al.SourcePlay(source);

                    if (DateTime.UtcNow - lastPosition >= POSITION_INTERVAL)
                    {
                        al.GetSourceProperty(source, GetSourceInteger.SampleOffset, out var offset);
                        var position = start + TimeSpan.FromSeconds((double)(playedFrames + offset) / OpusPcmReader.SAMPLE_RATE);
                        this.Post(() =>
                        {
                            if (this.CurrentTrackId == request.TrackId)
                                this.Position = position;
                        });
                        lastPosition = DateTime.UtcNow;
                    }

                    // the output may change while playing, e.g. when the speaker connects or goes away
                    if (DateTime.UtcNow - lastDeviceCheck >= DEVICE_CHECK_INTERVAL)
                    {
                        this.FollowDefaultDevice();
                        lastDeviceCheck = DateTime.UtcNow;
                    }

                    Thread.Sleep(TICK);
                }
            }
            finally
            {
                al.SourceStop(source);
                al.GetSourceProperty(source, GetSourceInteger.BuffersQueued, out var left);
                Unqueue(al, source, left);
                al.DeleteSource(source);
                al.DeleteBuffers(buffers);
            }

            if (finished)
            {
                // played to the end: unload, the bar disappears
                this.Post(() =>
                {
                    if (this.CurrentTrackId != request.TrackId)
                        return;
                    this.run?.Dispose();
                    this.run = null;
                    this.loop = null;
                    this.CurrentTrackId = null;
                    this.IsPlaying = false;
                    this.Position = TimeSpan.Zero;
                });
            }
        }
    }

    /// <summary>Decodes the next block into the buffer and queues it; at the end of the file nothing is queued.</summary>
    private void Fill(AL al, uint source, uint buffer, OpusPcmReader reader, Queue<int> queued)
    {
        if (reader.Read(BLOCK_SAMPLES) is not { } block)
            return;
        al.BufferData(buffer, BufferFormat.Stereo16, block, OpusPcmReader.SAMPLE_RATE);
        al.SourceQueueBuffers(source, [buffer]);
        queued.Enqueue(block.Length / OpusPcmReader.CHANNELS);
    }

    /// <returns>The last buffer taken off the queue.</returns>
    private static uint Unqueue(AL al, uint source, int count)
    {
        var ids = new uint[1];
        for (var i = 0; i < count; i++)
            al.SourceUnqueueBuffers(source, ids);
        return ids[0];
    }

    /// <summary>
    /// Opens the audio device in the background, so the first click plays right away instead of after a second.
    /// A missing device is reported only when the user actually plays.
    /// </summary>
    public void WarmUp() => _ = Task.Run(() =>
    {
        try
        {
            this.EnsureOpenAl();
        }
        catch (Exception)
        {
            // reported on the first play
        }
    });

    /// <summary>Opens the default audio device once.</summary>
    private unsafe void EnsureOpenAl()
    {
        lock (this.openAl)
        {
            if (this.al is not null)
                return;

            var (alc, al) = LoadOpenAl();
            var device = alc.OpenDevice(string.Empty);
            if (device == null)
                throw new InvalidOperationException(Strings.Player_NoDeviceFound);
            var context = alc.CreateContext(device, null);
            alc.MakeContextCurrent(context);
            if (alc.IsExtensionPresent(device, "ALC_SOFT_reopen_device"))
                this.reopenDevice = (delegate* unmanaged[Cdecl]<Device*, byte*, int*, byte>)alc.GetProcAddress(device, "alcReopenDeviceSOFT");

            this.alc = alc;
            this.device = device;
            this.context = context;
            this.al = al;
        }
    }

    /// <summary>
    /// Moves playback to the system's default output when that is another device than the open one, or when the
    /// open one was disconnected. Without this the player keeps the device it opened at startup: a Bluetooth
    /// speaker connected later stays silent while other apps play on it. Reopening keeps the source and its
    /// queued buffers, so a playing track just carries on on the new device.
    /// </summary>
    private unsafe void FollowDefaultDevice()
    {
        lock (this.openAl)
        {
            if (this.alc is not { } alc || this.device == null || this.reopenDevice == null)
                return;

            // OpenAL Soft answers the default from the device list it built first; asking for the whole list
            // enumerates the devices again (a few milliseconds), so the default is the current one
            alc.GetContextProperty((Device*)null, (GetContextString)ALC_ALL_DEVICES_SPECIFIER);
            var wanted = alc.GetContextProperty((Device*)null, (GetContextString)ALC_DEFAULT_ALL_DEVICES_SPECIFIER);
            var current = alc.GetContextProperty(this.device, (GetContextString)ALC_ALL_DEVICES_SPECIFIER);
            var connected = 1;
            alc.GetContextProperty(this.device, (GetContextInteger)ALC_CONNECTED, 1, &connected);
            if (connected != 0 && (string.IsNullOrEmpty(wanted) || wanted == current))
                return;

            // null opens the default device; on failure the old one stays
            this.reopenDevice(this.device, null, null);
        }
    }

    /// <summary>
    /// Loads the bundled OpenAL Soft. .NET's own lookup finds it next to the app, under <c>runtimes/</c> and in the folder
    /// a single-file Tapedeck.exe extracts it to; Silk.NET's lookup misses the latter, so it is only the fallback.
    /// </summary>
    private static (ALContext Alc, AL Al) LoadOpenAl()
    {
        var name = OperatingSystem.IsWindows() ? "soft_oal.dll"
            : OperatingSystem.IsMacOS() ? "libopenal.dylib"
            : "libopenal.so";
        if (!NativeLibrary.TryLoad(name, typeof(PlayerService).Assembly, null, out var handle))
            return (ALContext.GetApi(true), AL.GetApi(true));

        var exports = new LamdaNativeContext(symbol => NativeLibrary.TryGetExport(handle, symbol, out var address) ? address : 0);
        return (new ALContext(exports), new AL(exports));
    }

    private void Fail(long trackId, string message) => this.Post(() =>
    {
        if (this.CurrentTrackId == trackId)
        {
            this.CurrentTrackId = null;
            this.IsPlaying = false;
        }
        this.Failed?.Invoke(message);
    });

    private void Post(Action action)
    {
        if (this.ui is null)
            Dispatcher.UIThread.Post(action);
        else
            this.ui.Post(_ => action(), null);
    }

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
}
