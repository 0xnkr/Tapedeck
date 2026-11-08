using Tapedeck.Core;

namespace Tapedeck.Tests;

/// <summary>Running work for many items with only a few at once, e.g. parallel downloads.</summary>
public sealed class ThrottleTests
{
    [Fact]
    public async Task Never_more_than_the_limit_run_at_once_and_all_finish()
    {
        var running = 0;
        var most = 0;
        var done = new List<int>();

        await Throttle.ForEachAsync(Enumerable.Range(1, 20), 3, async i =>
        {
            most = Math.Max(most, Interlocked.Increment(ref running));
            await Task.Delay(10 + i % 4 * 5);
            Interlocked.Decrement(ref running);
            lock (done)
                done.Add(i);
        });

        Assert.Equal(3, most);
        Assert.Equal(Enumerable.Range(1, 20), done.Order());
    }

    [Fact]
    public async Task Items_start_in_order()
    {
        var started = new List<int>();

        await Throttle.ForEachAsync(Enumerable.Range(1, 6), 2, async i =>
        {
            lock (started)
                started.Add(i);
            await Task.Delay(5);
        });

        Assert.Equal([1, 2, 3, 4, 5, 6], started);
    }

    [Fact]
    public async Task Cancelling_starts_no_new_items_but_lets_running_ones_finish()
    {
        using var cts = new CancellationTokenSource();
        var finished = 0;

        await Throttle.ForEachAsync(Enumerable.Range(1, 10), 2, async i =>
        {
            if (i == 2)
                cts.Cancel();
            await Task.Delay(20);
            Interlocked.Increment(ref finished);
        }, cts.Token);

        Assert.Equal(2, finished);
    }

    [Fact]
    public async Task A_limit_below_one_is_refused() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Throttle.ForEachAsync([1], 0, _ => Task.CompletedTask));
}
