using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenAL;
using Tapedeck.Core.Playback;
using Tapedeck.Core.Services;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>
/// The desktop player: Opus is decoded by <see cref="OpusPcmReader"/> and streamed in short blocks to OpenAL Soft,
/// which brings its own native library for Windows, Linux and macOS. The background loop owns all OpenAL calls.
/// </summary>
public sealed class OpenAlPlayerService(ILogger<OpenAlPlayerService> log) : PlayerService(log)
{
    private const int BUFFER_COUNT = 4;
    // a quarter second per buffer: a second of audio queued, short enough for a quick pause and seek
    private const int BLOCK_SAMPLES = OpusPcmReader.SAMPLE_RATE / 4 * OpusPcmReader.CHANNELS;
    private static readonly TimeSpan TICK = TimeSpan.FromMilliseconds(30);
    // asking for the default device enumerates all devices, so not on every tick
    private static readonly TimeSpan DEVICE_CHECK_INTERVAL = TimeSpan.FromSeconds(2);

    // ALC constants Silk.NET has no names for
    private const int ALC_DEFAULT_ALL_DEVICES_SPECIFIER = 0x1012;
    private const int ALC_ALL_DEVICES_SPECIFIER = 0x1013;
    private const int ALC_CONNECTED = 0x313;

    // opening the device happens once, from the warm-up or the first play, whichever comes first
    private readonly object openAl = new();
    private AL? al;
    private ALContext? alc;
    private unsafe Device* device;
    private unsafe Context* context;
    // alcReopenDeviceSOFT (ALC_SOFT_reopen_device); null when OpenAL has no such function
    private unsafe delegate* unmanaged[Cdecl]<Device*, byte*, int*, byte> reopenDevice;

    public override unsafe void Dispose()
    {
        base.Dispose();
        if (this.alc is { } alc)
        {
            alc.MakeContextCurrent((Context*)null);
            if (this.context != null)
                alc.DestroyContext(this.context);
            if (this.device != null)
                alc.CloseDevice(this.device);
        }
    }

    protected override void Play(PlayRequest request, CancellationToken ct)
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
                this.ReportDuration(reader.Duration);

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
                    var (wantPause, seek) = this.TakeRequests();

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
                        this.ReportPosition(request.TrackId, position);
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
                this.ReportFinished(request.TrackId);
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

    public override void WarmUp() => _ = Task.Run(() =>
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
            this.Log.LogInformation("Audio output: {Device}", alc.GetContextProperty(device, (GetContextString)ALC_ALL_DEVICES_SPECIFIER));
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
            var moved = this.reopenDevice(this.device, null, null) != 0;
            this.Log.LogInformation("Audio output {Result} from {From} to {To} (connected: {Connected})",
                moved ? "moved" : "could not move", current, wanted, connected != 0);
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
        if (!NativeLibrary.TryLoad(name, typeof(OpenAlPlayerService).Assembly, null, out var handle))
            return (ALContext.GetApi(true), AL.GetApi(true));

        var exports = new LamdaNativeContext(symbol => NativeLibrary.TryGetExport(handle, symbol, out var address) ? address : 0);
        return (new ALContext(exports), new AL(exports));
    }
}
