using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix.Tests;

public class ProductServiceTests
{
    private static ProductService Create(StubHttpMessageHandler handler)
    {
        IOptions<EmporixOptions> options = Options.Create(new EmporixOptions { Tenant = "acme" });

        return new ProductService(
            new EmporixHttpClient(new HttpClient(handler), options),
            options,
            NullLogger<ProductService>.Instance);
    }

    private static StubHttpMessageHandler Products(params string[] ids)
        => new(
            HttpStatusCode.OK,
            "[" + string.Join(",", ids.Select(id => $$"""{"id":"{{id}}","code":"C-{{id}}"}""")) + "]");

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    // ---------- Addresses and defaults ----------

    [Fact]
    public async Task Get_addresses_the_tenant_scoped_path()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"p1","code":"C-1"}""");
        ProductService products = Create(handler);

        BasicProductWithId? product = await products.GetAsync("p1");

        Assert.Equal("/product/acme/products/p1", Uri(handler));
        Assert.Equal("p1", product?.Id);
    }

    [Fact]
    public async Task Get_escapes_the_identifier()
    {
        // An id containing a slash must not break the path apart.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"x"}""");
        ProductService products = Create(handler);

        await products.GetAsync("a/b");

        Assert.Equal("/product/acme/products/a%2Fb", Uri(handler));
    }

    [Fact]
    public async Task Reads_default_to_an_anonymous_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"p1"}""");
        ProductService products = Create(handler);

        await products.GetAsync("p1");

        Assert.True(handler.LastRequest!.Options.TryGetValue(
            EmporixRequestOptions.Auth,
            out AuthContext auth));
        Assert.Equal(AuthKind.Anonymous, auth.Kind);
    }

    [Fact]
    public async Task An_explicit_customer_token_wins_over_the_default()
    {
        // This is how personalised prices come about.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"p1"}""");
        ProductService products = Create(handler);

        await products.GetAsync("p1", AuthContext.Customer("kunde"));

        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Customer, auth.Kind);
    }

    [Fact]
    public async Task Writes_default_to_a_service_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, """{"id":"p1"}""");
        ProductService products = Create(handler);

        await products.CreateAsync(new BasicProductCreation());

        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Service, auth.Kind);
    }

    // ---------- Paging ----------

    [Fact]
    public async Task List_uses_the_page_size_from_the_specification()
    {
        // 60 per the Emporix specification. The Node SDK uses 50 — the difference
        // is deliberate and should surface if anyone changes it.
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.ListAsync();

        Assert.Contains("pageNumber=1", Uri(handler), StringComparison.Ordinal);
        Assert.Contains("pageSize=60", Uri(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Total_count_is_requested_through_a_header_not_the_address()
    {
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.ListAsync(new ProductPageOptions { IncludeTotalCount = true });

        Assert.Equal("true", handler.LastHeader("X-Total-Count"));
        Assert.DoesNotContain("totalCount", Uri(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Total_count_is_off_unless_asked_for()
    {
        // Emporix determines the count with a second query — that should not be
        // incurred by every list.
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.ListAsync();

        Assert.Null(handler.LastHeader("X-Total-Count"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task An_invalid_page_number_is_rejected_before_any_request(int pageNumber)
    {
        StubHttpMessageHandler handler = Products();
        ProductService products = Create(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await products.ListAsync(new ProductPageOptions { PageNumber = pageNumber }));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Enumerating_everything_walks_the_pages()
    {
        StubHttpMessageHandler handler = new((request, _) =>
        {
            string query = request.RequestUri!.Query;
            bool firstPage = query.Contains("pageNumber=1", StringComparison.Ordinal);

            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                firstPage ? """[{"id":"p1"},{"id":"p2"}]""" : """[{"id":"p3"}]""");
        });
        ProductService products = Create(handler);

        List<string?> ids = [];
        await foreach (BasicProductWithId product in products.ListAllAsync(pageSize: 2))
        {
            ids.Add(product.Id);
        }

        Assert.Equal(["p1", "p2", "p3"], ids);
        Assert.Equal(2, handler.CallCount);
    }

    // ---------- Search ----------

    [Fact]
    public async Task Search_passes_the_filter_through()
    {
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.SearchAsync("code:ABC");

        Assert.Contains("q=code%3AABC", Uri(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_by_name_builds_a_name_filter()
    {
        // A bare word would be rejected: the product filter expects field:value.
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.SearchByNameAsync("coffee");

        Assert.Contains("name:(~coffee)", System.Uri.UnescapeDataString(Uri(handler)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_by_name_neutralises_regex_characters()
    {
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.SearchByNameAsync("a+b*c");

        string decoded = System.Uri.UnescapeDataString(Uri(handler));
        Assert.Contains(@"a\+b\*c", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_by_name_replaces_breaking_characters_with_a_space()
    {
        // Replace rather than drop: «Access(instant)» must not run together into
        // one word, and doubled whitespace would match nothing.
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.SearchByNameAsync("Access(instant)");

        string decoded = System.Uri.UnescapeDataString(Uri(handler));
        Assert.Contains("Access instant", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", decoded, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("()")]
    [InlineData("\"\"")]
    public async Task A_search_term_with_nothing_left_makes_no_request(string term)
    {
        // An empty name filter would be a rejection — the empty page is the
        // more honest answer.
        StubHttpMessageHandler handler = Products();
        ProductService products = Create(handler);

        PaginatedItems<BasicProductWithId> page = await products.SearchByNameAsync(term);

        Assert.Empty(page.Items);
        Assert.False(page.HasNextPage);
        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Many at once ----------

    [Fact]
    public async Task Fetching_many_by_id_posts_the_filter_in_the_body()
    {
        // A hundred ids exceed the permitted address length.
        StubHttpMessageHandler handler = Products("p1", "p2");
        ProductService products = Create(handler);

        IReadOnlyList<BasicProductWithId> found = await products.GetManyByIdAsync(["p1", "p2"]);

        Assert.Equal(2, found.Count);
        Assert.Equal("/product/acme/products/search?pageSize=2", Uri(handler));
        Assert.Contains("id:(p1,p2)", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fetching_many_marks_the_post_as_repeatable()
    {
        // The call only reads — after a server error it may be repeated.
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.GetManyByIdAsync(["p1"]);

        Assert.True(handler.LastRequest!.Options.TryGetValue(
            EmporixRequestOptions.Idempotent,
            out bool idempotent));
        Assert.True(idempotent);
    }

    [Fact]
    public async Task Fetching_many_splits_into_chunks()
    {
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.GetManyByIdAsync([.. Enumerable.Range(1, 250).Select(i => $"p{i}")], chunkSize: 100);

        Assert.Equal(3, handler.CallCount);
        Assert.Contains("pageSize=100", Uri(handler), StringComparison.Ordinal);
        Assert.Contains("pageSize=50", Uri(handler, 2), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_list_makes_no_request()
    {
        StubHttpMessageHandler handler = Products();
        ProductService products = Create(handler);

        Assert.Empty(await products.GetManyByIdAsync([]));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Duplicate_codes_are_collapsed()
    {
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.GetManyByCodeAsync(["A", "B", "A"]);

        Assert.Contains("code:(A,B)", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Codes_containing_delimiters_are_skipped()
    {
        // Emporix' query language uses these characters as delimiters and cannot
        // escape them inside a list.
        StubHttpMessageHandler handler = Products("p1");
        ProductService products = Create(handler);

        await products.GetManyByCodeAsync(["GOOD", "with,comma", "with space", "with(paren)"]);

        Assert.Contains("code:(GOOD)", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_unusable_codes_means_no_request()
    {
        StubHttpMessageHandler handler = Products();
        ProductService products = Create(handler);

        Assert.Empty(await products.GetManyByCodeAsync(["a,b", "c d"]));
        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Variants ----------

    [Fact]
    public async Task Listing_variants_encapsulates_the_filter_syntax()
    {
        StubHttpMessageHandler handler = Products();
        ProductService products = Create(handler);

        await foreach (BasicProductWithId _ in products.ListVariantsAsync("parent-1"))
        {
            // The constructed filter is what is under test.
        }

        string decoded = System.Uri.UnescapeDataString(Uri(handler));
        Assert.Contains("productType:VARIANT parentVariantId:parent-1", decoded, StringComparison.Ordinal);
        Assert.Contains("pageSize=200", decoded, StringComparison.Ordinal);
    }

    // ---------- Writes ----------

    [Fact]
    public async Task Create_sends_the_product_as_json()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, """{"id":"new"}""");
        ProductService products = Create(handler);

        ResourceLocation? created = await products.CreateAsync(new BasicProductCreation { Code = "C-1" });

        Assert.Equal("new", created?.Id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("\"code\":\"C-1\"", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_uses_patch_and_replace_uses_put()
    {
        StubHttpMessageHandler patchHandler = new(HttpStatusCode.NoContent, string.Empty);
        await Create(patchHandler).UpdateAsync("p1", new ProductPartialUpdate());
        Assert.Equal(HttpMethod.Patch, patchHandler.LastRequest!.Method);

        StubHttpMessageHandler putHandler = new(HttpStatusCode.NoContent, string.Empty);
        await Create(putHandler).ReplaceAsync("p1", new BasicProductUpdate());
        Assert.Equal(HttpMethod.Put, putHandler.LastRequest!.Method);
    }

    [Fact]
    public async Task Write_options_become_query_parameters()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, """{"id":"p1"}""");
        ProductService products = Create(handler);

        await products.CreateAsync(
            new BasicProductCreation(),
            new ProductWriteOptions { SkipVariantGeneration = true, DoIndex = false });

        Assert.Contains("skipVariantGeneration=true", Uri(handler), StringComparison.Ordinal);
        Assert.Contains("doIndex=false", Uri(handler), StringComparison.Ordinal);
        // Values that were not set do not appear at all.
        Assert.DoesNotContain("skipRelatedItemsValidation", Uri(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_forwards_the_force_flag()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        ProductService products = Create(handler);

        await products.DeleteAsync("p1", force: true);

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Contains("force=true", Uri(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bulk_create_returns_one_result_per_entry()
    {
        // 207: individual failures raise no exception, they sit in the result.
        StubHttpMessageHandler handler = new(
            HttpStatusCode.MultiStatus,
            """[{"code":201},{"code":400}]""");
        ProductService products = Create(handler);

        IReadOnlyList<BulkResponse> results = await products.CreateManyAsync(
            [new BasicProductCreation(), new BasicProductCreation()]);

        Assert.Equal(2, results.Count);
        Assert.Equal("/product/acme/products/bulk", Uri(handler));
    }

    [Fact]
    public async Task Bulk_create_of_nothing_makes_no_request()
    {
        StubHttpMessageHandler handler = Products();
        ProductService products = Create(handler);

        Assert.Empty(await products.CreateManyAsync([]));
        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Failures ----------

    [Fact]
    public async Task A_missing_product_surfaces_as_not_found()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.NotFound,
            """{"message":"Product not found","errorCode":"PRODUCT_NOT_FOUND"}""");
        ProductService products = Create(handler);

        EmporixNotFoundException exception =
            await Assert.ThrowsAsync<EmporixNotFoundException>(async () => await products.GetAsync("weg"));

        Assert.Equal("PRODUCT_NOT_FOUND", exception.ErrorCode);
        Assert.NotNull(exception.CorrelationId);
    }

    [Fact]
    public async Task A_missing_scope_names_the_scope()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.Forbidden,
            """{"message":"Forbidden","details":["missing scope: product.product_manage"]}""");
        ProductService products = Create(handler);

        EmporixInsufficientScopeException exception =
            await Assert.ThrowsAsync<EmporixInsufficientScopeException>(async () =>
                await products.CreateAsync(new BasicProductCreation()));

        Assert.Equal("product.product_manage", exception.RequiredScope);
    }

    [Fact]
    public async Task Get_by_code_yields_null_when_nothing_matches()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        Assert.Null(await products.GetByCodeAsync("doesnotexist"));
    }

    [Fact]
    public async Task Empty_arguments_are_rejected()
    {
        StubHttpMessageHandler handler = Products();
        ProductService products = Create(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () => await products.GetAsync("  "));
        await Assert.ThrowsAsync<ArgumentException>(async () => await products.SearchAsync(""));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Update_can_patch_a_bundles_contents()
    {
        // The capability the old signature withheld: BasicProductUpdate has no
        // property for bundledProducts, so this body could not be built at all.
        // The specification declares PATCH as productPartialUpdate, which
        // carries the union of the type-specific fields.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, "");
        ProductService products = Create(handler);

        await products.UpdateAsync(
            "g1",
            new ProductPartialUpdate
            {
                // Assigned rather than initialized into: this property is
                // nullable here, unlike on the bundle types, where it carries a
                // default instance. The collection-initializer form compiles
                // against the null and throws at runtime.
                BundledProducts = [new Anonymous { ProductId = "p1", Amount = 3 }],
            });

        Assert.Equal(HttpMethod.Patch, handler.RequestMethods[0]);
        Assert.Equal("/product/acme/products/g1", Uri(handler));
        Assert.Contains("\"productId\":\"p1\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"amount\":3", handler.RequestBodies[0], StringComparison.Ordinal);
    }
}
