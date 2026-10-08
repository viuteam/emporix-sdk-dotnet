# Search Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Vendor Emporix's new search-service specification and wrap its thirteen operations as `client.Search`, with the generator repaired where this specification exposed it.

**Architecture:** A one-line catalog entry brings the specification; two `SpecPatch` entries narrow `queries` to one `QueryNode` and drop the `boost` default; a fix keeps `RetypeProperties` inside its class. `SearchService` holds the tenant-wide lists and jobs, `ForType(type)` returns `SearchTypeOperations` for search, saved searches and indexes of one custom type. `SearchJsonContext` registers the runtime types a filter's `object? Value` may hold.

**Tech Stack:** .NET 10, C# 14, System.Text.Json source generation, NSwag through `tools/Viu.Emporix.SpecSync`, xUnit, Microsoft.CodeAnalysis.PublicApiAnalyzers.

**Spec:** `docs/superpowers/specs/2026-10-08-search-service-design.md`

## Global Constraints

- Do not invent endpoints, fields or scopes; verify against `specs/search-service.yml` once Task 2 vendored it. The Node SDK has no search service, so the specification and the live tenant are the only sources.
- Never edit `src/Viu.Emporix/Generated/` by hand.
- `TreatWarningsAsErrors`, `AnalysisLevel` `latest-recommended`, `GenerateDocumentationFile`, `IsAotCompatible`: a warning fails the build, so does a `cref` to a member that does not exist yet, and nothing may use reflection.
- One context per service: every search type goes into `SearchJsonContext`, fully qualified.
- A request path is one interpolated string assigned as `Path = …`; a nested group is constructed as `new SearchTypeOperations(_http, $"…")`. `SpecPathTests` resolves only these forms.
- Auth defaults to a service token through `Defaults.Service(auth)`; `auth` and `cancellationToken` are the last two parameters.
- The search `POST` sets `Idempotent = true`; nothing else sets it.
- New public symbols need `./scripts/update-public-api.sh`; RS0016 is a build error. If you change a signature added on this branch, reset `src/Viu.Emporix/PublicAPI.Unshipped.txt` to the branch point before re-running it.
- English in code, comments, docs and commits. No nested parentheses in a commit body. Every commit ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Never print a token, a secret or tenant data; never `cat ~/.emporix-smoke.env`. Source it.
- Branch `feat/search-service`, which holds the design and this plan. Never push to `chore/spec-sync`, never merge, never touch the release pull request.
- On this machine a shell variable set earlier in the same command line can expand empty: use literal paths. The AOT publish needs `export LIBRARY_PATH=/opt/homebrew/lib`.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `tools/Viu.Emporix.SpecSync/GeneratedCodeFixer.cs` | modify | `RetypeProperties` bounded to its class; union pattern accepts a generic type |
| `tools/Viu.Emporix.SpecSync/SpecCatalog.cs` | modify | the `search-service` entry |
| `tools/Viu.Emporix.SpecSync/SpecPatches.cs` | modify | two `search-service` patches |
| `specs/search-service.yml`, `specs/sync-manifest.json`, `src/Viu.Emporix/Generated/SearchService.cs` | by tool | the vendored specification and its types |
| `src/Viu.Emporix/JsonContexts.cs` | modify | `SearchJsonContext` |
| `src/Viu.Emporix/SearchService.cs` | create | `SearchService` and `SearchTypeOperations` |
| `src/Viu.Emporix/EmporixClient.cs`, `src/Viu.Emporix/ServiceCollectionExtensions.cs` | modify | `client.Search` and its registration |
| `src/Viu.Emporix/PublicAPI.Unshipped.txt` | by script | the public surface |
| `tests/Viu.Emporix.Tests/SpecSyncTests.cs` | modify | the fixer's two tests |
| `tests/Viu.Emporix.Tests/SearchServiceTests.cs` | create | the facade's tests |
| `tests/Viu.Emporix.Tests/ServiceCollectionExtensionsTests.cs` | modify | the registration |
| `samples/Viu.Emporix.SmokeTest/Program.cs` | modify | the search pass |
| `README.md`, `CLAUDE.md` | modify | rows and counts |

---

### Task 1: Keep a union retype inside its class

**Files:**
- Modify: `tools/Viu.Emporix.SpecSync/GeneratedCodeFixer.cs` — `RetypeUnionProperties` and `RetypeProperties`
- Test: `tests/Viu.Emporix.Tests/SpecSyncTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GeneratedCodeFixer.RetypeUnionProperties`, unchanged in signature, that retypes only inside the named class and accepts a generic current type.

