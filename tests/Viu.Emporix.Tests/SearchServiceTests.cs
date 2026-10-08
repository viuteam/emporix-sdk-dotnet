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
