using Android.Content;
using Android.Media;
using Microsoft.Extensions.Logging;
using Tapedeck.Core.Playback;
using Tapedeck.Core.Services;
using Tapedeck.Resources;
using Tapedeck.Services;

namespace Tapedeck.Android.Services;

/// <summary>
/// The phone's player: Opus decoded by <see cref="OpusPcmReader"/>, streamed to an <see cref="AudioTrack"/> in short
/// blocks. The background loop owns the track; it writes a block, then looks at pause, seek and stop.
/// It asks for the audio focus while it plays and pauses when it loses it (a call, another app) or when the
/// headphones are unplugged, like every music app.
/// </summary>
public sealed class AndroidPlayerService(ILogger<AndroidPlayerService> log) : PlayerService(log)
{
    // a tenth of a second per write: pause, seek and stop answer that quickly
    private const int BLOCK_SAMPLES = OpusPcmReader.SAMPLE_RATE / 10 * OpusPcmReader.CHANNELS;
    // half a second queued in the track, enough for a busy phone
    private const int BUFFER_BYTES = OpusPcmReader.SAMPLE_RATE / 2 * OpusPcmReader.CHANNELS * sizeof(short);
    private static readonly TimeSpan TICK = TimeSpan.FromMilliseconds(30);

    /// <summary>An AudioTrack needs no device to be opened first.</summary>
    public override void WarmUp()
    {
    }

    protected override void Play(PlayRequest request, CancellationToken ct)
    {
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

        var attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Music)!
            .Build()!;
        AudioTrack track;
        try
        {
            track = CreateTrack(attributes);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
        {
            reader.Dispose();
            this.Fail(request.TrackId, string.Format(Strings.Player_NoDevice, ex.Message));
            return;
        }

        var context = global::Android.App.Application.Context;
        var audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
        var focus = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
            .SetAudioAttributes(attributes)
            .SetOnAudioFocusChangeListener(new FocusListener(this.PauseForSystem))
            .Build()!;
        var noisy = new NoisyReceiver(this.PauseForSystem);
        context.RegisterReceiver(noisy, new IntentFilter(AudioManager.ActionAudioBecomingNoisy), ReceiverFlags.NotExported);
        audio.RequestAudioFocus(focus);

        using (reader)
        using (track)
        {
            if (reader.Duration > TimeSpan.Zero)
                this.ReportDuration(reader.Duration);

            // the same leveling the phone's music player does with the track gain; the track's volume only goes
            // down to quieter, a boost scales the samples
            var gain = (float)LoudnessMeter.PlaybackGain(request.LoudnessLufs);
            track.SetVolume(Math.Min(gain, 1f));
            var boost = Math.Max(gain, 1f);

            var start = TimeSpan.Zero;
            // frames written since the last seek, and where the playback head stood then
            long written = 0;
            long headAtStart = 0;
            var paused = false;
            var lastPosition = DateTime.MinValue;
            var finished = false;

            try
            {
                track.Play();
                while (!ct.IsCancellationRequested)
                {
                    var (wantPause, seek) = this.TakeRequests();

                    if (seek is { } target)
                    {
                        track.Pause();
                        track.Flush();
                        start = reader.Seek(target);
                        written = 0;
                        headAtStart = track.PlaybackHeadPosition;
                        if (!wantPause)
                            track.Play();
                        paused = wantPause;
                    }

                    if (wantPause != paused)
                    {
                        if (wantPause)
                        {
                            track.Pause();
                        }
                        else
                        {
                            // the focus may have gone to another app while paused
                            audio.RequestAudioFocus(focus);
                            track.Play();
                        }
                        paused = wantPause;
                    }

                    var played = track.PlaybackHeadPosition - headAtStart;
                    if (DateTime.UtcNow - lastPosition >= POSITION_INTERVAL)
                    {
                        this.ReportPosition(request.TrackId, start + TimeSpan.FromSeconds((double)played / OpusPcmReader.SAMPLE_RATE));
                        lastPosition = DateTime.UtcNow;
                    }

                    if (paused)
                    {
                        Thread.Sleep(TICK);
                        continue;
                    }

                    if (reader.Read(BLOCK_SAMPLES) is { } block)
                    {
                        if (boost > 1f)
                            Amplify(block, boost);
                        // blocks until the track has room, at most about one block
                        track.Write(block, 0, block.Length);
                        written += block.Length / OpusPcmReader.CHANNELS;
                        continue;
                    }

                    // everything is written: wait until the track has played it, then unload
                    if (played >= written)
                    {
                        finished = true;
                        break;
                    }
                    Thread.Sleep(TICK);
                }
            }
            finally
            {
                track.Stop();
                track.Release();
                audio.AbandonAudioFocusRequest(focus);
                context.UnregisterReceiver(noisy);
            }

            if (finished)
                this.ReportFinished(request.TrackId);
        }
    }

    private static AudioTrack CreateTrack(AudioAttributes attributes)
    {
        var minimum = AudioTrack.GetMinBufferSize(OpusPcmReader.SAMPLE_RATE, ChannelOut.Stereo, Encoding.Pcm16bit);
        return new AudioTrack.Builder()
            .SetAudioAttributes(attributes)
            .SetAudioFormat(new AudioFormat.Builder()
                .SetSampleRate(OpusPcmReader.SAMPLE_RATE)!
                .SetEncoding(Encoding.Pcm16bit)!
                .SetChannelMask(ChannelOut.Stereo)!
                .Build()!)
            .SetBufferSizeInBytes(Math.Max(minimum, BUFFER_BYTES))
            .SetTransferMode(AudioTrackMode.Stream)
            .Build();
    }

    /// <summary>Pauses when another app takes the sound, for a moment or for good; ducking is left to the system.</summary>
    private sealed class FocusListener(Action pause) : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus focusChange)
        {
            if (focusChange is AudioFocus.Loss or AudioFocus.LossTransient)
                pause();
        }
    }

    /// <summary>Unplugged headphones or a disconnected Bluetooth headset: pause instead of playing out loud.</summary>
    private sealed class NoisyReceiver(Action pause) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == AudioManager.ActionAudioBecomingNoisy)
                pause();
        }
    }

    /// <summary>Raises quiet tracks; samples beyond the range are clipped instead of wrapping around.</summary>
    private static void Amplify(short[] block, float factor)
    {
        for (var i = 0; i < block.Length; i++)
            block[i] = (short)Math.Clamp(block[i] * factor, short.MinValue, short.MaxValue);
    }
}
