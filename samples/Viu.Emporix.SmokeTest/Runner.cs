using System.Diagnostics;

namespace Viu.Emporix.SmokeTest;

/// <summary>What one step of the flow did.</summary>
internal sealed record Step(StepOutcome Outcome, string Detail, string? Value = null)
{
    public static Step Ok(string detail, string? value = null) => new(StepOutcome.Ok, detail, value);

    /// <summary>Succeeded, but the answer was empty in a way worth looking at.</summary>
    public static Step Empty(string detail) => new(StepOutcome.Empty, detail);

    /// <summary>The call was understood and the token was not allowed to make it.</summary>
    public static Step Forbidden(string detail) => new(StepOutcome.Forbidden, detail);

    public static Step Failed(string detail) => new(StepOutcome.Failed, detail);

    public static Step Skipped(string detail) => new(StepOutcome.Skipped, detail);
}

internal enum StepOutcome
{
    Ok,
    Empty,

    /// <summary>
    /// A <c>403</c>: the address exists, the request was understood, and this
    /// token may not make it.
    /// </summary>
    /// <remarks>
    /// Kept apart from a failure because it says something different. A wrong
    /// address answers <c>404</c>; a wrong body answers <c>400</c>. A <c>403</c>
    /// says the SDK got it right and the client is missing a scope, which is a
    /// tenant's configuration and not this package's problem. Counting it as a
    /// failure would have the smoke test refuse to publish over someone else's
    /// permissions.
    /// </remarks>
    Forbidden,
    Failed,
    Skipped,
}

/// <summary>
/// Runs the steps, prints what happened, and decides the exit code.
/// </summary>
/// <remarks>
/// A failing step does not stop the run: knowing that four of nine calls are
/// broken is worth more than stopping at the first one, and the later steps
/// skip themselves when their input is missing.
/// </remarks>
internal sealed class Runner
{
    private int _failed;
    private int _empty;
    private int _forbidden;

    /// <summary>Runs one step and returns whatever it produced for the next one.</summary>
    public async Task<string?> RunAsync(string name, Func<Task<Step>> step)
    {
        ArgumentNullException.ThrowIfNull(step);

        long started = Stopwatch.GetTimestamp();
        Step result;

        try
        {
            result = await step().ConfigureAwait(false);
        }
        catch (EmporixApiException exception)
        {
            bool forbidden = exception.StatusCode == System.Net.HttpStatusCode.Forbidden;

            if (forbidden)
            {
                _forbidden++;
            }
            else
            {
                _failed++;
            }

            Write(
                forbidden ? "SCOPE" : "FAIL",
                name,
                $"{(int)exception.StatusCode} {exception.Message}",
                started);

            // The correlation id is what makes this findable in Emporix's own
            // logs, so it belongs in the output even though nothing else does.
            Console.WriteLine($"       correlation id: {exception.CorrelationId ?? "none"}");
            return null;
        }
        catch (EmporixException exception)
        {
            _failed++;
            Write("FAIL", name, exception.Message, started);
            return null;
        }

        switch (result.Outcome)
        {
            case StepOutcome.Ok:
                Write("ok", name, result.Detail, started);
                break;

            case StepOutcome.Empty:
                _empty++;
                Write("EMPTY", name, result.Detail, started);
                break;

            case StepOutcome.Forbidden:
                _forbidden++;
                Write("SCOPE", name, result.Detail, started);
                break;

            case StepOutcome.Failed:
                _failed++;
                Write("FAIL", name, result.Detail, started);
                break;

            case StepOutcome.Skipped:
                Write("skip", name, result.Detail, started);
                break;

            default:
                throw new InvalidOperationException($"Unhandled outcome {result.Outcome}.");
        }

        return result.Value;
    }

    /// <summary>Prints the summary and returns the exit code.</summary>
    /// <remarks>
    /// Three outcomes short of a plain pass, and only one of them is this
    /// package's fault. An empty answer means the tenant has nothing of that
    /// kind configured; a <c>403</c> means the client is missing a scope. Both
    /// are reported so they cannot pass unnoticed, and neither fails the run.
    /// </remarks>
    public int Report()
    {
        if (_failed > 0)
        {
            Console.WriteLine($"{_failed} step(s) failed. The package is not ready to publish.");
            return 1;
        }

        if (_forbidden > 0)
        {
            Console.WriteLine(
                $"No step failed. {_forbidden} were refused for a missing scope (SCOPE) — "
                + "the addresses are right, the client credentials are not entitled.");
        }

        if (_empty > 0)
        {
            Console.WriteLine($"{_empty} step(s) returned nothing. Check the note above each.");
        }

        if (_forbidden == 0 && _empty == 0)
        {
            Console.WriteLine("All steps passed.");
        }

        return 0;
    }

    private static void Write(string status, string name, string detail, long started)
        => Console.WriteLine(
            $"  {status,-5} {name,-38} {detail}  ({Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms)");
}
