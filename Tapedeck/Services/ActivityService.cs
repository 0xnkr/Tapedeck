using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tapedeck.Services;

/// <summary>
/// What the app is busy with, shown app-wide as a toast with a spinning record,
/// and a short confirmation once the work is done.
/// </summary>
public sealed partial class ActivityService : ObservableObject
{
    private static readonly TimeSpan CONFIRMATION_DURATION = TimeSpan.FromSeconds(3);

    private readonly List<string> activities = [];
    private CancellationTokenSource? confirmation;

    /// <summary>Current activity or confirmation; <c>null</c> hides the toast.</summary>
    [ObservableProperty]
    public partial string? Text { get; private set; }

    /// <summary>Work is in progress (the record spins).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Shows <paramref name="text"/> with a spinning record until the returned handle is disposed.</summary>
    public IDisposable Begin(string text)
    {
        this.activities.Add(text);
        this.Update();
        return new Handle(() =>
        {
            this.activities.Remove(text);
            this.Update();
        });
    }

    /// <summary>A short confirmation with a resting record, unless other work is still running.</summary>
    public void Confirm(string text) => _ = this.ConfirmAsync(text);

    private void Update()
    {
        this.IsBusy = this.activities.Count > 0;
        if (this.IsBusy)
        {
            confirmation?.Cancel();
            this.confirmation = null;
            this.Text = this.activities[^1];
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

        confirmation?.Cancel();
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

    private sealed class Handle(Action end) : IDisposable
    {
        public void Dispose() => end();
    }
}
