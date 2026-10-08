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
}
