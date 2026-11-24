using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

/// <summary>
/// The bulk and partial-update operations Emporix added on 2026-11-23.
/// </summary>
public class BulkOperationsTests
{
    private static IOptions<EmporixOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EmporixOptions { Tenant = "acme" });

    private static EmporixHttpClient Http(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Options());

    private static string Uri(StubHttpMessageHandler handler)
        => handler.RequestUris[0].PathAndQuery;

    private static bool Repeatable(StubHttpMessageHandler handler)
        => handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out bool idempotent)
            && idempotent;

    private static StubHttpMessageHandler MultiStatus()
        => new(
            HttpStatusCode.MultiStatus,
            """[{"index":0,"id":"x1","code":204,"status":"NO_CONTENT"},{"index":1,"code":404,"status":"NOT_FOUND","message":"gone"}]""");

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    // ---------- Availability ----------

    [Fact]
    public async Task Patching_an_availability_addresses_product_and_site_and_sends_the_operations()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        AvailabilityService availability = new(Http(handler), Options());

        await availability.PatchAsync(
            "p1",
            "main",
            [
                new AvailabilityChange
                {
                    Op = AvailabilityModels.AvailabilityPatchOperationOp.REPLACE,
                    Path = "/stockLevel",
                    Value = Json("5"),
                },
            ]);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/availability/acme/availability/p1/main", Uri(handler));
        Assert.Equal("""[{"op":"REPLACE","path":"/stockLevel","value":5}]""", handler.RequestBodies[0]);
        Assert.False(Repeatable(handler));
    }

    [Fact]
    public async Task Patching_an_availability_with_no_operations_makes_no_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        AvailabilityService availability = new(Http(handler), Options());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await availability.PatchAsync("p1", "main", []));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Patching_availabilities_in_bulk_names_each_product_and_site()
    {
        StubHttpMessageHandler handler = MultiStatus();
        AvailabilityService availability = new(Http(handler), Options());

        IReadOnlyList<AvailabilityModels.BulkResponse> results = await availability.PatchManyAsync(
        [
            new AvailabilityBulkPatch
            {
                ProductId = "p1",
                Site = "main",
                Changes = [new AvailabilityChange { Op = AvailabilityModels.AvailabilityPatchOperationOp.REMOVE, Path = "/distributionChannel" }],
            },
        ]);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/availability/acme/availability/bulk", Uri(handler));
        Assert.Equal(
            """[{"productId":"p1","site":"main","data":[{"op":"REMOVE","path":"/distributionChannel"}]}]""",
            handler.RequestBodies[0]);
        Assert.Equal([204, 404], results.Select(r => r.Code));
        Assert.False(Repeatable(handler));
    }

    // ---------- Media ----------

    [Fact]
    public async Task Creating_links_in_bulk_posts_the_list_and_is_never_repeated()
    {
        StubHttpMessageHandler handler = MultiStatus();
        MediaService media = new(Http(handler), Options());

        IReadOnlyList<MediaModels.AssetBulkResult> results = await media.CreateLinksAsync(
            [new MediaModels.AssetCreateLink { Url = "https://example.test/a.png" }]);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/media/acme/assets/bulk", Uri(handler));
        Assert.StartsWith("[{", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("https://example.test/a.png", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Equal("gone", results[1].Message);
        Assert.False(Repeatable(handler));
    }

    [Fact]
    public async Task Patching_assets_in_bulk_wraps_each_asset_s_operations()
    {
        StubHttpMessageHandler handler = MultiStatus();
        MediaService media = new(Http(handler), Options());

        await media.PatchManyAsync(
        [
            new MediaBulkPatch
            {
                Id = "a1",
                Operations =
                [
                    new MediaPatchOperation
                    {
                        Op = MediaModels.PatchOperationOp.Add,
                        Path = "/refIds/-",
                        Value = Json("""{"type":"PRODUCT","id":"p1"}"""),
                    },
                ],
            },
        ]);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/media/acme/assets/bulk", Uri(handler));
        Assert.Equal(
            """[{"id":"a1","data":[{"op":"add","path":"/refIds/-","value":{"type":"PRODUCT","id":"p1"}}]}]""",
            handler.RequestBodies[0]);
        Assert.False(Repeatable(handler));
    }

    [Fact]
    public async Task Deleting_assets_in_bulk_sends_the_ids_and_may_be_repeated()
    {
        StubHttpMessageHandler handler = MultiStatus();
        MediaService media = new(Http(handler), Options());

        await media.DeleteManyAsync(["a1", "a2"]);

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal("/media/acme/assets/bulk", Uri(handler));
        Assert.Equal("""["a1","a2"]""", handler.RequestBodies[0]);
        Assert.True(Repeatable(handler));
    }

    // ---------- Category ----------

    [Fact]
    public async Task Creating_categories_in_bulk_sends_publish_only_when_asked()
    {
        StubHttpMessageHandler handler = MultiStatus();
        CategoryService categories = new(Http(handler), Options());

        await categories.CreateManyAsync([new CategoryModels.CategoryCreateRequest { Published = true }], publish: true);
        await categories.CreateManyAsync([new CategoryModels.CategoryCreateRequest()]);

        Assert.Equal(HttpMethod.Post, handler.RequestMethods[0]);
        Assert.Equal("/category/acme/categories/bulk?publish=true", handler.RequestUris[0].PathAndQuery);
        Assert.Equal("/category/acme/categories/bulk", handler.RequestUris[1].PathAndQuery);
        Assert.False(Repeatable(handler));
    }

    [Fact]
    public async Task Patching_categories_in_bulk_carries_the_expected_version()
    {
        StubHttpMessageHandler handler = MultiStatus();
        CategoryService categories = new(Http(handler), Options());

        IReadOnlyList<CategoryModels.CategoryBulkResult> results = await categories.UpdateManyAsync(
        [
            new CategoryModels.CategoryBulkPatch
            {
                Id = "beverages",
                Metadata = new CategoryModels.CategoryBulkPatchMetadata { Version = 3 },
            },
        ]);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/category/acme/categories/bulk", Uri(handler));
        Assert.Contains("\"id\":\"beverages\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"version\":3", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Equal(2, results.Count);
        Assert.False(Repeatable(handler));
    }

    [Fact]
    public async Task Removing_assignments_by_reference_in_bulk_sends_the_references_and_may_be_repeated()
    {
        StubHttpMessageHandler handler = MultiStatus();
        CategoryService categories = new(Http(handler), Options());

        await categories.Assignments.BulkRemoveByReferenceAsync("c1", ["p1", "p2"]);

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal("/category/acme/categories/c1/assignments/references/bulk", Uri(handler));
        Assert.Equal("""["p1","p2"]""", handler.RequestBodies[0]);
        Assert.True(Repeatable(handler));
    }

    // ---------- Product ----------

    [Fact]
    public async Task Patching_products_in_bulk_carries_the_write_options()
    {
        StubHttpMessageHandler handler = MultiStatus();
        ProductService products = new(Http(handler), Options(), NullLogger<ProductService>.Instance);

        IReadOnlyList<ProductModels.BulkResponse> results = await products.PatchManyAsync(
            [new ProductModels.ProductBulkPartialUpdate { Id = "p1", Published = true }],
            new ProductWriteOptions { DoIndex = false });

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/product/acme/products/bulk?doIndex=false", Uri(handler));
        Assert.Equal("""[{"id":"p1","published":true}]""", handler.RequestBodies[0]);
        Assert.Equal(2, results.Count);
        Assert.False(Repeatable(handler));
    }

    // ---------- All of them ----------

    [Fact]
    public async Task Bulk_calls_with_nothing_to_do_make_no_request()
    {
        StubHttpMessageHandler handler = MultiStatus();

        Assert.Empty(await new AvailabilityService(Http(handler), Options()).PatchManyAsync([]));
        Assert.Empty(await new MediaService(Http(handler), Options()).CreateLinksAsync([]));
        Assert.Empty(await new MediaService(Http(handler), Options()).PatchManyAsync([]));
        Assert.Empty(await new MediaService(Http(handler), Options()).DeleteManyAsync([]));
        Assert.Empty(await new CategoryService(Http(handler), Options()).CreateManyAsync([]));
        Assert.Empty(await new CategoryService(Http(handler), Options()).UpdateManyAsync([]));
        Assert.Empty(await new CategoryService(Http(handler), Options()).Assignments.BulkRemoveByReferenceAsync("c1", []));
        Assert.Empty(await new ProductService(Http(handler), Options(), NullLogger<ProductService>.Instance).PatchManyAsync([]));

        Assert.Equal(0, handler.CallCount);
    }
}
