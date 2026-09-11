# Facade standard

How a wrapped endpoint looks in this repo. Copy the neighbouring method in the
file you are editing before copying anything here — the file you are in is the
most reliable style guide. This is the shape those files share, plus the traps
that only show up later.

## Where things go

| Change | Files |
|---|---|
| New operation on an existing service | the facade (`src/Viu.Emporix/<Svc>Service.cs`, or the grouped file the service lives in), its `[JsonSerializable]` entries in `JsonContexts.cs`, the service's tests, `PublicAPI.Unshipped.txt` |
| New request or response type | nothing — it comes from `src/Viu.Emporix/Generated/` with the sync |
| A body the generated types cannot express | a hand-written type next to the facade; see «When the generated type will not do» |
| Whole new service | the above **plus** `EmporixClient.cs` (backing field + lazy property) and `ServiceCollectionExtensions.cs` (`TryAddSingleton`) |

Forgetting the DI registration compiles and passes every other test. It is the
third place, and it is the one that gets missed.

## The method

```csharp
/// <summary>Changes individual fields of an asset.</summary>
/// <param name="assetId">The asset id.</param>
/// <param name="operations">The JSON Patch operations to apply.</param>
/// <param name="auth">What to authorise with; a service token when omitted.</param>
/// <param name="cancellationToken">Cancels the call.</param>
public Task PatchAsync(
    string assetId,
    IEnumerable<MediaPatchOperation> operations,
    AuthContext auth = default,
    CancellationToken cancellationToken = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
    ArgumentNullException.ThrowIfNull(operations);

    List<MediaPatchOperation> body = [.. operations];
    ArgumentOutOfRangeException.ThrowIfZero(body.Count, nameof(operations));

    return _http.SendAsync(
        new EmporixRequest
        {
            Method = HttpMethod.Patch,
            Path = $"{BasePath}/{Uri.EscapeDataString(assetId)}",
            Auth = Defaults.Service(auth),
            Content = EmporixJsonContent.Create(
                body, MediaJsonContext.Default.ListMediaPatchOperation),
        },
        cancellationToken);
}
```

Load-bearing details:

- **`auth` and `cancellationToken` are the last two parameters**, always
  present, always overridable. `Defaults.Service(auth)` or
  `Defaults.Anonymous(auth)` picks the default when the caller passes nothing —
  choose from the operation's `security` scopes, which
  `scripts/annotate_operations.py` prints. A backend-only scope defaults to
  service; a storefront-reachable endpoint takes the caller's token.
- **`Path` is one interpolated string.** `SpecPathTests` scans the source for
  it; a path assembled by concatenating outside the string is invisible to the
  scanner, which means the call is checked by neither direction. That is how 212
  of 639 calls once went unchecked.
- **`BasePath` carries the tenant** — `private string BasePath => $"/media/{_tenant}/assets";`
- **`Uri.EscapeDataString` on every path segment** that comes from a caller.
- **Validate arguments before the request leaves.** `ThrowIfNullOrWhiteSpace` on
  ids, `ThrowIfZero` on a collection that would send an empty body. A call that
  cannot do anything should not reach the network.

## The idempotency gate

`Idempotent = true` lets the retry handler repeat the request after a timeout or
a `5xx`. GET, PUT and DELETE generally qualify. A POST or PATCH qualifies only
when repeating it is provably harmless.

The gate is **per request, not per status code**. A JSON Patch that appends
through `/refIds/-` adds an entry every time it runs, so it can never be
idempotent — even though the specific `502` it might retry after is documented
as leaving no trace. Reason about the worst operation the method can carry, not
the one in front of you.

Anything that moves money, places an order, consumes a number from a sequence or
runs someone else's code is out, regardless of how it reads.

## Serialization

**One `JsonSerializerContext` per service, without exception.** They live in
`JsonContexts.cs` with fully qualified `[JsonSerializable]` entries. Emporix
reuses type names across specifications — `Metadata`, `Vendor`, `Price` — and a
shared context collides on them, aborting the source generator with `SYSLIB1031`
and taking every other context with it. Grouping even three small services was
enough.

Every context sets `DefaultIgnoreCondition = WhenWritingNull`. That is what
makes a partial update send only what the caller set.

Two generator rules worth knowing before they bite:

- **`SYSLIB1220`** — a converter named in a `JsonConverterAttribute` must be
  public. One reached only through a context's `Converters` list may stay
  internal, and should.
