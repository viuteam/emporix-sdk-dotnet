# Search Service — Design

- **Date:** 2026-10-08
- **Status:** Approved 2026-10-08 → [implementation plan](../plans/2026-10-08-search-service.md)
- **Affects:** `tools/Viu.Emporix.SpecSync` (catalog, two patches, one fixer
  repair), `Viu.Emporix` — a new `SearchService`, `EmporixClient`,
  `ServiceCollectionExtensions`, `JsonContexts.cs`; the smoke test
- **Related:** [ADR-0001](../../adr/0001-type-generation.md),
  [ADR-0004](../../adr/0004-aot-trimming.md)

## Upstream

`utilities/search-service/api-reference/api.yml` in `emporix/api-references`,
introduced 2026-09-28 (COP-6704) and changed nine times since; the last two
commits on 2026-10-08 added the two tenant-wide lists and relaxed the
saved-search validation. Every operation is marked **preview**. The Emporix
documentation connector had no changelog entry for it on 2026-10-08.

The service searches documents of one custom schema type, stores saved
searches and manages the indexes the search runs on, built asynchronously by
index jobs. Thirteen operations, security OAuth2 only: `search.search_read`
for reads and the search itself, `search.search_manage` for writes.

| Group | Operations |
|---|---|
| Search | `POST /search/{t}/search/{type}` — one body that is either a `SearchRequest` or a `SavedSearchCall` (`anyOf`) |
| Saved searches | `GET /search/{t}/search/queries`; `GET`, and per id `GET`, `PUT` (201 `SavedQueryId` or 204), `DELETE` under `/search/{t}/search/{type}/queries` |
| Indexes | `GET /search/{t}/search/indexes`; `GET`, and per id `GET`, `PUT` (202 `JobId` or 204), `DELETE` (202 `JobId`) under `/search/{t}/search/{type}/indexes` |
| Jobs | `GET /search/{t}/jobs`, `GET /search/{t}/jobs/{id}` |

Lists answer `200` with an array and take `pageNumber`, `pageSize`, `q`,
`sort`, `fields`, `Accept-Language` and `X-Total-Count`.

## Verified groundwork

Measured on 2026-10-08 with the service added to the catalog in a scratch run.

- **Not in the catalog**, so the daily sync never fetched it and
  `SpecPathTests` reported nothing. No facade calls `/search/`. The Node SDK
  has nothing either, by GitHub code search.
- **Tenant `viu`**: the backend client holds both scopes; the three lists
  answer `200 []`. Custom types `PET` (117 instances, string fields
  `mixins.attributes.name` and `mixins.attributes.id`) and `EMPLOYEE` exist.
- **The union rule retypes the wrong class.** `RetypeProperties` anchors on
  the class name, but its body is a lazy `.*?` that may run past the class's
  end, and the union pattern `[\w\.]+\??` cannot match
  `ICollection<QueryNode>?`. So the entry `SearchRequest.Queries` ran on into
  `SavedQuery` and retyped `SavedQuery.Queries` — a plain `QueryNode` — while
  reporting success. The nine retypes on `main` are all in the right class;
  the defect shows only where a union's first branch is generic.
- **`queries` is `oneOf [array of QueryNode, QueryNode]`** on `SearchRequest`
  and `SavedQueryRequest`. A node is an `and` group, an `or` group or one
  query, so the single node covers everything; the specification says a list
  «is stored as one `or` group», and `SavedQuery` returns one node. A patch to
  `allOf: [$ref QueryNode]` gave `QueryNode? Queries` on all three classes.
- **`QueryNode.boost` has `default: 1`**, which NSwag turns into
  `Boost = 1D`. Every node would send `"boost":1`, group nodes included, which
  the specification forbids: «Do not combine those forms in the same object».
- **`FilterNode.Value` has no type** and is generated as `object?`. A
  source-generated context writes it only for runtime types it has
  registered.
- **The search body** generates as an empty class `Body`; `SearchRequest` and
  `SavedSearchCall` are generated as named types.

## Design

### Generator

