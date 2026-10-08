using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Viu.Emporix.SearchServiceModels;

namespace Viu.Emporix.Tests;

public class SearchServiceTests
{
    private static SearchService Create(StubHttpMessageHandler handler)
    {
        IOptions<EmporixOptions> options = Options.Create(new EmporixOptions { Tenant = "acme" });

        return new SearchService(new EmporixHttpClient(new HttpClient(handler), options), options);
    }

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

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
}
