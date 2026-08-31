using System.Diagnostics;

namespace Viu.Emporix.SmokeTest;

/// <summary>What one step of the flow did.</summary>
internal sealed record Step(StepOutcome Outcome, string Detail, string? Value = null)
{
    public static Step Ok(string detail, string? value = null) => new(StepOutcome.Ok, detail, value);

    /// <summary>Succeeded, but the answer was empty in a way worth looking at.</summary>
    public static Step Empty(string detail) => new(StepOutcome.Empty, detail);

    public static Step Failed(string detail) => new(StepOutcome.Failed, detail);

    public static Step Skipped(string detail) => new(StepOutcome.Skipped, detail);
}

internal enum StepOutcome
{
    Ok,
    Empty,
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
            // The correlation id is what makes this findable in Emporix's own
            // logs, so it belongs in the output even though nothing else does.
            _failed++;
            Write("FAIL", name, $"{(int)exception.StatusCode} {exception.Message}", started);
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
    /// An empty answer is not a failure — a tenant may genuinely have no prices
    /// configured — but it is reported separately so it cannot pass unnoticed.
    /// </remarks>
    public int Report()
    {
        if (_failed > 0)
        {
            Console.WriteLine($"{_failed} step(s) failed. The package is not ready to publish.");
            return 1;
        }

        if (_empty > 0)
        {
            Console.WriteLine($"All steps passed, but {_empty} returned nothing. Check the note above each.");
            return 0;
        }

        Console.WriteLine("All steps passed.");
        return 0;
    }

    private static void Write(string status, string name, string detail, long started)
        => Console.WriteLine(
            $"  {status,-5} {name,-38} {detail}  ({Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms)");
}