- **An `object`-typed property cannot be written by a source-generated context**
  unless the runtime type inside it happens to be registered there. Probed: a
  `RefId` went out fine, an array of two threw `NotSupportedException`, and so
  did a boxed `JsonElement`. Two calls that look alike, one of which works.

## When the generated type will not do

Reach for a hand-written type only for a body the generator cannot express, and
say why in the XML docs. Three live examples, each a different reason:

| Type | Why it is hand-written |
|---|---|
| `MediaPatchOperation` | the generated one declares `object? Value`, which no source-generated context can write; this one takes `JsonElement?`, which writes itself verbatim and needs nothing registered |
| `AiPatchOperation` | the specification leaves the operation object untitled, so the generator calls it `Anonymous` — no name to put in a signature |
| `AssetReferenceUpdate` | the update schema is a union of «blob» and «link» and neither half carries `refIds`, though the endpoint accepts them |

For the first two the better fix may be upstream of the type: a `SpecPatch` that
gives the schema a `title` turns `Anonymous` into a real name for everyone. Try
that first — `SpecPatches.cs` has 27 such repairs and a `Title` helper.

## Enums

Generated enums carry two repairs from `GeneratedCodeFixer`, both worth knowing
when you write a facade that takes one:

- **`NullOnUnknownEnumConverter<T>`** on nullable enum properties, so a value
  Emporix added but the vendored spec does not list reads as `null` instead of
  making the whole response unreadable. Non-nullable properties keep the strict
  converter — the spec marks those required, so an unrecognised value there is a
  broken contract. See ADR-0010.
- **`[JsonStringEnumMemberName]`** on every member whose declared value differs
  from its C# name, because `JsonStringEnumConverter` ignores `[EnumMember]` and
  would otherwise write `Add` for `add`, `_status` for `/status`.

A non-nullable generated enum defaults to its first member. `PatchOperationOp`
defaults to `Add`, so an operation built without setting `Op` silently means
«add» rather than failing. Say so in the XML docs of anything that takes one.

## XML docs carry what the signature cannot

The parameters are already named. The docs are for what breaks callers, and they
are where this repo keeps what a live call cost someone:

- a status that does **not** mean the request body was wrong;
- merge-versus-replace semantics — `PATCH` merges, `PUT` needs the whole
  document, `null` stores `null` rather than deleting;
- immutable fields the server validates against the stored value;
- optimistic locking (`metadata.version`, `409`);
- **a write the server accepts and discards.** These exist, they answer `204`,
  and nothing but a read-back reveals them.

Write `<remarks>` as prose explaining why, not what. Most comments in this
codebase record a defect a live call found; that is the bar.

## Tests

One file per service under `tests/Viu.Emporix.Tests/`, `StubHttpMessageHandler`
for HTTP. Assert what a wrapper can get wrong:

```csharp
[Fact]
public async Task Patching_an_asset_sends_the_operations_as_an_array()
{
    StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
    MediaService media = new(Http(handler), Options());

    await media.PatchAsync("a1", [ /* … */ ]);

    Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
    Assert.Equal("/media/acme/assets/a1", Uri(handler));
    Assert.StartsWith("[", handler.RequestBodies[0].TrimStart(), StringComparison.Ordinal);
}
```

The URL, the verb, the body, the query parameters **including their absence**,
and the idempotency flag:

```csharp
Assert.False(handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out _));
```

Use the handler's callback form when a step must answer differently per request
— `new StubHttpMessageHandler((request, _) => …)` with
`StubHttpMessageHandler.Json(status, body)`.

And keep the limit in view: these tests catch typos and regressions, not wrong
calls. Every one of them asserts what the author already believed.

## Public API surface

`Microsoft.CodeAnalysis.PublicApiAnalyzers` tracks every public symbol.

```bash
./scripts/update-public-api.sh     # RS0016 after adding API means run this
```

- `RS0026` means two overloads with optional parameters — rename one rather than
  suppress it.
- Generated types are excluded from the baseline, so a sync alone leaves it
  empty.
- If you change a signature you added **on this same branch**, reset
  `PublicAPI.Unshipped.txt` to the branch point before re-running the script.
  Otherwise it records a `*REMOVED*` line for a symbol that never shipped, and
  the next release announces a removal that never happened.
