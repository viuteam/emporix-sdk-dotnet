using System.Net;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class PaginationTests
{
    private static EmporixHttpClient Create(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Options.Create(new EmporixOptions { Tenant = "acme" }));

    private static EmporixRequest ListRequest() => new()
    {
        Method = HttpMethod.Get,
        Path = "/product/acme/products",
        Auth = AuthContext.Anonymous(),
    };

    private static StubHttpMessageHandler Page(
        int itemCount,
        string? totalCount = null,
        string? nextCursor = null,
        string? previousCursor = null)
    {
        string body = "[" + string.Join(
            ",",
            Enumerable.Range(1, itemCount).Select(i => $$"""{"id":"p{{i}}","name":"Item {{i}}"}""")) + "]";

        return new StubHttpMessageHandler((_, _) =>
        {
            HttpResponseMessage response = StubHttpMessageHandler.Json(HttpStatusCode.OK, body);
            if (totalCount is not null)
            {
                response.Headers.TryAddWithoutValidation("X-Total-Count", totalCount);
            }

            if (nextCursor is not null)
            {
                response.Headers.TryAddWithoutValidation("X-Next-Cursor", nextCursor);
            }

            if (previousCursor is not null)
            {
                response.Headers.TryAddWithoutValidation("X-Prev-Cursor", previousCursor);
            }

            return response;
        });
    }

    private static Task<PaginatedItems<TestProduct>> Fetch(
        StubHttpMessageHandler handler,
        int pageNumber = 1,
        int pageSize = 10)
        => Create(handler).SendPageAsync(
            ListRequest(),
            TestJsonContext.Default.ListTestProduct,
            pageNumber,
            pageSize);

    // ---------- Step 1: cursor ----------

    [Fact]
    public async Task A_next_cursor_means_there_is_another_page()
    {
        // Even on a half-full page: the cursor is the most reliable answer.
        PaginatedItems<TestProduct> page = await Fetch(Page(3, nextCursor: "abc"), pageSize: 10);

        Assert.True(page.HasNextPage);
        Assert.Equal("abc", page.NextCursor);
    }

    [Fact]
    public async Task Cursors_are_read_from_the_headers()
    {
        PaginatedItems<TestProduct> page =
            await Fetch(Page(3, nextCursor: "next", previousCursor: "previous"));

        Assert.Equal("next", page.NextCursor);
        Assert.Equal("previous", page.PreviousCursor);
    }

    [Fact]
    public async Task A_missing_cursor_says_nothing_about_the_last_page()
    {
        // Hardly any endpoint has cursors at all — their absence must not be
        // read as «last page».
        PaginatedItems<TestProduct> page = await Fetch(Page(10), pageSize: 10);

        Assert.Null(page.NextCursor);
        Assert.True(page.HasNextPage);
    }

    // ---------- Step 2: total count ----------

    [Fact]
    public async Task Total_count_decides_when_it_is_present()
    {
        PaginatedItems<TestProduct> page = await Fetch(Page(10, totalCount: "25"), pageNumber: 1, pageSize: 10);

        Assert.Equal(25, page.TotalCount);
        Assert.True(page.HasNextPage);
    }

    [Fact]
    public async Task Total_count_recognises_the_last_page()
    {
        PaginatedItems<TestProduct> page = await Fetch(Page(5, totalCount: "25"), pageNumber: 3, pageSize: 10);

        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task Total_count_beats_the_full_page_guess()
    {
        // A full page, but the total says that was everything. Without step 2
        // this would cost one pointless request.
        PaginatedItems<TestProduct> page = await Fetch(Page(10, totalCount: "10"), pageNumber: 1, pageSize: 10);

        Assert.False(page.HasNextPage);
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("-5")]
    [InlineData("")]
    [InlineData("3.5")]
    public async Task An_unusable_total_count_header_is_ignored(string value)
    {
        // Read as a number, an unusable value would quietly declare every page
        // the last. Treated as «not stated», step 3 applies.
        PaginatedItems<TestProduct> page = await Fetch(Page(10, totalCount: value), pageSize: 10);

        Assert.Null(page.TotalCount);
        Assert.True(page.HasNextPage);
    }

    // ---------- Step 3: full page ----------

    [Fact]
    public async Task A_full_page_suggests_another_one()
    {
        PaginatedItems<TestProduct> page = await Fetch(Page(10), pageSize: 10);

        Assert.True(page.HasNextPage);
    }

    [Fact]
    public async Task A_partial_page_is_the_last_one()
    {
        PaginatedItems<TestProduct> page = await Fetch(Page(4), pageSize: 10);

        Assert.False(page.HasNextPage);
        Assert.Equal(4, page.Items.Count);
    }

    [Fact]
    public async Task An_empty_page_is_the_last_one()
    {
        PaginatedItems<TestProduct> page = await Fetch(Page(0), pageSize: 10);

        Assert.False(page.HasNextPage);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task An_empty_body_is_an_empty_page_not_an_error()
    {
        StubHttpMessageHandler handler = new(
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) });

        PaginatedItems<TestProduct> page = await Fetch(handler);

        Assert.Empty(page.Items);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task Page_reports_back_what_was_requested()
    {
        PaginatedItems<TestProduct> page = await Fetch(Page(10), pageNumber: 3, pageSize: 10);

        Assert.Equal(3, page.PageNumber);
        Assert.Equal(10, page.PageSize);
    }

    // ---------- Walking every page ----------

    [Fact]
    public async Task Enumerates_across_pages()
    {
        int[] pageSizes = [3, 3, 2];
        List<int> requested = [];

        List<string> all = [];
        await foreach (string id in PaginatedItems.EnumerateAllAsync(
            (pageNumber, _) =>
            {
                requested.Add(pageNumber);
                int count = pageSizes[pageNumber - 1];
                return Task.FromResult(new PaginatedItems<string>(
                    [.. Enumerable.Range(1, count).Select(i => $"s{pageNumber}-{i}")],
                    pageNumber,
                    pageSize: 3,
                    hasNextPage: count == 3));
            }))
        {
            all.Add(id);
        }

        Assert.Equal(8, all.Count);
        Assert.Equal([1, 2, 3], requested);
        Assert.Equal("s1-1", all[0]);
        Assert.Equal("s3-2", all[^1]);
    }

    [Fact]
    public async Task Enumeration_stops_on_an_empty_page_even_if_more_are_claimed()
    {
        // Protection against an endpoint that stubbornly reports «there is more»:
        // without this stop the walk would never end.
        int calls = 0;

        await foreach (string _ in PaginatedItems.EnumerateAllAsync(
            (pageNumber, _) =>
            {
                calls++;
                return Task.FromResult(new PaginatedItems<string>(
                    [],
                    pageNumber,
                    pageSize: 10,
                    hasNextPage: true));
            }))
        {
            // Nothing is expected here.
        }

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Enumeration_can_start_at_a_later_page()
    {
        List<int> requested = [];

        await foreach (string _ in PaginatedItems.EnumerateAllAsync(
            (pageNumber, _) =>
            {
                requested.Add(pageNumber);
                return Task.FromResult(new PaginatedItems<string>(
                    ["x"],
                    pageNumber,
                    pageSize: 10,
                    hasNextPage: false));
            },
            startPage: 5))
        {
            // Only the page number matters.
        }

        Assert.Equal([5], requested);
    }

    [Fact]
    public async Task Enumeration_passes_the_cancellation_token_through()
    {
        using CancellationTokenSource cts = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (string _ in PaginatedItems.EnumerateAllAsync(
                async (pageNumber, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    await cts.CancelAsync();
                    return new PaginatedItems<string>(
                        ["x"],
                        pageNumber,
                        pageSize: 1,
                        hasNextPage: true);
                },
                startPage: 1,
                cts.Token))
            {
                // The second page fetch must cancel.
            }
        });
    }

    [Fact]
    public void Enumeration_rejects_invalid_arguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PaginatedItems.EnumerateAllAsync<string>(null!));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PaginatedItems.EnumerateAllAsync<string>(
                (_, _) => Task.FromResult(new PaginatedItems<string>([], 1, 10, false)),
                startPage: 0));
    }
}
