using System.ComponentModel;
using Android.Content;
using AndroidX.Core.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Resources;
using Tapedeck.Services;
using AndroidApplication = Android.App.Application;

namespace Tapedeck.Android.Services;

/// <summary>
/// Runs the <see cref="WorkService"/> while the app is open or busy, and a while after.
/// Android starts a foreground service only while the app is visible, and cuts a background app off the network.
/// Work often begins seconds after the tap (looking for files, preparing the tools), when the user may have locked
/// the phone already; so the service starts as soon as the app is visible, not when the work begins. Every long task
/// shows the activity toast, so its busy state tells whether there is work; the notification shows the same text.
/// When work ends or the app is left, the <see cref="MediaIndex"/> picks up the changed files for other players.
/// </summary>
public sealed class WorkNotifier
{
    // one task often follows another (a download, then the MusicBrainz and lyrics queues)
    private static readonly TimeSpan LINGER = TimeSpan.FromSeconds(30);

    private readonly ActivityService activity;
    private readonly MediaIndex mediaIndex;
    private readonly ILogger log;
    private bool visible;
    private bool running;
    private CancellationTokenSource? stop;

    public WorkNotifier(ActivityService activity, MediaIndex mediaIndex, ILogger<WorkNotifier>? logger = null)
    {
        this.activity = activity;
        this.mediaIndex = mediaIndex;
        this.log = (ILogger?)logger ?? NullLogger.Instance;
        activity.PropertyChanged += this.OnActivityChanged;
        // files written in an earlier run, or copied onto the phone
        mediaIndex.Request();
    }

    /// <summary>The activity was shown or hidden (<c>OnStart</c>/<c>OnStop</c>).</summary>
    public void SetVisible(bool isVisible)
    {
        this.visible = isVisible;
        if (isVisible)
        {
            this.Start();
            return;
        }
        // the user may switch to a player right now
        this.mediaIndex.Request();
        if (!this.activity.IsBusy)
            _ = this.StopLaterAsync();
    }

    private string Text => this.activity.IsBusy && this.activity.Text is { } text ? text : Strings.Notification_Ready;

    private void OnActivityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActivityService.IsBusy))
        {
            if (!this.activity.IsBusy)
                this.mediaIndex.Request();
            if (this.activity.IsBusy)
                this.Start();
            else if (!this.visible)
                _ = this.StopLaterAsync();
            else if (this.running)
                WorkService.Update(AndroidApplication.Context, this.Text);
        }
        else if (e.PropertyName == nameof(ActivityService.Text) && this.running && this.activity.IsBusy)
        {
            WorkService.Update(AndroidApplication.Context, this.Text);
        }
    }

    private void Start()
    {
        this.stop?.Cancel();
        this.stop = null;
        var context = AndroidApplication.Context;
        if (this.running)
        {
            WorkService.Update(context, this.Text);
            return;
        }
        try
        {
            ContextCompat.StartForegroundService(context,
                new Intent(context, typeof(WorkService)).PutExtra(WorkService.EXTRA_TEXT, this.Text));
            this.running = true;
        }
        catch (Exception ex) when (ex is Java.Lang.IllegalStateException or Java.Lang.SecurityException)
        {
            // only when work begins in the background long after the app was left
            this.log.LogWarning("Could not start the work service: {Error}", ex.Message);
        }
    }

    private async Task StopLaterAsync()
    {
        this.stop?.Cancel();
        var cts = this.stop = new CancellationTokenSource();
        try
        {
            await Task.Delay(LINGER, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!this.running || this.visible || this.activity.IsBusy)
            return;
        var context = AndroidApplication.Context;
        context.StopService(new Intent(context, typeof(WorkService)));
        this.running = false;
    }
}