- [ ] **Step 1: Record today's generator output**

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- generate 2>&1 | grep -c "NOT FOUND"
git status --porcelain
```

Note the count. Expected: a clean tree.

- [ ] **Step 2: Write the failing tests**

In `tests/Viu.Emporix.Tests/SpecSyncTests.cs`, directly after the test `A_union_property_becomes_raw_json`, add:

```csharp
    [Fact]
    public void A_union_retype_stays_inside_its_class()
    {
        // The search service's queries: the first branch of the union is a
        // list, which the pattern did not match, so the search ran on into
        // the next class and retyped SavedQuery.Queries instead.
        const string source = """
                public partial class SearchRequest
                {
                    public System.Collections.Generic.ICollection<QueryNode>? Queries { get; set; }
                }

                public partial class SavedQuery
                {
                    public QueryNode? Queries { get; set; }
                }
            """;

        (string result, IReadOnlyList<string> retyped, IReadOnlyList<string> missed) =
            GeneratedCodeFixer.RetypeUnionProperties(source, ["SearchRequest.Queries"]);

        Assert.Single(retyped);
        Assert.Empty(missed);
        Assert.Contains("public System.Text.Json.JsonElement? Queries", result, StringComparison.Ordinal);
        Assert.Contains("public QueryNode? Queries", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_union_property_missing_from_its_class_is_reported_not_retyped_elsewhere()
    {
        const string source = """
                public partial class First
                {
                    public string? Other { get; set; }
                }

                public partial class Second
                {
                    public Thing? Value { get; set; }
                }
            """;

        (string result, IReadOnlyList<string> retyped, IReadOnlyList<string> missed) =
            GeneratedCodeFixer.RetypeUnionProperties(source, ["First.Value"]);

        Assert.Empty(retyped);
        Assert.Equal(["First.Value"], missed);
        Assert.Equal(source, result);
    }
```

- [ ] **Step 3: Run them to see them fail**

```bash
dotnet test --filter "FullyQualifiedName~SpecSyncTests.A_union" 2>&1 | grep -E "\[FAIL\]|Bestanden!|Fehler!|Passed!|Failed!"
```

Expected: both new tests fail — the first finds `SavedQuery` retyped, the second finds `Second.Value` retyped. `A_union_property_becomes_raw_json` passes.

- [ ] **Step 4: Bound the retype to its class**

In `tools/Viu.Emporix.SpecSync/GeneratedCodeFixer.cs`, in `RetypeUnionProperties`, replace

```csharp
        => RetypeProperties(source, unions, "System.Text.Json.JsonElement?", @"[\w\.]+\??");
```

with

```csharp
        // One generic argument list is allowed: a union whose first branch is a
        // list comes out as ICollection<T>, and the plain pattern missed it.
        => RetypeProperties(source, unions, "System.Text.Json.JsonElement?", @"[\w\.]+(?:<[\w\.,\s]+>)?\??");
```

In `RetypeProperties`, replace

```csharp
            Regex declaration = new(
                $@"(?<class>public partial class {Regex.Escape(className)}\b(?:[^{{]*)\{{)(?<body>.*?)(?<property>public\s+){currentType}(?<tail>\s+{Regex.Escape(propertyName)}\s*\{{)",
                RegexOptions.Singleline);
```

with

```csharp
            // The body may not cross into another class: a lazy match that could
            // retype a property of the same name further down — SavedQuery's,
            // for SearchRequest's — and report it as done.
            Regex declaration = new(
                $@"(?<class>public partial class {Regex.Escape(className)}\b(?:[^{{]*)\{{)(?<body>(?:(?!partial class ).)*?)(?<property>public\s+){currentType}(?<tail>\s+{Regex.Escape(propertyName)}\s*\{{)",
                RegexOptions.Singleline);
```

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet test --filter "FullyQualifiedName~SpecSyncTests" 2>&1 | grep -E "\[FAIL\]|Bestanden!|Fehler!|Passed!|Failed!"
```

Expected: every `SpecSyncTests` test passes.

- [ ] **Step 6: Prove that today's generated code does not move**

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- generate 2>&1 | grep -c "NOT FOUND"
git status --porcelain
```

Expected: the same count as in Step 1, and only the two edited files in `git status`. A changed file under `Generated/` means a retype on `main` moves: stop and ask.

- [ ] **Step 7: Commit**

```bash
git add tools/Viu.Emporix.SpecSync/GeneratedCodeFixer.cs tests/Viu.Emporix.Tests/SpecSyncTests.cs
git commit -F - <<'EOF'
fix: retype a union property only inside its own class

RetypeProperties anchored on the class name but matched the property with a
lazy body that could run past the class's end, and the union pattern did not
admit a generic type. A union whose first branch is a list therefore never
matched in its own class, and the search continued into the next class with a
property of the same name. The search service's SearchRequest.Queries retyped
SavedQuery.Queries this way and reported success. The body now stops at the
next class, the pattern takes one generic argument list, and a property its
class does not have is reported as not found. Nothing generated today changes.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Vendor the search-service specification

**Files:**
- Modify: `tools/Viu.Emporix.SpecSync/SpecCatalog.cs`, `tools/Viu.Emporix.SpecSync/SpecPatches.cs`
- Modify, by tool: `specs/sync-manifest.json`; create, by tool: `specs/search-service.yml`, `src/Viu.Emporix/Generated/SearchService.cs`

**Interfaces:**
- Consumes: Task 1's fixer.
- Produces, in `Viu.Emporix.SearchServiceModels`: `SearchRequest` (`string Index`, `QueryNode? Queries`, `FilterNode? Filters`), `SavedSearchCall` (`string Query`, `string SearchQueryId`), `SearchHit` (`string? Id`, `IDictionary<string,string>? Name`, `double? _score`, `AdditionalProperties`), `SavedQueryRequest` (`Id`, `Name`, `Description`, `string Index`, `QueryNode? Queries`, `FilterNode? Filters`, `QueryMetadataRequest? Metadata`), `QueryMetadataRequest` (`int Version`), `SavedQueryId` (`string Id`), `SavedQuery` (`Id`, `Name`, `Description`, `Type`, `IndexId`, `QueryNode? Queries`, `FilterNode? Filters`, `ResourceMetadata? Metadata`), `IndexRequest` (`Id`, `Name`, `Description`, `ICollection<IndexField> Fields`, `IndexMetadataRequest? Metadata`), `IndexField` (`string Path`, `bool? Text`, `IndexFieldAutocomplete? Autocomplete`, `bool? Exact`), `IndexMetadataRequest` (`int Version`), `SearchIndex` (`Id`, `Type`, `Name`, `Description`, `Fields`, `SearchIndexStatus? Status`, `ResourceMetadata? Metadata`), `JobId` (`string? Id`), `IndexJob` (`Id`, `IndexJobStatus? Status`, `IndexJobType? Type`, `IndexId`, `Message`, `Response`, `Metadata`), `ResourceMetadata` (`int? Version`, `DateTimeOffset? CreatedAt`, `ModifiedAt`), `QueryNode` (`And`, `Or`, `SearchType? Type`, `Query`, `Field`, `double? Boost`, `Fuzzy? Fuzzy`), `FilterNode` (`And`, `Or`, `Field`, `FilterOperator? Operator`, `object? Value`); enums `SearchType { TEXT, PHRASE, AUTOCOMPLETE, WILDCARD }`, `FilterOperator { EQ, NEQ, GT, GTE, LT, LTE, BETWEEN, IN, NOT_IN, EXISTS }`, `SearchIndexStatus { Building, Ready, Failed }`, `IndexJobStatus { In_progress, Success, Failure }`, `IndexJobType { Create_index, Update_index, Delete_index }`, `IndexFieldAutocomplete { PREFIX, NGRAM }`.

- [ ] **Step 1: Add the catalog entry**

In `tools/Viu.Emporix.SpecSync/SpecCatalog.cs`, directly above `        new("schema", $"{Base}/utilities/schema/api-reference/api.yml"),`, add:

```csharp
        new("search-service", $"{Base}/utilities/search-service/api-reference/api.yml"),
```

- [ ] **Step 2: Add the two patches**

In `tools/Viu.Emporix.SpecSync/SpecPatches.cs`, directly above `            ["label-service"] =`, add:

```csharp
            ["search-service"] =
            [
                new SpecPatch(
                    "upstream: queries is a oneOf of a list of query nodes and one node. NSwag "
                    + "keeps the list, and the union rule would turn it into raw JSON. One node "
                    + "covers both — the specification stores a list as one or group, and a saved "
                    + "search returns a single node — so queries is narrowed to it on both bodies.",
                    ReplaceAll(
                        "          oneOf:\n            - type: array\n              items:\n                $ref: '#/components/schemas/QueryNode'\n            - $ref: '#/components/schemas/QueryNode'",
                        "          allOf:\n            - $ref: '#/components/schemas/QueryNode'")),
                new SpecPatch(
                    "upstream: boost declares default 1, which the generator turns into an "
                    + "initializer, so every query node would send boost — an and or an or group "
                    + "too, which the specification forbids combining with a query. Without the "
                    + "default the property stays null, and the server's own default applies.",
                    ReplaceAll(
                        "          exclusiveMinimum: true\n          default: 1\n",
                        "          exclusiveMinimum: true\n")),
            ],
```

- [ ] **Step 3: Fetch, and compare with the analysed operations**

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- fetch 2>&1 | grep -E "search-service|stale|Changed"
git status --porcelain -- specs/
grep -cE "^    (get|post|put|delete):" specs/search-service.yml
grep -E "operationId:" specs/search-service.yml | sed 's/ *operationId: //'
```

Expected: both patches reported as `repaired search-service`, none stale; `Changed:` names `search-service` and nothing else; `13` operations with these ids: `POST-search-tenant-type`, `GET-search-tenant-queries`, `GET-search-tenant-type-queries`, `PUT-search-tenant-type-queries-id`, `GET-search-tenant-type-queries-id`, `DELETE-search-tenant-type-queries-id`, `GET-search-tenant-indexes`, `GET-search-tenant-type-indexes`, `PUT-search-tenant-type-indexes-id`, `GET-search-tenant-type-indexes-id`, `DELETE-search-tenant-type-indexes-id`, `GET-search-tenant-jobs`, `GET-search-tenant-jobs-id`. **If `Changed:` names another service, or the operations differ, stop and ask the user** — the service is preview and moved twice on 2026-10-08.

- [ ] **Step 4: Generate, and check the types the facade relies on**

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- generate 2>&1 | grep "search-service →"
git status --porcelain -- src/Viu.Emporix/Generated/
grep -nE "public (QueryNode\?|object\?|double\?) (Queries|Value|Boost)" src/Viu.Emporix/Generated/SearchService.cs
```

Expected: no `JsonElement (union)` and no `NOT FOUND` for `search-service`; only `Generated/SearchService.cs` is new; `QueryNode? Queries` three times, `object? Value` once, `double? Boost` without ` = 1D`.

- [ ] **Step 5: Build and measure**

```bash
dotnet build
dotnet test --no-build --filter "SpecPathTests"
```

Expected: the build succeeds; `Every_operation_a_specification_declares_has_a_facade` fails with the thirteen `/search/` operations, the other two pass. Task 5 turns it green.

- [ ] **Step 6: Commit**

```bash
git add tools/Viu.Emporix.SpecSync/SpecCatalog.cs tools/Viu.Emporix.SpecSync/SpecPatches.cs specs/search-service.yml specs/sync-manifest.json src/Viu.Emporix/Generated/SearchService.cs
git commit -F - <<'EOF'
feat: vendor the search service specification

Emporix introduced a search service on 2026-09-28: search over a tenant's
custom entities, saved searches and the indexes they run on. It was not in the
catalog, so the daily sync never fetched it and no test reported its thirteen
operations. Two patches keep its request types writable: queries is narrowed to
one query node, which covers the list form, and the boost default no longer
becomes an initializer that would send boost on every node.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: `SearchJsonContext`, and filter values that can be written

**Files:**
- Modify: `src/Viu.Emporix/JsonContexts.cs` — a new context after `internal sealed partial class IndexingJsonContext : JsonSerializerContext;`
- Create: `tests/Viu.Emporix.Tests/SearchServiceTests.cs`

**Interfaces:**
- Consumes: Task 2's types.
- Produces: `internal sealed partial class SearchJsonContext` with `Default.SearchRequest`, `SavedSearchCall`, `ListSearchHit`, `SavedQueryRequest`, `SavedQuery`, `ListSavedQuery`, `SavedQueryId`, `IndexRequest`, `SearchIndex`, `ListSearchIndex`, `JobId`, `IndexJob`, `ListIndexJob`, `FilterNode`, `QueryNode`; the test class `SearchServiceTests`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Viu.Emporix.Tests/SearchServiceTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Viu.Emporix.SearchServiceModels;

namespace Viu.Emporix.Tests;

public class SearchServiceTests
{
    // ---------- What a body can carry ----------

    [Fact]
    public void Filter_values_of_every_registered_type_are_written()
    {
        // FilterNode.Value is generated as object. A source-generated context
        // writes an object only as a runtime type it has registered.
        DateTimeOffset day = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        (object Value, string Json)[] cases =
        [
            ("ACTIVE", "\"ACTIVE\""),
            (180, "180"),
            (180L, "180"),
            (4.5, "4.5"),
            (4.5m, "4.5"),
            (true, "true"),
            (day, "\"2026-10-08T00:00:00+00:00\""),
            (new[] { "ACTIVE", "DRAFT" }, "[\"ACTIVE\",\"DRAFT\"]"),
            (new[] { 100, 200 }, "[100,200]"),
            (new[] { 100L, 200L }, "[100,200]"),
            (new[] { 1.5, 2.5 }, "[1.5,2.5]"),
            (new[] { 1.5m, 2.5m }, "[1.5,2.5]"),
            (new[] { day }, "[\"2026-10-08T00:00:00+00:00\"]"),
            (JsonDocument.Parse("""{"a":1}""").RootElement.Clone(), """{"a":1}"""),
        ];

        foreach ((object value, string json) in cases)
        {
            string written = JsonSerializer.Serialize(
                new FilterNode { Field = "f", Operator = FilterOperator.EQ, Value = value },
                SearchJsonContext.Default.FilterNode);

            Assert.Contains($"\"value\":{json}", written, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_filter_value_of_another_type_throws_before_anything_is_sent()
    {
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(
            new FilterNode { Field = "f", Operator = FilterOperator.EQ, Value = Guid.Empty },
            SearchJsonContext.Default.FilterNode));
    }

    [Fact]
    public void A_query_group_sends_no_boost_unless_one_is_set()
    {
        // The specification forbids combining a group with a query's fields,
        // and a generated default of 1 would have put boost on every node.
        string written = JsonSerializer.Serialize(
            new QueryNode
            {
                Or =
                [
                    new QueryNode { Type = SearchType.TEXT, Field = "name.en", Query = "red" },
                    new QueryNode { Type = SearchType.TEXT, Field = "name.en", Query = "blue", Boost = 2 },
                ],
            },
            SearchJsonContext.Default.QueryNode);

        Assert.Equal(1, written.Split("boost").Length - 1);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
dotnet build tests/Viu.Emporix.Tests 2>&1 | grep -oE "error CS[0-9]+: [^[]+" | sort -u | head -3
```

Expected: `CS0103`, the name `SearchJsonContext` does not exist.

- [ ] **Step 3: Add the context**

In `src/Viu.Emporix/JsonContexts.cs`, directly after `internal sealed partial class IndexingJsonContext : JsonSerializerContext;`, add:

```csharp

/// <summary>Serialization for the search service. See <see cref="ProductJsonContext"/>.</summary>
/// <remarks>
/// A filter's value is generated as <c>object</c>, and a source-generated context
/// writes an object only as a runtime type it has registered — hence the
/// primitives and arrays at the end. Any other type throws before a request
/// leaves.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.SearchServiceModels.SearchRequest))]
[JsonSerializable(typeof(Viu.Emporix.SearchServiceModels.SavedSearchCall))]
[JsonSerializable(typeof(List<Viu.Emporix.SearchServiceModels.SearchHit>))]
[JsonSerializable(typeof(Viu.Emporix.SearchServiceModels.SavedQueryRequest))]
[JsonSerializable(typeof(Viu.Emporix.SearchServiceModels.SavedQuery))]
[JsonSerializable(typeof(List<Viu.Emporix.SearchServiceModels.SavedQuery>))]
[JsonSerializable(typeof(Viu.Emporix.SearchServiceModels.SavedQueryId))]
[JsonSerializable(typeof(Viu.Emporix.SearchServiceModels.IndexRequest))]
[JsonSerializable(typeof(Viu.Emporix.SearchServiceModels.SearchIndex))]
[JsonSerializable(typeof(List<Viu.Emporix.SearchServiceModels.SearchIndex>))]
[JsonSerializable(typeof(Viu.Emporix.SearchServiceModels.JobId))]
[JsonSerializable(typeof(Viu.Emporix.SearchServiceModels.IndexJob))]
[JsonSerializable(typeof(List<Viu.Emporix.SearchServiceModels.IndexJob>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(long[]))]
[JsonSerializable(typeof(double[]))]
[JsonSerializable(typeof(decimal[]))]
[JsonSerializable(typeof(DateTimeOffset[]))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class SearchJsonContext : JsonSerializerContext;
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet build 2>&1 | tail -2
dotnet test --no-build --filter "FullyQualifiedName~SearchServiceTests" 2>&1 | grep -E "\[FAIL\]|Bestanden!|Fehler!|Passed!|Failed!"
```

Expected: the build succeeds with no new public symbol; the three tests pass. If the `Guid` test reports another exception type, it is the type the serializer throws for an unregistered runtime type — use it in the assertion and in the remarks of Task 5's `SearchAsync`.

- [ ] **Step 5: Commit**

```bash
git add src/Viu.Emporix/JsonContexts.cs tests/Viu.Emporix.Tests/SearchServiceTests.cs
git commit -F - <<'EOF'
feat: serialise the search service, filter values included

A filter's value is generated as an object, which a source-generated context
can write only as a runtime type it has registered. SearchJsonContext
registers the strings, numbers, booleans and timestamps a comparison takes, the
arrays a range or a list takes, and JsonElement for anything else. Any other
type throws before a request leaves, and a query group no longer carries a
boost nobody set.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: `client.Search` — the lists that span every type, and jobs

**Files:**
- Create: `src/Viu.Emporix/SearchService.cs`
- Modify: `src/Viu.Emporix/EmporixClient.cs` (field after `private IndexingService? _indexing;`, property after the `Indexing` property), `src/Viu.Emporix/ServiceCollectionExtensions.cs` (after the `IndexingService` registration)
- Modify, by script: `src/Viu.Emporix/PublicAPI.Unshipped.txt`
- Test: `tests/Viu.Emporix.Tests/SearchServiceTests.cs`, `tests/Viu.Emporix.Tests/ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: Task 3's `SearchJsonContext`.
- Produces: `public sealed class SearchService` with `ListQueriesAsync(string? query = null, int pageNumber = 1, int pageSize = 60, AuthContext auth = default, CancellationToken cancellationToken = default) → Task<PaginatedItems<SavedQuery>>`, `ListIndexesAsync(…) → Task<PaginatedItems<SearchIndex>>`, `ListJobsAsync(…) → Task<PaginatedItems<IndexJob>>`, `GetJobAsync(string jobId, AuthContext auth = default, CancellationToken cancellationToken = default) → Task<IndexJob?>`, and `internal static List<KeyValuePair<string, string?>> Paging(string? query, int pageNumber, int pageSize)`; `EmporixClient.Search`; the test helpers `Create(StubHttpMessageHandler)` and `Uri(StubHttpMessageHandler, int)`.

- [ ] **Step 1: Write the failing tests**

In `tests/Viu.Emporix.Tests/SearchServiceTests.cs`, add at the top of the class:

```csharp
    private static SearchService Create(StubHttpMessageHandler handler)
    {
        IOptions<EmporixOptions> options = Options.Create(new EmporixOptions { Tenant = "acme" });

        return new SearchService(new EmporixHttpClient(new HttpClient(handler), options), options);
    }

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;
```

and at its end:

```csharp
    // ---------- Across every type ----------

    [Fact]
    public async Task The_lists_across_types_read_pages_of_their_type()
    {
        StubHttpMessageHandler queries = new(
            HttpStatusCode.OK, """[{"id":"red-cars","type":"vehicle","indexId":"vehicles"}]""");
        PaginatedItems<SavedQuery> saved = await Create(queries).ListQueriesAsync(query: "type:vehicle");

        Assert.Equal("/search/acme/search/queries?pageNumber=1&pageSize=60&q=type%3Avehicle", Uri(queries));
        Assert.Equal("vehicles", Assert.Single(saved.Items).IndexId);

        StubHttpMessageHandler indexes = new(HttpStatusCode.OK, """[{"id":"vehicles","status":"ready"}]""");
        PaginatedItems<SearchIndex> found = await Create(indexes).ListIndexesAsync();

        Assert.Equal("/search/acme/search/indexes?pageNumber=1&pageSize=60", Uri(indexes));
        Assert.Equal(SearchIndexStatus.Ready, Assert.Single(found.Items).Status);

        StubHttpMessageHandler jobs = new(
            HttpStatusCode.OK, """[{"id":"j1","status":"in_progress","type":"create_index"}]""");
        PaginatedItems<IndexJob> running = await Create(jobs).ListJobsAsync(pageNumber: 2, pageSize: 10);

        Assert.Equal("/search/acme/jobs?pageNumber=2&pageSize=10", Uri(jobs));
        Assert.Equal(IndexJobStatus.In_progress, Assert.Single(running.Items).Status);
    }

    [Fact]
    public async Task A_job_is_read_by_its_escaped_id()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK, """{"id":"j 1","status":"failure","type":"delete_index","response":"gone"}""");

        IndexJob? job = await Create(handler).GetJobAsync("j 1");

        Assert.Equal("/search/acme/jobs/j%201", Uri(handler));
        Assert.Equal(IndexJobStatus.Failure, job?.Status);
        Assert.Equal(IndexJobType.Delete_index, job?.Type);
    }

    [Fact]
    public async Task Paging_and_ids_are_checked_before_any_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        SearchService search = Create(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await search.ListJobsAsync(pageNumber: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await search.ListIndexesAsync(pageSize: 0));
        await Assert.ThrowsAsync<ArgumentException>(async () => await search.GetJobAsync(" "));

        Assert.Equal(0, handler.CallCount);
    }
```

In `tests/Viu.Emporix.Tests/ServiceCollectionExtensionsTests.cs`, below `        Assert.NotNull(provider.GetRequiredService<IndexingService>());` add `        Assert.NotNull(provider.GetRequiredService<SearchService>());`, and below `        Assert.NotNull(client.Indexing);` add `        Assert.NotNull(client.Search);`.

- [ ] **Step 2: Run them to see them fail**

```bash
dotnet build tests/Viu.Emporix.Tests 2>&1 | grep -oE "error CS[0-9]+: [^[]+" | sort -u | head -3
```

Expected: `CS0246`, the type `SearchService` could not be found.

- [ ] **Step 3: Write `SearchService`**

Create `src/Viu.Emporix/SearchService.cs`:

```csharp
using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.SearchServiceModels;

namespace Viu.Emporix;

/// <summary>
/// Search over a tenant's custom entities: the search itself, saved searches,
/// and the indexes it runs on.
/// </summary>
/// <remarks>
/// <para>
/// A preview service at Emporix, introduced on 2026-09-28 and still changing
/// from week to week. Everything that belongs to one custom schema type is
/// reached through <c>ForType</c>; the lists here span every type.
/// </para>
/// <para>
/// An index builds asynchronously. Creating, changing or deleting one answers
/// with a job id, which <see cref="GetJobAsync"/> reads; wrap it in
/// <see cref="EmporixPolling.WaitForAsync"/> to wait for the job to finish.
/// </para>
/// </remarks>
public sealed class SearchService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal SearchService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/search/{_tenant}";

    /// <summary>Lists the saved searches of every type.</summary>
    /// <param name="query">An Emporix <c>q</c> filter, or nothing.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<SavedQuery>> ListQueriesAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/search/queries",
                Auth = Defaults.Service(auth),
                Query = Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListSavedQuery,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Lists the search indexes of every type.</summary>
    /// <param name="query">An Emporix <c>q</c> filter, or nothing.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<SearchIndex>> ListIndexesAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/search/indexes",
                Auth = Defaults.Service(auth),
                Query = Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListSearchIndex,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Lists the jobs that build and delete indexes.</summary>
    /// <param name="query">An Emporix <c>q</c> filter, or nothing.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Jobs cannot be deleted; the list keeps every one.</remarks>
    public async Task<PaginatedItems<IndexJob>> ListJobsAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/jobs",
                Auth = Defaults.Service(auth),
                Query = Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListIndexJob,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads one index job.</summary>
    /// <param name="jobId">The job id an index write answered with.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IndexJob?> GetJobAsync(
        string jobId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/jobs/{Uri.EscapeDataString(jobId)}",
                Auth = Defaults.Service(auth),
            },
            SearchJsonContext.Default.IndexJob,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The page parameters and an optional <c>q</c> filter every list here takes.</summary>
    internal static List<KeyValuePair<string, string?>> Paging(string? query, int pageNumber, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
        ];

        if (query is { Length: > 0 })
        {
            parameters.Add(new KeyValuePair<string, string?>("q", query));
        }

        return parameters;
    }
}
```

- [ ] **Step 4: Wire it into the client and the container**

In `src/Viu.Emporix/EmporixClient.cs`, below `    private IndexingService? _indexing;` add:

```csharp
    private SearchService? _search;
```

and directly after the closing brace of the `Indexing` property add:

```csharp

    /// <summary>Search over custom entities, saved searches and the indexes they run on.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public SearchService Search
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _search ??= new SearchService(_http, _options);
        }
    }
```

In `src/Viu.Emporix/ServiceCollectionExtensions.cs`, directly after the `IndexingService` registration and its blank line, add:

```csharp
        services.TryAddSingleton(static provider => new SearchService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

```

- [ ] **Step 5: Record the public surface and build**

```bash
dotnet build 2>&1 | grep -oE "(error|warning) [A-Z]+[0-9]+: [^[]+" | grep -v "RS0016" | sort -u | head
./scripts/update-public-api.sh
dotnet build 2>&1 | tail -2
```

Expected: nothing from the first command; the script records `SearchService`, its four methods and `EmporixClient.Search.get`; the build succeeds.

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test --no-build --filter "FullyQualifiedName~SearchServiceTests|FullyQualifiedName~ServiceCollectionExtensionsTests|FullyQualifiedName~SpecResponseTests" 2>&1 | grep -E "\[FAIL\]|Bestanden!|Fehler!|Passed!|Failed!"
```

Expected: all pass; `SpecResponseTests` accepts the four new reads.

- [ ] **Step 7: Commit**

```bash
git add src/Viu.Emporix/SearchService.cs src/Viu.Emporix/EmporixClient.cs src/Viu.Emporix/ServiceCollectionExtensions.cs src/Viu.Emporix/PublicAPI.Unshipped.txt tests/Viu.Emporix.Tests/SearchServiceTests.cs tests/Viu.Emporix.Tests/ServiceCollectionExtensionsTests.cs
git commit -F - <<'EOF'
feat: list saved searches, indexes and index jobs across types

client.Search is the search service's facade. Its lists span every custom
type and page like every other list in the SDK, and a job reads by its id,
which is what an index write answers with while the index builds.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: `ForType` — search, saved searches and indexes of one type

**Files:**
- Modify: `src/Viu.Emporix/SearchService.cs`
- Modify, by script: `src/Viu.Emporix/PublicAPI.Unshipped.txt`
- Test: `tests/Viu.Emporix.Tests/SearchServiceTests.cs`

**Interfaces:**
- Consumes: Task 4's `SearchService`, `SearchService.Paging`.
- Produces: `SearchService.ForType(string type) → SearchTypeOperations`; `public sealed class SearchTypeOperations` with `SearchAsync(SearchRequest request, int pageNumber = 1, int pageSize = 60, string? sort = null, AuthContext auth = default, CancellationToken cancellationToken = default) → Task<PaginatedItems<SearchHit>>`, `RunSavedSearchAsync(string savedSearchId, string text, int pageNumber = 1, int pageSize = 60, AuthContext auth = default, CancellationToken cancellationToken = default) → Task<PaginatedItems<SearchHit>>`, `ListQueriesAsync(string? query = null, int pageNumber = 1, int pageSize = 60, …) → Task<PaginatedItems<SavedQuery>>`, `GetQueryAsync(string savedSearchId, …) → Task<SavedQuery?>`, `UpsertQueryAsync(string savedSearchId, SavedQueryRequest savedSearch, …) → Task<SavedQueryId?>`, `DeleteQueryAsync(string savedSearchId, …) → Task`, `ListIndexesAsync(string? query = null, int pageNumber = 1, int pageSize = 60, …) → Task<PaginatedItems<SearchIndex>>`, `GetIndexAsync(string indexId, …) → Task<SearchIndex?>`, `UpsertIndexAsync(string indexId, IndexRequest index, …) → Task<JobId?>`, `DeleteIndexAsync(string indexId, …) → Task<JobId?>`, each ending in `AuthContext auth = default, CancellationToken cancellationToken = default`.

- [ ] **Step 1: Write the failing tests**

Append to `SearchServiceTests`:

```csharp
    // ---------- One type ----------

    /// <summary>The answer the specification shows for a search.</summary>
    private const string SearchHits = """
        [{"id":"64f1c2a8e4b0a1d2c3e4f5a6","name":{"en":"Diesel engine"},"status":"ACTIVE","horsepower":180,"_score":4.2},
         {"id":"64f1c2a8e4b0a1d2c3e4f5b7","name":{"en":"Diesel generator"},"status":"ACTIVE","horsepower":220,"_score":3.1}]
        """;

    [Fact]
    public async Task A_search_posts_its_criteria_and_reads_the_hits()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, SearchHits);

        PaginatedItems<SearchHit> hits = await Create(handler).ForType("vehicle").SearchAsync(
            new SearchRequest
            {
                Index = "vehicles",
                Queries = new QueryNode { Type = SearchType.TEXT, Field = "name.en", Query = "diesel" },
            },
            sort: "name:ASC");

        Assert.Equal(HttpMethod.Post, handler.RequestMethods[0]);
        Assert.Equal("/search/acme/search/vehicle?pageNumber=1&pageSize=60&sort=name%3AASC", Uri(handler));
        Assert.True(handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out bool idempotent));
        Assert.True(idempotent);

        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("vehicles", body.RootElement.GetProperty("index").GetString());
        Assert.Equal("TEXT", body.RootElement.GetProperty("queries").GetProperty("type").GetString());

        Assert.Equal(2, hits.Items.Count);
        Assert.Equal(4.2, hits.Items[0]._score);
        Assert.Equal("Diesel engine", hits.Items[0].Name?["en"]);

        // Stored fields arrive beside the declared ones.
        Assert.Equal(180, ((JsonElement)hits.Items[0].AdditionalProperties["horsepower"]).GetInt32());
    }

    [Fact]
    public async Task A_saved_search_runs_with_its_text()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        SearchTypeOperations vehicles = Create(handler).ForType("vehicle");

        await vehicles.RunSavedSearchAsync("red-cars", "red");

        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("red-cars", body.RootElement.GetProperty("searchQueryId").GetString());
        Assert.Equal("red", body.RootElement.GetProperty("query").GetString());
        Assert.Equal("/search/acme/search/vehicle?pageNumber=1&pageSize=60", Uri(handler));

        await Assert.ThrowsAsync<ArgumentException>(async () => await vehicles.RunSavedSearchAsync("red-cars", " "));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Saved_searches_are_listed_read_upserted_and_deleted()
    {
        StubHttpMessageHandler handler = new((_, call) => call switch
        {
            1 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """[{"id":"red-cars"}]"""),
            2 => StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                """{"id":"red-cars","indexId":"vehicles","queries":{"type":"TEXT","field":"name.en","query":"red"}}"""),
            3 => StubHttpMessageHandler.Json(HttpStatusCode.Created, """{"id":"red-cars"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
        });
        SearchTypeOperations vehicles = Create(handler).ForType("vehicle");
        QueryNode red = new() { Type = SearchType.TEXT, Field = "name.en", Query = "red" };

        PaginatedItems<SavedQuery> listed = await vehicles.ListQueriesAsync();
        SavedQuery? read = await vehicles.GetQueryAsync("red-cars");
        SavedQueryId? created = await vehicles.UpsertQueryAsync(
            "red-cars", new SavedQueryRequest { Index = "vehicles", Queries = red });
        SavedQueryId? replaced = await vehicles.UpsertQueryAsync(
            "red-cars",
            new SavedQueryRequest
            {
                Index = "vehicles",
                Queries = red,
                Metadata = new QueryMetadataRequest { Version = 1 },
            });
        await vehicles.DeleteQueryAsync("red-cars");

        Assert.Equal(
            [HttpMethod.Get, HttpMethod.Get, HttpMethod.Put, HttpMethod.Put, HttpMethod.Delete],
            handler.RequestMethods);
        Assert.Equal("/search/acme/search/vehicle/queries?pageNumber=1&pageSize=60", Uri(handler, 0));
        Assert.Equal("/search/acme/search/vehicle/queries/red-cars", Uri(handler, 1));
        Assert.Equal("/search/acme/search/vehicle/queries/red-cars", Uri(handler, 4));
        Assert.Single(listed.Items);
        Assert.Equal(SearchType.TEXT, read?.Queries?.Type);
        Assert.Equal("red-cars", created?.Id);
        Assert.Null(replaced);
        Assert.Contains("\"version\":1", handler.RequestBodies[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Indexes_are_listed_read_upserted_and_deleted()
    {
        StubHttpMessageHandler handler = new((_, call) => call switch
        {
            1 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """[{"id":"vehicles","status":"building"}]"""),
            2 => StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                """{"id":"vehicles","type":"vehicle","fields":[{"path":"name.en","text":true}],"status":"ready","metadata":{"version":2}}"""),
            3 => StubHttpMessageHandler.Json(HttpStatusCode.Accepted, """{"id":"job-1"}"""),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.Accepted, """{"id":"job-2"}"""),
        });
        SearchTypeOperations vehicles = Create(handler).ForType("vehicle");

        PaginatedItems<SearchIndex> listed = await vehicles.ListIndexesAsync(query: "status:ready");
        SearchIndex? read = await vehicles.GetIndexAsync("vehicles");
        JobId? building = await vehicles.UpsertIndexAsync(
            "vehicles",
            new IndexRequest { Fields = [new IndexField { Path = "name.en", Text = true }] });
        JobId? deleting = await vehicles.DeleteIndexAsync("vehicles");

        Assert.Equal([HttpMethod.Get, HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete], handler.RequestMethods);
        Assert.Equal("/search/acme/search/vehicle/indexes?pageNumber=1&pageSize=60&q=status%3Aready", Uri(handler, 0));
        Assert.Equal("/search/acme/search/vehicle/indexes/vehicles", Uri(handler, 2));
        Assert.Equal(SearchIndexStatus.Building, Assert.Single(listed.Items).Status);
        Assert.Equal(2, read?.Metadata?.Version);
        Assert.Equal("job-1", building?.Id);
        Assert.Equal("job-2", deleting?.Id);

        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[2]);
        JsonElement field = body.RootElement.GetProperty("fields")[0];
        Assert.Equal("name.en", field.GetProperty("path").GetString());
        Assert.False(field.TryGetProperty("exact", out _));
    }

    [Fact]
    public async Task A_type_is_escaped_into_the_path_and_checked()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        SearchService search = Create(handler);

        await search.ForType("my type").ListIndexesAsync();

        Assert.Equal("/search/acme/search/my%20type/indexes?pageNumber=1&pageSize=60", Uri(handler));
        Assert.Throws<ArgumentException>(() => search.ForType(" "));
    }

    [Fact]
    public async Task Type_operations_check_their_arguments_before_any_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        SearchTypeOperations vehicles = Create(handler).ForType("vehicle");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await vehicles.SearchAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await vehicles.UpsertQueryAsync("red-cars", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await vehicles.UpsertIndexAsync("vehicles", null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await vehicles.GetQueryAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(async () => await vehicles.DeleteIndexAsync(" "));

        Assert.Equal(0, handler.CallCount);
    }
```

- [ ] **Step 2: Run them to see them fail**

```bash
dotnet build tests/Viu.Emporix.Tests 2>&1 | grep -oE "error CS[0-9]+: [^[]+" | sort -u | head -3
```

Expected: `CS1061`, `SearchService` has no `ForType`.

- [ ] **Step 3: Add `ForType` and `SearchTypeOperations`**

In `src/Viu.Emporix/SearchService.cs`, directly above `    /// <summary>Lists the saved searches of every type.</summary>`, add:

```csharp
    /// <summary>Search, saved searches and indexes of one custom schema type.</summary>
    /// <param name="type">The custom schema type, such as <c>PET</c>.</param>
    public SearchTypeOperations ForType(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return new SearchTypeOperations(_http, $"/search/{_tenant}/search/{Uri.EscapeDataString(type)}");
    }

```

Then append to the end of the file:

```csharp

/// <summary>Search, saved searches and indexes of one custom schema type.</summary>
/// <remarks>Reached through <see cref="SearchService.ForType"/>.</remarks>
public sealed class SearchTypeOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal SearchTypeOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Searches the documents of this type.</summary>
    /// <param name="request">The index, and the queries, the filters or both.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="sort">A sort expression such as <c>name:ASC</c>, or nothing for relevance.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// A filter's <c>Value</c> is generated as <c>object</c> and goes out only as a
    /// <see cref="string"/>, <see cref="int"/>, <see cref="long"/>, <see cref="double"/>,
    /// <see cref="decimal"/>, <see cref="bool"/>, <see cref="DateTimeOffset"/>, an array
    /// of any of these but <see cref="bool"/>, or a <see cref="System.Text.Json.JsonElement"/>.
    /// Any other type throws <see cref="NotSupportedException"/> before the request leaves.
    /// </para>
    /// <para>
    /// Field paths are the stored ones — <c>name.en</c>, not <c>name</c> — and a
    /// filtered field must be indexed with <c>Exact</c>. Sent as a <c>POST</c>
    /// because the criteria are a body, but declared repeatable: it only reads.
    /// </para>
    /// </remarks>
    public async Task<PaginatedItems<SearchHit>> SearchAsync(
        SearchRequest request,
        int pageNumber = 1,
        int pageSize = 60,
        string? sort = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<KeyValuePair<string, string?>> query = SearchService.Paging(null, pageNumber, pageSize);

        if (sort is { Length: > 0 })
        {
            query.Add(new KeyValuePair<string, string?>("sort", sort));
        }

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Query = query,
                Content = EmporixJsonContent.Create(request, SearchJsonContext.Default.SearchRequest),
                Idempotent = true,
            },
            SearchJsonContext.Default.ListSearchHit,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a saved search with a text of its own.</summary>
    /// <param name="savedSearchId">The saved search.</param>
    /// <param name="text">The text applied to every search term in it; not blank.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The saved search supplies the index, the queries and the filters.</remarks>
    public async Task<PaginatedItems<SearchHit>> RunSavedSearchAsync(
        string savedSearchId,
        string text,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedSearchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Query = SearchService.Paging(null, pageNumber, pageSize),
                Content = EmporixJsonContent.Create(
                    new SavedSearchCall { SearchQueryId = savedSearchId, Query = text },
                    SearchJsonContext.Default.SavedSearchCall),
                Idempotent = true,
            },
            SearchJsonContext.Default.ListSearchHit,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists this type's saved searches.</summary>
    /// <param name="query">An Emporix <c>q</c> filter, or nothing.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<SavedQuery>> ListQueriesAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/queries",
                Auth = Defaults.Service(auth),
                Query = SearchService.Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListSavedQuery,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads one saved search.</summary>
    /// <param name="savedSearchId">The saved search id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The request's <c>index</c> comes back as <c>IndexId</c>, and a list of queries as one <c>or</c> group.</remarks>
    public async Task<SavedQuery?> GetQueryAsync(
        string savedSearchId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedSearchId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/queries/{Uri.EscapeDataString(savedSearchId)}",
                Auth = Defaults.Service(auth),
            },
            SearchJsonContext.Default.SavedQuery,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates or replaces a saved search.</summary>
    /// <param name="savedSearchId">The id: 1 to 66 letters, digits, underscores or hyphens.</param>
    /// <param name="savedSearch">The definition; its index must exist.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The id when Emporix created the saved search; <see langword="null"/> when it
    /// replaced one, which it answers with <c>204</c>.
    /// </returns>
    /// <remarks>
    /// Replacing needs <c>Metadata.Version</c> set to the stored version: without one
    /// Emporix answers <c>400</c>, with a stale one <c>409</c>.
    /// </remarks>
    public async Task<SavedQueryId?> UpsertQueryAsync(
        string savedSearchId,
        SavedQueryRequest savedSearch,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedSearchId);
        ArgumentNullException.ThrowIfNull(savedSearch);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/queries/{Uri.EscapeDataString(savedSearchId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(savedSearch, SearchJsonContext.Default.SavedQueryRequest),
            },
            SearchJsonContext.Default.SavedQueryId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a saved search.</summary>
    /// <param name="savedSearchId">The saved search id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteQueryAsync(
        string savedSearchId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedSearchId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/queries/{Uri.EscapeDataString(savedSearchId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists this type's search indexes.</summary>
    /// <param name="query">An Emporix <c>q</c> filter, or nothing.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<SearchIndex>> ListIndexesAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/indexes",
                Auth = Defaults.Service(auth),
                Query = SearchService.Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListSearchIndex,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads one search index.</summary>
    /// <param name="indexId">The index id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SearchIndex?> GetIndexAsync(
        string indexId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/indexes/{Uri.EscapeDataString(indexId)}",
                Auth = Defaults.Service(auth),
            },
            SearchJsonContext.Default.SearchIndex,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates or changes a search index, which then builds asynchronously.</summary>
    /// <param name="indexId">The index id.</param>
    /// <param name="index">The definition: at least one field.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The job that builds the index — read it with <see cref="SearchService.GetJobAsync"/>
    /// — or <see langword="null"/> when Emporix answers <c>204</c> without one.
    /// </returns>
    /// <remarks>
    /// A string field needs <c>Text</c>, <c>Autocomplete</c> or <c>Exact</c>; number,
    /// boolean and date fields take only <c>Exact</c>. Changing an existing index
    /// needs <c>Metadata.Version</c>. A retry after a timeout may meet the version
    /// the first attempt already moved, and answer <c>409</c>.
    /// </remarks>
    public async Task<JobId?> UpsertIndexAsync(
        string indexId,
        IndexRequest index,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexId);
        ArgumentNullException.ThrowIfNull(index);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/indexes/{Uri.EscapeDataString(indexId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(index, SearchJsonContext.Default.IndexRequest),
            },
            SearchJsonContext.Default.JobId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a search index, asynchronously.</summary>
    /// <param name="indexId">The index id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The job that deletes the index — read it with <see cref="SearchService.GetJobAsync"/>.</returns>
    public async Task<JobId?> DeleteIndexAsync(
        string indexId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/indexes/{Uri.EscapeDataString(indexId)}",
                Auth = Defaults.Service(auth),
            },
            SearchJsonContext.Default.JobId,
            cancellationToken).ConfigureAwait(false);
    }
}
```

Also replace `<c>ForType</c>` in the remarks of `SearchService` with `<see cref="ForType"/>`, now that it exists.

- [ ] **Step 4: Record the public surface and build**

```bash
dotnet build 2>&1 | grep -oE "(error|warning) [A-Z]+[0-9]+: [^[]+" | grep -v "RS0016" | sort -u | head
./scripts/update-public-api.sh
dotnet build 2>&1 | tail -2
```

Expected: nothing from the first command; the script records `ForType`, `SearchTypeOperations` and its ten methods; the build succeeds.

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet test --no-build --filter "FullyQualifiedName~SearchServiceTests|FullyQualifiedName~SpecPathTests|FullyQualifiedName~SpecResponseTests" 2>&1 | grep -E "\[FAIL\]|Bestanden!|Fehler!|Passed!|Failed!"
```

Expected: all pass. `SpecPathTests` finds all thirteen operations wrapped; `SpecResponseTests` accepts every typed read — `SavedQueryId` and `JobId` match the `201` and `202` bodies, a `204` none.

- [ ] **Step 6: Commit**

```bash
git add src/Viu.Emporix/SearchService.cs src/Viu.Emporix/PublicAPI.Unshipped.txt tests/Viu.Emporix.Tests/SearchServiceTests.cs
git commit -F - <<'EOF'
feat: search a custom type, and manage its saved searches and indexes

client.Search.ForType reaches everything that belongs to one custom schema
type: the search itself, by criteria or by a saved search with a text of its
own, the saved searches, and the indexes the search runs on. The search is a
POST that only reads, so it may be retried. An index write answers with the
job that builds or deletes it, and an upsert of an existing saved search or
index needs its stored version.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Documentation and the full check

**Files:**
- Modify: `README.md` — line 112, the service table, the grouped-operations table
- Modify: `CLAUDE.md:8-10`

**Interfaces:**
- Consumes: everything above. Produces: no code.

- [ ] **Step 1: Update the counts**

- `README.md`, line 112: `Every one of the 43` → `Every one of the 44`.
- `CLAUDE.md`, lines 8 to 10: `43 vendored Emporix specifications` → `44 vendored Emporix specifications`, `46 properties on the client` → `47 properties on the client`, `over 677 public calls.` → `over 691 public calls.`

- [ ] **Step 2: Add the rows**

In `README.md`, directly below `| \`client.Indexing\` | which provider indexes the catalogue, and rebuilding it |`, add:

```markdown
| `client.Search` | search over custom entities, saved searches and their indexes (preview) |
```

and directly below `| \`client.Schemas.CustomEntities\` · \`client.Schemas.InstancesOf(type)\` | a tenant's own shapes, and their records |`, add:

```markdown
| `client.Search.ForType(type)` | one custom type's search, saved searches and indexes |
```

- [ ] **Step 3: Run the full check**

```bash
dotnet build 2>&1 | tail -3
dotnet test --no-build 2>&1 | grep -E "Bestanden!|Fehler!|Passed!|Failed!"
export LIBRARY_PATH=/opt/homebrew/lib
dotnet publish samples/Viu.Emporix.Sample --configuration Release 2>&1 | grep -E "IL[0-9]{4}|error|->"
```

Expected: no warnings; every test passes — note the count; the AOT publish succeeds without an `IL` warning.

- [ ] **Step 4: Commit**

```bash
git add README.md CLAUDE.md
git commit -F - <<'EOF'
docs: list the search service in the readme

The service table and the grouped operations name client.Search and its
ForType group, and the counts take the forty-fourth specification, the
forty-seventh client property and fourteen more public calls.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 5: Break each rule once on purpose**

For every row: make the change, run the command, see the named test fail, restore with `git checkout -- <files>`.

| # | Change | Command | Must fail |
|---|---|---|---|
| 1 | in `GeneratedCodeFixer.RetypeProperties`, put `(?<body>.*?)` back | `dotnet test --filter "FullyQualifiedName~SpecSyncTests"` | `A_union_retype_stays_inside_its_class`, `A_union_property_missing_from_its_class_is_reported_not_retyped_elsewhere` |
| 2 | in `SpecPatches.cs`, delete the `boost` patch, then `dotnet run --project tools/Viu.Emporix.SpecSync` | `dotnet test --filter "FullyQualifiedName~SearchServiceTests"` | `A_query_group_sends_no_boost_unless_one_is_set`; restore `tools/`, `specs/` and `src/Viu.Emporix/Generated/` |
| 3 | in `SearchJsonContext`, delete `[JsonSerializable(typeof(int[]))]` | `dotnet test --filter "FullyQualifiedName~SearchServiceTests"` | `Filter_values_of_every_registered_type_are_written` |
| 4 | in `SearchTypeOperations.SearchAsync`, delete `Idempotent = true,` | the same | `A_search_posts_its_criteria_and_reads_the_hits` |
| 5 | in `SearchService.Paging`, delete the `if` that adds `q` | the same | `The_lists_across_types_read_pages_of_their_type`, `Indexes_are_listed_read_upserted_and_deleted` |

Then `git status --porcelain` must print nothing, and the full suite passes again. Note the five results for the pull request.

---

### Task 7: A search pass in the smoke test

**Files:**
- Modify: `samples/Viu.Emporix.SmokeTest/Program.cs` — a new pass directly above the final `Console.WriteLine();` / `return runner.Report();`

**Interfaces:**
- Consumes: `client.Search`, `client.Schemas.InstancesOf`, `EmporixPolling.WaitForAsync`; in `Program.cs` the existing `runner`, `client` and `service`.
- Produces: smoke steps `build a throwaway search index`, `wait for the index job`, `search by text, by filter and by an or group`, `save a search, run it, read it back and delete it`, `delete the search index`.

- [ ] **Step 1: Add the pass**

In `samples/Viu.Emporix.SmokeTest/Program.cs`, directly above the last two lines

```csharp
Console.WriteLine();
return runner.Report();
```

insert:

```csharp
// Search, a preview service Emporix introduced on 2026-09-28. It indexes custom
// entities, so this pass builds a throwaway index over PET, searches it, and
// drops it again; the delete runs even when a step in between fails. The index
// jobs stay in the job list: the service has no delete for them. The PET
// values searched for are read from the tenant and never printed.
Console.WriteLine();
Console.WriteLine("Service token — search, the pass that builds an index and drops it");
Console.WriteLine();

const string SmokeIndex = "emporix-sdk-smoke";
SearchTypeOperations pets = client.Search.ForType("PET");
(string Id, string Name)? pet = null;

string? indexJob = await runner.RunAsync("build a throwaway search index", async () =>
{
    JsonElement instances = await client.Schemas.InstancesOf("PET").ListAsync(pageSize: 1, auth: service);
    JsonElement first = instances.ValueKind == JsonValueKind.Array && instances.GetArrayLength() > 0
        ? instances[0]
        : default;

    if (first.ValueKind != JsonValueKind.Object
        || !first.TryGetProperty("mixins", out JsonElement mixins)
        || !mixins.TryGetProperty("attributes", out JsonElement attributes)
        || !attributes.TryGetProperty("id", out JsonElement id)
        || !attributes.TryGetProperty("name", out JsonElement name)
        || id.ValueKind != JsonValueKind.String
        || name.ValueKind != JsonValueKind.String
        || id.GetString() is not { Length: > 0 } petId
        || name.GetString() is not { Length: > 0 } petName)
    {
        return Step.Empty("no PET instance carries mixins.attributes.id and .name to search for");
    }

    pet = (petId, petName);

    // A crashed earlier run may have left the index behind; replacing it needs
    // its stored version.
    Viu.Emporix.SearchServiceModels.SearchIndex? leftover = null;
    try
    {
        leftover = await pets.GetIndexAsync(SmokeIndex, service);
    }
    catch (EmporixNotFoundException)
    {
    }

    Viu.Emporix.SearchServiceModels.JobId? job = await pets.UpsertIndexAsync(
        SmokeIndex,
        new Viu.Emporix.SearchServiceModels.IndexRequest
        {
            Fields =
            [
                new Viu.Emporix.SearchServiceModels.IndexField { Path = "mixins.attributes.name", Text = true },
                new Viu.Emporix.SearchServiceModels.IndexField { Path = "mixins.attributes.id", Exact = true },
            ],
            Metadata = leftover?.Metadata?.Version is int version
                ? new Viu.Emporix.SearchServiceModels.IndexMetadataRequest { Version = version }
                : null,
        },
        service);

    return job?.Id is { Length: > 0 } jobId
        ? Step.Ok(leftover is null ? "accepted, building" : "a leftover replaced, building", jobId)
        : Step.Failed("no job id came back");
});

await runner.RunAsync("wait for the index job", async () =>
{
    if (indexJob is null)
    {
        return Step.Skipped("no index job");
    }

    Viu.Emporix.SearchServiceModels.IndexJob? done = await EmporixPolling.WaitForAsync(
        token => client.Search.GetJobAsync(indexJob, service, token),
        job => job?.Status is not Viu.Emporix.SearchServiceModels.IndexJobStatus.In_progress,
        new EmporixPollingOptions { Timeout = TimeSpan.FromMinutes(2) });

    return done?.Status == Viu.Emporix.SearchServiceModels.IndexJobStatus.Success
        ? Step.Ok("built")
        : Step.Failed($"the job ended with status {done?.Status}");
});

await runner.RunAsync("search by text, by filter and by an or group", async () =>
{
    if (indexJob is null || pet is not { } target)
    {
        return Step.Skipped("no index, or nothing to search for");
    }

    Viu.Emporix.SearchServiceModels.QueryNode byName = new()
    {
        Type = Viu.Emporix.SearchServiceModels.SearchType.TEXT,
        Field = "mixins.attributes.name",
        Query = target.Name,
    };

    PaginatedItems<Viu.Emporix.SearchServiceModels.SearchHit> byText = await pets.SearchAsync(
        new Viu.Emporix.SearchServiceModels.SearchRequest { Index = SmokeIndex, Queries = byName },
        auth: service);

    // The filter's value goes out through the generated object property.
    PaginatedItems<Viu.Emporix.SearchServiceModels.SearchHit> byFilter = await pets.SearchAsync(
        new Viu.Emporix.SearchServiceModels.SearchRequest
        {
            Index = SmokeIndex,
            Filters = new Viu.Emporix.SearchServiceModels.FilterNode
            {
                Field = "mixins.attributes.id",
                Operator = Viu.Emporix.SearchServiceModels.FilterOperator.EQ,
                Value = target.Id,
            },
        },
        auth: service);

    // A group node with nothing but its children: boost must not come along.
    PaginatedItems<Viu.Emporix.SearchServiceModels.SearchHit> byGroup = await pets.SearchAsync(
        new Viu.Emporix.SearchServiceModels.SearchRequest
        {
            Index = SmokeIndex,
            Queries = new Viu.Emporix.SearchServiceModels.QueryNode
            {
                Or =
                [
                    byName,
                    new Viu.Emporix.SearchServiceModels.QueryNode
                    {
                        Type = Viu.Emporix.SearchServiceModels.SearchType.WILDCARD,
                        Field = "mixins.attributes.name",
                        Query = "*",
                    },
                ],
            },
        },
        auth: service);

    string counts =
        $"{byText.Items.Count} by text, {byFilter.Items.Count} by filter, {byGroup.Items.Count} by an or group";

    return byText.Items.Count > 0 && byFilter.Items.Count > 0 && byGroup.Items.Count > 0
        ? Step.Ok(counts)
        : Step.Failed(counts);
});

await runner.RunAsync("save a search, run it, read it back and delete it", async () =>
{
    if (indexJob is null || pet is not { } target)
    {
        return Step.Skipped("no index, or nothing to search for");
    }

    const string SavedSearch = "emporix-sdk-smoke";

    Viu.Emporix.SearchServiceModels.SavedQuery? leftover = null;
    try
    {
        leftover = await pets.GetQueryAsync(SavedSearch, service);
    }
    catch (EmporixNotFoundException)
    {
    }

    Viu.Emporix.SearchServiceModels.SavedQueryId? created = await pets.UpsertQueryAsync(
        SavedSearch,
        new Viu.Emporix.SearchServiceModels.SavedQueryRequest
        {
            Index = SmokeIndex,
            Queries = new Viu.Emporix.SearchServiceModels.QueryNode
            {
                Type = Viu.Emporix.SearchServiceModels.SearchType.TEXT,
                Field = "mixins.attributes.name",
                Query = target.Name,
            },
            Metadata = leftover?.Metadata?.Version is int version
                ? new Viu.Emporix.SearchServiceModels.QueryMetadataRequest { Version = version }
                : null,
        },
        service);

    try
    {
        PaginatedItems<Viu.Emporix.SearchServiceModels.SearchHit> hits =
            await pets.RunSavedSearchAsync(SavedSearch, target.Name, auth: service);
        Viu.Emporix.SearchServiceModels.SavedQuery? read = await pets.GetQueryAsync(SavedSearch, service);

        return read?.IndexId == SmokeIndex && read.Queries?.Field == "mixins.attributes.name"
            ? Step.Ok($"{(created is null ? "replaced" : "created")}, {hits.Items.Count} hit(s), read back")
            : Step.Failed($"read back index {read?.IndexId ?? "nothing"}, field {read?.Queries?.Field ?? "nothing"}");
    }
    finally
    {
        await pets.DeleteQueryAsync(SavedSearch, service);
    }
});

await runner.RunAsync("delete the search index", async () =>
{
    try
    {
        await pets.GetIndexAsync(SmokeIndex, service);
    }
    catch (EmporixNotFoundException)
    {
        return Step.Skipped("no index to clean up");
    }

    Viu.Emporix.SearchServiceModels.JobId? deleting = await pets.DeleteIndexAsync(SmokeIndex, service);
    if (deleting?.Id is not { Length: > 0 } jobId)
    {
        return Step.Failed("no delete job came back");
    }

    Viu.Emporix.SearchServiceModels.IndexJob? done = await EmporixPolling.WaitForAsync(
        token => client.Search.GetJobAsync(jobId, service, token),
        job => job?.Status is not Viu.Emporix.SearchServiceModels.IndexJobStatus.In_progress,
        new EmporixPollingOptions { Timeout = TimeSpan.FromMinutes(2) });

    return done?.Status == Viu.Emporix.SearchServiceModels.IndexJobStatus.Success
        ? Step.Ok("deleted; its jobs stay in the job list")
        : Step.Failed($"the delete job ended with status {done?.Status}");
});

```

- [ ] **Step 2: Build the smoke test**

```bash
dotnet build samples/Viu.Emporix.SmokeTest --configuration Release 2>&1 | grep -oE "(error|warning) [A-Z]+[0-9]+: [^[]+" | sort -u | head
dotnet build samples/Viu.Emporix.SmokeTest --configuration Release 2>&1 | tail -2
```

Expected: no errors and no warnings. Never run after a failed build: `--no-build` would run the old binary.

- [ ] **Step 3: Run it against the tenant**

```bash
set -a; . ~/.emporix-smoke.env; set +a; dotnet run --project samples/Viu.Emporix.SmokeTest --configuration Release --no-build
```

Expected: the five new steps report OK, the rest the known baseline. Copy the five lines for the pull request.

If a step fails, that is the finding this pass exists for — a body the API refuses, an index that is built but finds nothing, a boost the server rejects. Do not adjust the step to pass. Read what came back, fix the SDK where it is wrong with a unit test that fails first, and keep the sequence of answers for the pull request. If the index stays behind, delete it before stopping: `client.Search.ForType("PET").DeleteIndexAsync("emporix-sdk-smoke")`.

- [ ] **Step 4: Commit**

```bash
git add samples/Viu.Emporix.SmokeTest/Program.cs
git commit -F - <<'EOF'
test: walk the search service in the smoke test

A pass over PET on the service token: build a throwaway index and wait for its
job, search it by text, by a filter whose value goes out through the generated
object property, and by an or group without a boost, save a search, run it,
read it back and delete it, then delete the index and wait for that job too.
The jobs stay in the job list; the service cannot delete them.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 8: Open the pull request

**Files:**
- Create, outside the repository: `pr-body.md` in the session's scratchpad directory

**Interfaces:**
- Consumes: the branch and the results noted in Tasks 6 and 7. Produces: the pull request URL.

- [ ] **Step 1: Make sure the branch is current and clean**

```bash
git fetch origin --prune
git log --oneline HEAD..origin/main
git status --porcelain
```

Expected: no output. If `main` moved, `git rebase origin/main` and repeat Task 6, Step 3.

- [ ] **Step 2: Check upstream once more**

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- fetch 2>&1 | tail -1
git checkout -- specs/sync-manifest.json
git status --porcelain
```

Expected: `No content changes.` and a clean tree. A `Changed:` line means upstream moved again — for `search-service` that is likely: stop and ask the user whether to take it into this pull request.

- [ ] **Step 3: Check the commit messages**

```bash
git log --format=%B origin/main..HEAD | grep -nE '\([^)]*\('
```

Expected: no output.

- [ ] **Step 4: Write the body**

Write to `pr-body.md` in the scratchpad, filling in the test count from Task 6, Step 3 and the five smoke lines from Task 7, Step 3:

````markdown
Emporix introduced a search service on 2026-09-28 (COP-6704): search over a tenant's custom entities, saved searches, and the indexes they run on, built asynchronously by index jobs. Every operation is marked preview, and the specification has changed nine times since — twice on 2026-10-08. It was not in the catalog, so the daily sync never fetched it and `SpecPathTests` had nothing to report.

Design: `docs/superpowers/specs/2026-10-08-search-service-design.md` · plan: `docs/superpowers/plans/2026-10-08-search-service.md`

## Specs that moved

| Spec | Change | New operations |
|---|---|---|
| `search-service` | new in the catalog | 13 |

## Measurement

```bash
dotnet test --filter "SpecPathTests"
```

After the vendoring commit, `Every_operation_a_specification_declares_has_a_facade` failed on the thirteen `/search/` operations. It passes now.

## Added

| Verb | Path | Scope | Facade |
|---|---|---|---|
| `POST` | `/search/{tenant}/search/{type}` | `search.search_read` | `ForType(type).SearchAsync`, `.RunSavedSearchAsync` |
| `GET` | `/search/{tenant}/search/queries` | `search.search_read` | `Search.ListQueriesAsync` |
| `GET` | `/search/{tenant}/search/{type}/queries` | `search.search_read` | `ForType(type).ListQueriesAsync` |
| `GET` · `PUT` · `DELETE` | `/search/{tenant}/search/{type}/queries/{id}` | read · manage · manage | `GetQueryAsync`, `UpsertQueryAsync`, `DeleteQueryAsync` |
| `GET` | `/search/{tenant}/search/indexes` | `search.search_read` | `Search.ListIndexesAsync` |
| `GET` | `/search/{tenant}/search/{type}/indexes` | `search.search_read` | `ForType(type).ListIndexesAsync` |
| `GET` · `PUT` · `DELETE` | `/search/{tenant}/search/{type}/indexes/{id}` | read · manage · manage | `GetIndexAsync`, `UpsertIndexAsync`, `DeleteIndexAsync` |
| `GET` | `/search/{tenant}/jobs`, `/search/{tenant}/jobs/{id}` | `search.search_read` | `Search.ListJobsAsync`, `Search.GetJobAsync` |

## What the generator needed

- **A real fixer defect.** `RetypeProperties` could run past its class: the union rule's entry `SearchRequest.Queries` — first branch a list, which the pattern did not match — retyped `SavedQuery.Queries` instead and reported success. It now stays inside its class and accepts a generic type; nothing generated on `main` changes.
- **`queries`** is narrowed by a patch to one `QueryNode`, which covers the list form, so the request types stay writable and typed.
- **`boost: default: 1`** became `Boost = 1D`, sending boost on every node, group nodes included, which the specification forbids. A patch drops the default.
- **`FilterNode.Value`** has no type and stays `object?`; `SearchJsonContext` registers strings, numbers, booleans, timestamps, their arrays and `JsonElement`. Any other type throws before a request leaves.

## Left out

- `sort` on lists, `fields`, `Accept-Language`, `Content-Language` and `X-Total-Count`.
- The Node SDK has no search service, so there was nothing to compare with.
- **Unverified against a live tenant:** autocomplete and fuzzy search, `BETWEEN`/`IN`/`EXISTS` filters, `EMPLOYEE` and every type but `PET`.

## Verification

- `dotnet build`: no warnings.
- `dotnet test`: COUNT passed.
- `dotnet publish samples/Viu.Emporix.Sample -c Release`: the AOT publish succeeded without trim or AOT warnings.
- Broken on purpose, each failing the test it should: the unbounded fixer body, the boost patch, a registered filter type, the search's idempotency flag, the `q` parameter.
- Smoke test against tenant `viu`, a pass that builds a throwaway index over `PET` and drops it again. The index jobs stay in the job list; the service cannot delete them.

| Step | Result |
|---|---|
| build a throwaway search index | LINE |
| wait for the index job | LINE |
| search by text, by filter and by an or group | LINE |
| save a search, run it, read it back and delete it | LINE |
| delete the search index | LINE |

🤖 Generated with [Claude Code](https://claude.com/claude-code)
````

Replace `COUNT` and each `LINE` before saving.

- [ ] **Step 5: Push and open the pull request**

```bash
git push -u origin feat/search-service
gh pr create --base main --head feat/search-service --title "feat: add the search service" --body-file <scratchpad>/pr-body.md
```

`<scratchpad>` is the session's scratchpad directory; use its literal path.

- [ ] **Step 6: Report and stop**

Report the URL in a `<pr-created>` tag on its own line, bind it with the app's PR tools, and never poll CI. Merging, publishing and the release pull request are the user's calls.
