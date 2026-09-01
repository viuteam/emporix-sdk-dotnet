# ADR-0009 — Cloud functions: the caller brings the types

**Status:** Decided · **Date:** 2026-09-01 · Affects: [ADR-0004](0004-aot-trimming.md)

## Context

`POST /cloud-functions/{tenant}/functions/{functionId}` invokes a function the
tenant wrote. There is no schema: the request and response shapes belong to
whoever deployed the function, and Emporix vendors no specification for them —
it is the one service in the API with no generated types at all.

Every other call in this SDK serialises through a source-generated
`JsonSerializerContext`, because [ADR-0004](0004-aot-trimming.md) forbids
reflection. That rule and «arbitrary caller-defined types» are in direct tension,
and this is the only place in 48 services where they meet.

The Node SDK solves it with `invoke<TRes, TReq>` and TypeScript generics, which
costs nothing there because JSON.parse needs no type information at runtime. In
.NET without reflection it costs something: `JsonSerializer` needs a
`JsonTypeInfo<T>`, and only the caller can supply one for their own type.

## Options

| Option | For | Against |
| --- | --- | --- |
| **`JsonTypeInfo<T>` parameters** | Honest about the constraint, fully AOT-safe, the caller's own context does the work | An unusual signature; the caller must have a serialization context, which an AOT consumer has anyway |
| `JsonElement` in and out | No unusual parameters | The caller serialises twice — their type to `JsonElement`, then `JsonElement` to bytes — and reads results without types |
| `[RequiresUnreferencedCode]` and reflection | Familiar generic signature | Breaks the AOT promise for the whole assembly's trim analysis; a consumer publishing AOT gets warnings from a call they may never make |
| Leave the service out | Zero surface | The API has it, and a caller then hand-builds the request against the `HttpClient` — which is the same work without the auth handling |

## Decision

**The caller passes the type information.** Two overloads, plus a raw escape
hatch:

```csharp
// Typed, AOT-safe: the caller's own context supplies both.
TResponse? result = await client.CloudFunctions.InvokeAsync(
    "my-function",
    request,
    MyJsonContext.Default.MyRequest,
    MyJsonContext.Default.MyResponse);

// Untyped, for a function whose shape is decided at runtime.
JsonElement result = await client.CloudFunctions.InvokeAsync("my-function", payload);

// Raw, for a function that answers with something other than JSON.
using HttpResponseMessage response = await client.CloudFunctions.InvokeRawAsync(
    "my-function", content);
```

The signature is unusual, and it is unusual because the situation is. A method
that looked like `InvokeAsync<TReq, TRes>` and quietly used reflection would be
familiar and would break the promise the rest of the package makes.

## Consequences

- The SDK still contributes what it is for on this call: the tenant in the path,
  the token on the request, retry, error translation and the correlation id. A
  caller dropping to `HttpClient` loses all of that.
- `InvokeAsync` is **not** repeatable. A cloud function is arbitrary code; the
  SDK cannot know whether running it twice is safe, and assuming it is would be
  the one place where the idempotency gate guesses.
- The default auth is anonymous, matching the Node SDK: a cloud function is
  often a public endpoint, and a caller who needs otherwise passes a context like
  everywhere else.
- If .NET ever gains reflection-free generic serialisation without a
  `JsonTypeInfo`, this ADR is the one to revisit.
