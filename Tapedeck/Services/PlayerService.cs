using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Tapedeck.Services;

/// <summary>What the player needs to play a track and to show it in the player bar.</summary>
/// <param name="FilePath">Absolute path of the audio file.</param>
/// <param name="Cover">Square cover or thumbnail for the bar; <c>null</c> shows a placeholder.</param>
/// <param name="Duration">Known length, until the file tells its own.</param>
/// <param name="LoudnessLufs">Measured loudness, so all tracks play equally loud; <c>null</c> plays the file as it is.</param>
public sealed record PlayRequest(
    long TrackId, string FilePath, string Title, string Artist, string? Cover, TimeSpan Duration, double? LoudnessLufs = null);

/// <summary>
/// Plays one track at a time, for listening in. This part holds what the views see (track, position, playing)
/// and the requests from the UI; each platform plays the audio in its own loop on a background thread
/// (<see cref="Play"/>), which picks up pause and seek through <see cref="TakeRequests"/>.
/// The audio file is open while a track is loaded, so callers stop it before writing or moving the file.
/// </summary>
public abstract partial class PlayerService(ILogger log) : ObservableObject, IDisposable
{
    // often enough that a lyrics line changes with the singing, not a beat late
    protected static readonly TimeSpan POSITION_INTERVAL = TimeSpan.FromMilliseconds(100);

    // property changes are raised on the UI thread the service was created on
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private readonly object requests = new();

    private CancellationTokenSource? run;
    private Task? loop;
    // requests from the UI thread to the loop
    private bool pauseRequested;
    private TimeSpan? seekRequest;

    protected ILogger Log => log;

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

    /// <summary>
    /// Opens the audio output in the background, so the first click plays right away instead of after a second.
    /// A missing output is reported only when the user actually plays.
    /// </summary>
    public abstract void WarmUp();

    public virtual void Dispose()
    {
        this.run?.Cancel();
        this.loop?.Wait(TimeSpan.FromSeconds(1));
        GC.SuppressFinalize(this);
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
    protected abstract void Play(PlayRequest request, CancellationToken ct);

    /// <summary>For the loop: whether the user wants it paused, and a seek target it has not handled yet.</summary>
    protected (bool Pause, TimeSpan? Seek) TakeRequests()
    {
        lock (this.requests)
        {
            var seek = this.seekRequest;
            this.seekRequest = null;
            return (this.pauseRequested, seek);
        }
    }

    /// <summary>For the platform: something else wants the sound (a call, another app, unplugged headphones), so pause.</summary>
    protected void PauseForSystem() => this.Post(() =>
    {
        if (this.IsPlaying)
            this.TogglePause();
    });

    /// <summary>For the loop: the length the file itself tells.</summary>
    protected void ReportDuration(TimeSpan duration) => this.Post(() => this.Duration = duration);

    /// <summary>For the loop: where playback is now.</summary>
    protected void ReportPosition(long trackId, TimeSpan position) => this.Post(() =>
    {
        if (this.CurrentTrackId == trackId)
            this.Position = position;
    });

    /// <summary>For the loop: played to the end, so unload; the bar disappears.</summary>
    protected void ReportFinished(long trackId) => this.Post(() =>
    {
        if (this.CurrentTrackId != trackId)
            return;
        this.run?.Dispose();
        this.run = null;
        this.loop = null;
        this.CurrentTrackId = null;
        this.IsPlaying = false;
        this.Position = TimeSpan.Zero;
    });

    /// <summary>For the loop: the track cannot be played; the text says why.</summary>
    protected void Fail(long trackId, string message)
    {
        log.LogWarning("Playback of track {TrackId} failed: {Message}", trackId, message);
        this.Post(() => this.OnFailed(trackId, message));
    }

    private void OnFailed(long trackId, string message)
    {
        if (this.CurrentTrackId == trackId)
        {
            this.CurrentTrackId = null;
            this.IsPlaying = false;
        }
        this.Failed?.Invoke(message);
    }

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
