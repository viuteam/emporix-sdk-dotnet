using Microsoft.Extensions.Time.Testing;

namespace Viu.Emporix.Tests;

/// <summary>
/// The waiting helper from ADR-0008.
/// </summary>
/// <remarks>
/// The parts worth pinning are the ones that are easy to get wrong: not
/// sleeping before the first look, growing the interval to a ceiling and no
/// further, and keeping a timeout distinguishable from a cancellation.
/// </remarks>
public class EmporixPollingTests
{
    private static readonly EmporixPollingOptions Fast = new()
    {
        InitialInterval = TimeSpan.FromSeconds(1),
        MaximumInterval = TimeSpan.FromSeconds(8),
        Timeout = TimeSpan.FromMinutes(1),
    };


    /// <summary>
    /// Runs the wait while driving the clock forward.
    /// </summary>
    /// <remarks>
    /// A fake time provider only moves when a test moves it, and the operation
    /// under test is asleep on a timer that will not fire until it does. So the
    /// test pumps: yield to let the wait reach its next delay, advance a
    /// second, repeat. Real time does not pass, so a wait of minutes finishes
    /// in milliseconds.
    /// </remarks>
    private static async Task<T> PumpAsync<T>(Task<T> operation, FakeTimeProvider time)
    {
        while (!operation.IsCompleted)
        {
            await Task.Yield();
            time.Advance(TimeSpan.FromSeconds(1));
        }

        return await operation;
    }

    [Fact]
    public async Task A_job_that_is_already_done_costs_no_waiting()
    {
        // The first poll happens immediately. Sleeping a second to discover
        // that nothing needed waiting for is the most common way to make a
        // helper like this feel slow.
        FakeTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        int calls = 0;

        string result = await EmporixPolling.WaitForAsync(
            _ => { calls++; return Task.FromResult("DONE"); },
            state => state == "DONE",
            Fast,
            time);

        Assert.Equal("DONE", result);
        Assert.Equal(1, calls);
        Assert.Equal(TimeSpan.Zero, time.GetUtcNow() - start);
    }

    [Fact]
    public async Task It_polls_until_the_condition_holds()
    {
        FakeTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        string[] states = ["PENDING", "PROCESSING", "DONE"];
        int index = 0;

        string result = await PumpAsync(
            EmporixPolling.WaitForAsync(
                _ => Task.FromResult(states[index++]),
                state => state == "DONE",
                Fast,
                time),
            time);

        Assert.Equal("DONE", result);
        Assert.Equal(3, index);
    }

    [Fact]
    public async Task The_interval_doubles_and_then_stops_doubling()
    {
        // 1, 2, 4, 8, 8 — not 1, 2, 4, 8, 16. An unbounded doubling turns a
        // long job into a wait measured in hours.
        FakeTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        List<TimeSpan> waits = [];
        TimeSpan last = TimeSpan.Zero;
        int calls = 0;

        await PumpAsync(
            EmporixPolling.WaitForAsync(
                _ =>
                {
                    if (calls++ > 0)
                    {
                        waits.Add(time.GetUtcNow() - start - last);
                        last = time.GetUtcNow() - start;
                    }

                    return Task.FromResult(calls > 6 ? "DONE" : "PENDING");
                },
                state => state == "DONE",
                Fast,
                time),
            time);

        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
             TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8)],
            waits);
    }

    [Fact]
    public async Task A_timeout_is_reported_before_sleeping_past_it()
    {
        // Waiting out the last interval only to announce the deadline had
        // already passed wastes the caller's time for no information.
        FakeTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await PumpAsync(
                EmporixPolling.WaitForAsync(
                    _ => Task.FromResult("PENDING"),
                    state => state == "DONE",
                    new EmporixPollingOptions
                    {
                        InitialInterval = TimeSpan.FromSeconds(5),
                        MaximumInterval = TimeSpan.FromSeconds(5),
                        Timeout = TimeSpan.FromSeconds(12),
                    },
                    time),
                time));

        // Two waits of five seconds; the third would have crossed twelve.
        Assert.Equal(TimeSpan.FromSeconds(10), time.GetUtcNow() - start);
    }

    [Fact]
    public async Task A_timeout_and_a_cancellation_are_different_exceptions()
    {
        // The caller handles them differently: a timeout means the job is slow,
        // a cancellation means nobody is waiting any more.
        FakeTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await EmporixPolling.WaitForAsync(
                token => { token.ThrowIfCancellationRequested(); return Task.FromResult("PENDING"); },
                state => state == "DONE",
                Fast,
                time,
                cancellation.Token));
    }

    [Fact]
    public async Task A_failing_poll_is_not_swallowed()
    {
        // Retrying a broken call forever, hidden inside a wait, is worse than
        // failing: the retry handler already decides what is worth repeating.
        FakeTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();

        await Assert.ThrowsAsync<EmporixNotFoundException>(async () =>
            await EmporixPolling.WaitForAsync<string>(
                _ => throw new EmporixNotFoundException("no such job", null, [], null),
                _ => true,
                Fast,
                time));
    }

    [Theory]
    [InlineData(0, 8, 60)]
    [InlineData(1, 0, 60)]
    [InlineData(1, 8, 0)]
    public async Task Nonsensical_options_are_rejected(int initial, int maximum, int timeout)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await EmporixPolling.WaitForAsync(
                _ => Task.FromResult("DONE"),
                _ => true,
                new EmporixPollingOptions
                {
                    InitialInterval = TimeSpan.FromSeconds(initial),
                    MaximumInterval = TimeSpan.FromSeconds(maximum),
                    Timeout = TimeSpan.FromSeconds(timeout),
                },
                new FakeTimeProvider()));
    }
}
