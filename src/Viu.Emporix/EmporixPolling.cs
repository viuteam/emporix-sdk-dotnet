namespace Viu.Emporix;

/// <summary>How long to wait between polls, and for how long in total.</summary>
public sealed class EmporixPollingOptions
{
    /// <summary>The wait before the first re-check. Defaults to one second.</summary>
    public TimeSpan InitialInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between two checks. Defaults to thirty seconds.</summary>
    public TimeSpan MaximumInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to keep waiting before giving up. Defaults to ten minutes.
    /// </summary>
    /// <remarks>
    /// Distinct from cancellation on purpose: a timeout means the job took
    /// longer than expected, cancellation means the caller stopped caring, and
    /// the two want different handling.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Waits for something Emporix is doing in the background.
/// </summary>
/// <remarks>
/// <para>
/// Several Emporix calls start work and answer with a job rather than a result —
/// recalculating variants, generating invoices, reindexing, running an import.
/// This waits for one to finish.
/// </para>
/// <para>
/// It takes a delegate rather than a job type because the four job shapes in the
/// API share nothing a type system can use: three call the field
/// <c>Status</c> and one calls it <c>JobStatus</c>, and the three are three
/// unrelated enums. See <see href="../../docs/adr/0008-long-running-jobs.md">ADR-0008</see>.
/// </para>
/// <para>
/// For work that takes minutes rather than seconds, a webhook is usually the
/// better answer than polling at all.
/// </para>
/// </remarks>
public static class EmporixPolling
{
    /// <summary>Polls until the state satisfies a condition.</summary>
    /// <typeparam name="T">Whatever the poll returns.</typeparam>
    /// <param name="poll">Fetches the current state.</param>
    /// <param name="isComplete">Decides whether waiting is over.</param>
    /// <param name="options">How long to wait between polls, and in total.</param>
    /// <param name="timeProvider">The clock. Injected for tests.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The first state for which <paramref name="isComplete"/> held.</returns>
    /// <exception cref="TimeoutException">
    /// The condition was still not met when <see cref="EmporixPollingOptions.Timeout"/>
    /// elapsed.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled. Distinct from the timeout on purpose.
    /// </exception>
    /// <remarks>
    /// The first poll happens immediately: a job that is already finished should
    /// not cost a second of waiting.
    /// </remarks>
    public static async Task<T> WaitForAsync<T>(
        Func<CancellationToken, Task<T>> poll,
        Func<T, bool> isComplete,
        EmporixPollingOptions? options = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poll);
        ArgumentNullException.ThrowIfNull(isComplete);

        EmporixPollingOptions settings = options ?? new EmporixPollingOptions();
        TimeProvider clock = timeProvider ?? TimeProvider.System;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.InitialInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MaximumInterval, settings.InitialInterval);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.Timeout, TimeSpan.Zero);

        long started = clock.GetTimestamp();
        TimeSpan interval = settings.InitialInterval;

        while (true)
        {
            T state = await poll(cancellationToken).ConfigureAwait(false);

            if (isComplete(state))
            {
                return state;
            }

            TimeSpan elapsed = clock.GetElapsedTime(started);

            if (elapsed + interval >= settings.Timeout)
            {
                // Reported before waiting rather than after: sleeping past the
                // deadline only to announce it would waste the caller's time.
                throw new TimeoutException(
                    $"The operation did not complete within {settings.Timeout}.");
            }

            await Task.Delay(interval, clock, cancellationToken).ConfigureAwait(false);

            // Doubling, capped. No jitter, unlike the retry handler: two callers
            // polling are waiting on two different jobs, not colliding on one
            // recovering server.
            interval = interval < settings.MaximumInterval
                ? TimeSpan.FromTicks(Math.Min(interval.Ticks * 2, settings.MaximumInterval.Ticks))
                : settings.MaximumInterval;
        }
    }
}
