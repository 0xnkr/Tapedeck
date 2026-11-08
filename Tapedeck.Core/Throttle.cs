namespace Tapedeck.Core;

/// <summary>Runs work for many items with only a few at once.</summary>
public static class Throttle
{
    /// <summary>
    /// Starts <paramref name="body"/> for the items in order, at most <paramref name="maxParallel"/> at a time, and
    /// waits until all started ones are done. Everything runs on the caller's context (the UI thread for a view
    /// model), so the bodies may touch view state without locks. Cancelling stops starting new items; the running
    /// ones end through their own token.
    /// </summary>
    public static async Task ForEachAsync<T>(IEnumerable<T> items, int maxParallel, Func<T, Task> body, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallel, 1);
        using var gate = new SemaphoreSlim(maxParallel);
        var running = new List<Task>();
        foreach (var item in items)
        {
            try
            {
                await gate.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            running.Add(RunAsync(item));
        }
        await Task.WhenAll(running);

        async Task RunAsync(T item)
        {
            try
            {
                await body(item);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
