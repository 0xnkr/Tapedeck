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
    private static readonly TimeSpan ConfirmationDuration = TimeSpan.FromSeconds(3);

    private readonly List<string> _activities = [];
    private CancellationTokenSource? _confirmation;

    /// <summary>Current activity or confirmation; <c>null</c> hides the toast.</summary>
    [ObservableProperty]
    public partial string? Text { get; private set; }

    /// <summary>Work is in progress (the record spins).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Shows <paramref name="text"/> with a spinning record until the returned handle is disposed.</summary>
    public IDisposable Begin(string text)
    {
        _activities.Add(text);
        Update();
        return new Handle(() =>
        {
            _activities.Remove(text);
            Update();
        });
    }

    /// <summary>A short confirmation with a resting record, unless other work is still running.</summary>
    public void Confirm(string text) => _ = ConfirmAsync(text);

    private void Update()
    {
        IsBusy = _activities.Count > 0;
        if (IsBusy)
        {
            _confirmation?.Cancel();
            _confirmation = null;
            Text = _activities[^1];
        }
        else if (_confirmation is null)
        {
            Text = null;
        }
    }

    private async Task ConfirmAsync(string text)
    {
        if (IsBusy)
            return;

        _confirmation?.Cancel();
        using var cts = _confirmation = new CancellationTokenSource();
        Text = text;
        try
        {
            await Task.Delay(ConfirmationDuration, cts.Token);
            Text = null;
        }
        catch (OperationCanceledException)
        {
            // replaced by new work or another confirmation
        }
        finally
        {
            if (_confirmation == cts)
                _confirmation = null;
        }
    }

    private sealed class Handle(Action end) : IDisposable
    {
        public void Dispose() => end();
    }
}
