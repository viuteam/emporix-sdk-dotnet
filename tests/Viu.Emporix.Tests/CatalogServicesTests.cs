using System.Net;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class CatalogServicesTests
{
    private static IOptions<EmporixOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EmporixOptions { Tenant = "acme" });

    private static EmporixHttpClient Http(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Options());

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    // ---------- Brands ----------

    [Fact]
    public async Task Brand_paths_carry_no_tenant_segment()
    {
        // Unlike most services, brands and labels take the tenant from the token.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"id":"b1"}]""");
        BrandService brands = new(Http(handler));

        await brands.ListAsync();

        Assert.Equal("/brand/brands", Uri(handler));
    }

    [Fact]
    public async Task Brand_get_addresses_by_id()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"b1"}""");
        BrandService brands = new(Http(handler));

        Assert.NotNull(await brands.GetAsync("b1"));
        Assert.Equal("/brand/brands/b1", Uri(handler));
    }

    [Fact]
    public async Task Brand_update_uses_patch()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        BrandService brands = new(Http(handler));

        await brands.UpdateAsync("b1", new BrandServiceModels.UpdateBrand());

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task Brand_calls_default_to_a_service_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        BrandService brands = new(Http(handler));

        await brands.ListAsync();

        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Service, auth.Kind);
    }

    [Fact]
    public async Task Brand_list_returns_an_empty_list_rather_than_null()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, string.Empty);
        BrandService brands = new(Http(handler));

        Assert.Empty(await brands.ListAsync());
    }

    // ---------- Labels ----------

    [Fact]
    public async Task Label_paths_carry_no_tenant_segment()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"id":"l1"}]""");
        LabelService labels = new(Http(handler));

        await labels.ListAsync();

        Assert.Equal("/label/labels", Uri(handler));
    }

    [Fact]
    public async Task Label_delete_addresses_by_id()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        LabelService labels = new(Http(handler));

        await labels.DeleteAsync("l1");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal("/label/labels/l1", Uri(handler));
    }

    // ---------- Catalogs ----------

    [Fact]
    public async Task Catalog_paths_are_tenant_scoped()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"id":"cat1"}]""");
        CatalogService catalogs = new(Http(handler), Options());

        await catalogs.ListAsync();

        Assert.Equal("/catalog/acme/catalogs", Uri(handler));
    }

    [Fact]
    public async Task Catalog_list_forwards_an_optional_filter()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        CatalogService catalogs = new(Http(handler), Options());

        await catalogs.ListAsync("code:main");

        Assert.Contains("q=code%3Amain", Uri(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_list_omits_the_filter_when_absent()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        CatalogService catalogs = new(Http(handler), Options());

        await catalogs.ListAsync();

        Assert.Equal("/catalog/acme/catalogs", Uri(handler));
    }

    [Fact]
    public async Task Catalogs_can_be_found_by_the_category_they_contain()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        CatalogService catalogs = new(Http(handler), Options());

        await catalogs.ListForCategoryAsync("cat1");

        Assert.Equal("/catalog/acme/catalogs/categories/cat1", Uri(handler));
    }

    [Fact]
    public async Task Catalog_replace_uses_put_because_it_upserts()
    {
        // Emporix treats this as an upsert: an unknown id creates rather than fails.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        CatalogService catalogs = new(Http(handler), Options());

        await catalogs.ReplaceAsync("cat1", new CatalogModels.UpdateCatalog());

        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
    }

    // ---------- Shared behaviour ----------

    [Fact]
    public async Task Empty_identifiers_are_rejected_everywhere()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new BrandService(Http(handler)).GetAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new LabelService(Http(handler)).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new CatalogService(Http(handler), Options()).GetAsync("  "));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_missing_scope_names_the_scope()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.Forbidden,
            """{"message":"Forbidden","details":["missing scope: brand.brand_manage"]}""");
        BrandService brands = new(Http(handler));

        EmporixInsufficientScopeException exception =
            await Assert.ThrowsAsync<EmporixInsufficientScopeException>(async () =>
                await brands.CreateAsync(new BrandServiceModels.Brand()));

        Assert.Equal("brand.brand_manage", exception.RequiredScope);
    }
}
