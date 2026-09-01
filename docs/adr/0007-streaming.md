# ADR-0007 — Streaming responses: expose them, do not wrap them

**Status:** Implemented · **Date:** 2026-09-01 · Affects: [ADR-0004](0004-aot-trimming.md)

## Context

Wave 5 covers the AI and import services, and both stream. The
[roadmap](../roadmap.md) listed this as a blocking decision on the strength of a
line in the Phase 0 analysis: `IAsyncEnumerable<SseEvent>`, scope V1.x. Nothing
was ever built.

Measured against the specifications rather than the assumption:

| | Operations | of which stream |
| --- | ---: | ---: |
| `ai-service` | 57 | 2 |
| `import-service` | 20 | 1 |
| `indexing-service` | 11 | 0 |

Three operations. `POST /ai-service/{tenant}/agentic/chat` and its
`chat-stream` sibling, and `GET /importtool/{tenant}/runs/{runId}/events`. Every
other operation in those services answers ordinary JSON.

The second measurement settles the rest. `System.Net.ServerSentEvents` — a
Microsoft package, 35 million downloads — is **part of the .NET 10 shared
framework**. Referencing it explicitly produces:

> NU1510: PackageReference System.Net.ServerSentEvents is not trimmed. This
> package is automatically available and does not need to be referenced
> explicitly.

There is no dependency to take, and there is a parser already in the box.

## Options

| Option | For | Against |
| --- | --- | --- |
| **Expose the response, parse with the framework** | No new type, no dependency, no maintenance. `SendRawAsync` already returns the response unread. | The caller writes three lines to get an event stream |
| An `IAsyncEnumerable<SseEvent>` wrapper in the SDK | One line at the call site | Our own event type, our own reconnection policy, our own cancellation semantics — for three endpoints, duplicating a framework type |
| A dependency on an SSE library | Battle-tested | Reinvents what `net10.0` already ships |

## Decision

**No streaming abstraction.** The three streaming operations return
`HttpResponseMessage` through the existing `SendRawAsync`, undisposed, and the
caller reads it:

```csharp
using HttpResponseMessage response = await client.Ai.ChatStreamAsync(request);
await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

await foreach (SseItem<string> item in SseParser.Create(stream).EnumerateAsync(cancellationToken))
{
    Console.Write(item.Data);
}
```

Three lines, using a parser Microsoft maintains, with cancellation and
disposal that behave the way every other .NET stream does.

## Why not the wrapper

The wrapper looked obvious in the analysis and stopped looking obvious once the
numbers were in. It would have meant deciding, on behalf of every caller:

- what an event type looks like, when `SseItem<T>` already exists;
- whether a dropped connection is an exception or an end;
- whether to reconnect, and with what backoff;
- how `Last-Event-ID` is tracked.

Those are the caller's decisions, they differ between a chat UI and an import
monitor, and none of them is ours to make for three endpoints.

## Consequences

- Wave 5 needs no new infrastructure. It was the only wave the roadmap listed as
  blocked on building something, and it is not.
- The SDK's dependency count stays where [ADR-0003](0003-packaging.md) put it.
- A caller who wants an `IAsyncEnumerable` writes one extension method over
  `SseParser`. If several do, the evidence for adding it will exist — which is
  the right order.
- The three streaming methods are documented as returning an unread response, in
  the same terms as `Media.DownloadAsync` and `Quotes.RenderPdfAsync`, which
  already work this way.
