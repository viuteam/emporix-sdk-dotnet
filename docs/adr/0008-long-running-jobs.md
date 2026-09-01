# ADR-0008 — Long-running jobs: one waiting helper, no job abstraction

**Status:** Implemented · **Date:** 2026-09-01 · Affects: [ADR-0005](0005-resilience.md)

## Context

Several Emporix operations start work and answer with a job rather than a
result. Four such endpoints are already in the SDK or arrive in wave 5:

| Service | Response type | Fields |
| --- | --- | --- |
| product | `DynamicVariantRecalculationJobResponse` | `Id`, `Status`, `CreatedAt`, … |
| invoice | `JobStatusResponse` | `JobStatus`, `JobType`, `Orders` |
| indexing | `ReindexJob` | `Id`, `Status`, `ExpectedCount`, `ProcessedCount` |
| ai | `Job` | `Id`, `Status`, `Type`, `ExportResult`, … |

They have nothing in common that a type system can use. Three call the field
`Status` and one calls it `JobStatus`. The three `Status` fields are three
different enums — `DynamicVariantRecalculationJobStatus`, `ReindexJobStatus`,
`JobStatus` — with no shared base, because they come from four independent
specifications.

`Products.GetRecalculationJobAsync` already ships as a bare call, so callers are
polling by hand today.

## Options

| Option | For | Against |
| --- | --- | --- |
| **A waiting helper over a delegate** | Backoff, timeout and cancellation written once and tested once | The caller still says what «done» means |
| A `IEmporixJob` interface the generated types implement | `WaitAsync(job)` reads well | The generated types are regenerated on every sync; making them implement anything means the pipeline edits them, and the four shapes still disagree on the field name |
| Nothing — every caller polls | No SDK surface at all | Everyone rediscovers backoff, and the ones who do not hammer the API |

## Decision

**One helper, no job abstraction.** `EmporixPolling.WaitForAsync` takes a
delegate that fetches the state and a predicate that recognises the end:

```csharp
var job = await EmporixPolling.WaitForAsync(
    poll: ct => client.Products.GetRecalculationJobAsync(jobId, cancellationToken: ct),
    isComplete: j => j?.Status is not (JobStatus.PENDING or JobStatus.PROCESSING),
    cancellationToken: cancellationToken);
```

What the SDK contributes is the part that is easy to get wrong: an interval that
grows, a ceiling on it, an overall timeout distinct from cancellation, and a
`TaskCanceledException` that says which of the two ended the wait. What it does
not contribute is an opinion about what a job is.

Defaults follow [ADR-0005](0005-resilience.md), because a poll is the same
politeness problem as a retry: start at one second, double to a ceiling of
thirty, no jitter — unlike a retry, concurrent pollers are not a thundering
herd, they are separate jobs.

## Why not the interface

It reads better at the call site and it costs the wrong thing. The generated
types are rewritten on every specification sync; making them implement an SDK
interface means the generation pipeline edits them to add it — a fifth
post-processing rule, on top of the four that already exist, for a cosmetic
gain. And it would still not work: `JobStatusResponse` has no `Status` at all,
so the interface would need a per-type adapter, which is the predicate again
wearing a hat.

## Consequences

- The four job endpoints stay plain calls that return what the API returns. No
  wrapper type appears in a signature.
- One class, about forty lines, tested on its own: it grows the interval, honours
  a timeout, honours cancellation, and tells the two apart.
- A caller who does not want to wait simply does not call it — polling stays
  opt-in, which matters because for a long import the right answer is often a
  webhook rather than a poll.
- If a fifth job endpoint arrives with a shape that finally matches the others,
  this decision can be revisited with evidence rather than anticipation.
