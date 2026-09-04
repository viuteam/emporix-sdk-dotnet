using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix.Tests;

/// <summary>
/// The reads that resolve the product type.
/// </summary>
/// <remarks>
/// These assert the addresses, not Emporix's behaviour — the group has to build
/// exactly what the plain methods build, since it goes through the same cores.
/// What the resolution itself does is covered by
/// <see cref="EmporixProductConverterTests"/>, where it is a pure function.
/// </remarks>
public class ProductAnyTypeTests
{
    private static ProductService Create(StubHttpMessageHandler handler)
    {
        IOptions<EmporixOptions> options = Options.Create(new EmporixOptions { Tenant = "acme" });

        return new ProductService(
            new EmporixHttpClient(new HttpClient(handler), options),
            options,
            NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task Get_addresses_the_same_path_as_the_plain_read()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK, """{"id":"g1","code":"gift","productType":"BUNDLE"}""");
        ProductService products = Create(handler);

        IEmporixProduct? product = await products.AnyType.GetAsync("g1");

        Assert.Equal("/product/acme/products/g1", handler.RequestUris[0].PathAndQuery);
        Assert.IsType<BundleProductWithId>(product);
    }

    [Fact]
    public async Task GetByCode_filters_by_code_and_resolves_the_shape()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK, """[{"id":"v1","code":"red-m","productType":"VARIANT"}]""");
        ProductService products = Create(handler);

        IEmporixProduct? product = await products.AnyType.GetByCodeAsync("red-m");

        Assert.Contains("q=code", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
        Assert.IsType<VariantProductWithId>(product);
    }

    [Fact]
    public async Task List_pages_the_same_way_and_resolves_each_element()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            [{"id":"b1","code":"plain","productType":"BASIC"},
             {"id":"g1","code":"gift","productType":"BUNDLE"}]
            """);
        ProductService products = Create(handler);

        PaginatedItems<IEmporixProduct> page = await products.AnyType.ListAsync();

        Assert.Contains("pageNumber=1", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
        Assert.Collection(
            page.Items,
            p => Assert.IsType<BasicProductWithId>(p),
            p => Assert.IsType<BundleProductWithId>(p));
    }

    [Fact]
    public async Task Search_passes_the_query_through()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await products.AnyType.SearchAsync("productType:BUNDLE");

        Assert.Contains("q=productType", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchByName_reaches_the_same_endpoint()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await products.AnyType.SearchByNameAsync("coffee");

        Assert.StartsWith("/product/acme/products", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetManyById_chunks_like_the_plain_read()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await products.AnyType.GetManyByIdAsync(["a", "b"]);

        // A POST to /search with the filter in the body: a hundred ids exceed
        // the permitted address length, so it does not travel as a query
        // parameter.
        Assert.Equal("/product/acme/products/search?pageSize=2", handler.RequestUris[0].PathAndQuery);
        Assert.Contains("id:(a,b)", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetManyByCode_chunks_like_the_plain_read()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await products.AnyType.GetManyByCodeAsync(["x", "y"]);

        Assert.Equal("/product/acme/products/search?pageSize=2", handler.RequestUris[0].PathAndQuery);
        Assert.Contains("code:(x,y)", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetManyByCode_drops_a_code_the_filter_cannot_carry()
    {
        // The plain read collapses duplicates and drops codes containing a
        // comma or a quote, because the query language uses those as
        // delimiters. Reaching the chunked search directly would skip that and
        // send a broken filter — which is why the cleaning is a core of its own
        // rather than logic in the public method.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await products.AnyType.GetManyByCodeAsync(["good", "bad,code", "good"]);

        // The filter travels in the body here: the chunked search is a POST.
        Assert.Contains("code:(good)", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("bad", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    // ---------- Walking every page ----------

    [Fact]
    public async Task ListAll_resolves_each_element_across_page_boundaries()
    {
        // The core of these two methods: a walker that resolves per element,
        // not per page. A page of bundles followed by a page of variants has to
        // come out as bundles and variants, not as whichever the first page was.
        StubHttpMessageHandler handler = new((request, _) =>
        {
            bool first = request.RequestUri!.Query.Contains("pageNumber=1", StringComparison.Ordinal);

            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                first
                    ? """[{"id":"b1","productType":"BASIC"},{"id":"g1","productType":"BUNDLE"}]"""
                    : """[{"id":"v1","productType":"VARIANT"}]""");
        });
        ProductService products = Create(handler);

        List<IEmporixProduct> all = [];

        await foreach (IEmporixProduct product in products.AnyType.ListAllAsync(pageSize: 2))
        {
            all.Add(product);
        }

        Assert.Collection(
            all,
            p => Assert.IsType<BasicProductWithId>(p),
            p => Assert.IsType<BundleProductWithId>(p),
            p => Assert.IsType<VariantProductWithId>(p));

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ListVariants_builds_the_same_filter_as_the_plain_walker()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await foreach (IEmporixProduct _ in products.AnyType.ListVariantsAsync("parent-1"))
        {
            // The constructed filter is what is under test.
        }

        string decoded = System.Uri.UnescapeDataString(handler.RequestUris[0].PathAndQuery);

        Assert.Contains("productType:VARIANT parentVariantId:parent-1", decoded, StringComparison.Ordinal);
        Assert.Contains("pageSize=200", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListVariants_returns_variants_as_variants()
    {
        // Why this method is worth having at all. The filter pins productType
        // to VARIANT, so every result is known to be one — and the plain walker
        // still hands back the basic shape, with parentVariantId reachable only
        // through the extension data.
        StubHttpMessageHandler handler = new((request, _) =>
        {
            bool first = request.RequestUri!.Query.Contains("pageNumber=1", StringComparison.Ordinal);

            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                first
                    ? """[{"id":"v1","productType":"VARIANT","parentVariantId":"parent-1"}]"""
                    : "[]");
        });
        ProductService products = Create(handler);

        List<string?> parents = [];

        await foreach (IEmporixProduct product in products.AnyType.ListVariantsAsync("parent-1", pageSize: 1))
        {
            VariantProductWithId variant = Assert.IsType<VariantProductWithId>(product);
            parents.Add(variant.ParentVariantId);
        }

        Assert.Equal(["parent-1"], parents);
    }

    [Fact]
    public async Task ListAll_defaults_to_the_same_page_size_as_the_plain_walker()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await foreach (IEmporixProduct _ in products.AnyType.ListAllAsync())
        {
            // The page size is what is under test.
        }

        Assert.Contains("pageSize=60", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
    }
}
