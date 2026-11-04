using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tapedeck.Services;

/// <summary>A running activity: change its text while it runs, dispose it when the work is done.</summary>
public interface IActivity : IDisposable
{
    void Update(string text);
}

/// <summary>
/// What the app is busy with, shown app-wide as a toast with a spinning record,
/// and a short confirmation once the work is done.
/// </summary>
public sealed partial class ActivityService : ObservableObject
{
    private static readonly TimeSpan CONFIRMATION_DURATION = TimeSpan.FromSeconds(3);

    private readonly List<Activity> activities = [];
    private CancellationTokenSource? confirmation;

    /// <summary>Current activity or confirmation; <c>null</c> hides the toast.</summary>
    [ObservableProperty]
    public partial string? Text { get; private set; }

    /// <summary>Work is in progress (the record spins).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Shows <paramref name="text"/> with a spinning record until the returned activity is disposed.</summary>
    public IActivity Begin(string text)
    {
        var activity = new Activity(this, text);
        this.activities.Add(activity);
        this.Refresh();
        return activity;
    }

    /// <summary>A short confirmation with a resting record, unless other work is still running.</summary>
    public void Confirm(string text) => _ = this.ConfirmAsync(text);

    private void Refresh()
    {
        this.IsBusy = this.activities.Count > 0;
        if (this.IsBusy)
        {
            this.confirmation?.Cancel();
            this.confirmation = null;
            this.Text = this.activities[^1].Text;
        }
        else if (this.confirmation is null)
        {
            this.Text = null;
        }
    }

    private async Task ConfirmAsync(string text)
    {
        if (this.IsBusy)
            return;

        this.confirmation?.Cancel();
        using var cts = this.confirmation = new CancellationTokenSource();
        this.Text = text;
        try
        {
            await Task.Delay(CONFIRMATION_DURATION, cts.Token);
            this.Text = null;
        }
        catch (OperationCanceledException)
        {
            // replaced by new work or another confirmation
        }
        finally
        {
            if (this.confirmation == cts)
                this.confirmation = null;
        }
    }

    private sealed class Activity(ActivityService owner, string text) : IActivity
    {
        public string Text { get; private set; } = text;

        public void Update(string text)
        {
            this.Text = text;
            owner.Refresh();
        }

        public void Dispose()
        {
            owner.activities.Remove(this);
            owner.Refresh();
        }
    }
}