1. **`RetypeProperties` stays inside its class.** The body may not cross
   another `partial class` declaration, and the union pattern accepts one
   generic argument list. Afterwards a property that is not in its class is
   reported as `NOT FOUND`, never retyped elsewhere. On `main` the generated
   code does not change.
2. **Two `SpecPatch` entries for `search-service`:** `queries` narrowed to one
   `QueryNode`, and `default: 1` dropped from `boost` — the server's default
   still applies when the field is absent.

### Facade — `client.Search`

```csharp
SearchTypeOperations pets = client.Search.ForType("PET");

PaginatedItems<SearchHit> hits = await pets.SearchAsync(new SearchRequest
{
    Index = "pets",
    Queries = new QueryNode { Type = SearchType.TEXT, Field = "mixins.attributes.name", Query = "rex" },
    Filters = new FilterNode { Field = "mixins.attributes.id", Operator = FilterOperator.EQ, Value = "p1" },
});
```

| Member | Call |
|---|---|
| `SearchService.ListQueriesAsync(q, pageNumber, pageSize)` | `GET …/search/queries` |
| `SearchService.ListIndexesAsync(q, pageNumber, pageSize)` | `GET …/search/indexes` |
| `SearchService.ListJobsAsync(q, pageNumber, pageSize)` | `GET …/jobs` |
| `SearchService.GetJobAsync(jobId)` | `GET …/jobs/{id}` |
| `SearchService.ForType(type)` | — returns `SearchTypeOperations` |
| `SearchAsync(SearchRequest, pageNumber, pageSize, sort)` | `POST …/search/{type}` |
| `RunSavedSearchAsync(savedSearchId, text, pageNumber, pageSize)` | the same, with a `SavedSearchCall` |
| `ListQueriesAsync`, `GetQueryAsync`, `UpsertQueryAsync` → `SavedQueryId?`, `DeleteQueryAsync` | `…/search/{type}/queries` |
| `ListIndexesAsync`, `GetIndexAsync`, `UpsertIndexAsync` → `JobId?`, `DeleteIndexAsync` → `JobId?` | `…/search/{type}/indexes` |

- The per-type group follows `SchemaService.InstancesOf`: constructed with
  `new SearchTypeOperations(_http, $"…")`, the form `SpecPathTests` resolves.
- **Auth** defaults to a service token (`Defaults.Service`): the
  specification knows only OAuth2.
- **The search is repeatable** (`Idempotent = true`): it carries its criteria
  in the body and changes nothing. PUT and DELETE are retried by method, as
  everywhere; a retried index upsert after a timeout may answer `409` on the
  version.
- **Lists return `PaginatedItems<T>`** through `SendPageAsync`. Left out until
  someone needs them: `sort` on lists, `fields`, `Accept-Language`,
  `Content-Language` and `X-Total-Count`.
- **Filter values** stay `object?`. `SearchJsonContext` registers `string`,
  `int`, `long`, `double`, `decimal`, `bool`, `DateTimeOffset`, the arrays of
  all but `bool`, and `JsonElement`. Any other runtime type throws
  `NotSupportedException` before the request leaves; the XML docs list the
  types.
- XML docs name the preview status, that `{type}` is a custom schema type,
  that an index builds asynchronously — `UpsertIndexAsync` returns the job to
  poll with `GetJobAsync` — and that an upsert of an existing resource needs
  `metadata.version`.

### Verification

- Unit tests in `SearchServiceTests.cs`, one example body taken from the
  specification; `SpecSyncTests` for the fixer and both patches.
- `SpecPathTests` turns from thirteen gaps to none; `SpecResponseTests`
  checks every typed read.
- Smoke test, decided 2026-10-08: a write pass on `PET` with a throwaway
  index `emporix-sdk-smoke`, the index job polled to success, a text search,
  an `or` group, a filter whose value goes out through `object?`, a saved
  search created, run, read and deleted, then the index deleted. **The index
  jobs stay** in the job list; there is no delete for them.

## Assumptions

- The facade is named `SearchService` / `client.Search`; generated types live
  in `Viu.Emporix.SearchServiceModels`.
- Because the service is preview and moves daily, Task 2 of the plan
  re-fetches and compares the thirteen operations before anything is built.
