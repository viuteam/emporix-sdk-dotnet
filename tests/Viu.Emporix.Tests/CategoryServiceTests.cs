using System.Net;
using Microsoft.Extensions.Options;
using Viu.Emporix.CategoryModels;

namespace Viu.Emporix.Tests;

public class CategoryServiceTests
{
    private static CategoryService Create(StubHttpMessageHandler handler)
    {
        IOptions<EmporixOptions> options = Options.Create(new EmporixOptions { Tenant = "acme" });

        return new CategoryService(new EmporixHttpClient(new HttpClient(handler), options), options);
    }

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    private static StubHttpMessageHandler Categories(params string[] ids)
        => new(
            HttpStatusCode.OK,
            "[" + string.Join(",", ids.Select(id => $$"""{"id":"{{id}}"}""")) + "]");

    [Fact]
    public async Task Get_addresses_the_tenant_scoped_path()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"cat1"}""");
        CategoryService categories = Create(handler);

        Category? category = await categories.GetAsync("cat1");

        Assert.Equal("/category/acme/categories/cat1", Uri(handler));
        Assert.Equal("cat1", category?.Id);
    }

    [Fact]
    public async Task Reads_default_to_an_anonymous_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"cat1"}""");
        CategoryService categories = Create(handler);

        await categories.GetAsync("cat1");

        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Anonymous, auth.Kind);
    }

    [Fact]
    public async Task Writes_default_to_a_service_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, """{"id":"cat1"}""");
        CategoryService categories = Create(handler);

        await categories.CreateAsync(new CategoryCreateRequest());

        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Service, auth.Kind);
    }

    [Fact]
    public async Task The_subtree_comes_in_one_call()
    {
        // Useful for building navigation without walking level by level.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"root"}""");
        CategoryService categories = Create(handler);

        await categories.GetTreeAsync("root");

        Assert.Equal("/category/acme/categories/root/subcategories", Uri(handler));
    }

    [Fact]
    public async Task Listing_pages_with_the_specification_default()
    {
        StubHttpMessageHandler handler = Categories("cat1");
        CategoryService categories = Create(handler);

        await categories.ListAsync();

        Assert.Contains("pageSize=60", Uri(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Walking_every_category_follows_the_pages()
    {
        StubHttpMessageHandler handler = new((request, _) =>
        {
            bool firstPage = request.RequestUri!.Query.Contains("pageNumber=1", StringComparison.Ordinal);

            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                firstPage ? """[{"id":"c1"},{"id":"c2"}]""" : """[{"id":"c3"}]""");
        });
        CategoryService categories = Create(handler);

        List<string?> ids = [];
        await foreach (Category category in categories.ListAllAsync(pageSize: 2))
        {
            ids.Add(category.Id);
        }

        Assert.Equal(["c1", "c2", "c3"], ids);
    }

    // ---------- Assignments ----------

    [Fact]
    public async Task Assignments_live_under_the_category_path()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"id":"a1"}]""");
        CategoryService categories = Create(handler);

        IReadOnlyList<CategoryAssignment> assignments = await categories.Assignments.ListAsync("cat1");

        Assert.Single(assignments);
        Assert.Equal("/category/acme/categories/cat1/assignments", Uri(handler));
    }

    [Fact]
    public async Task Assignment_reads_are_anonymous_and_writes_are_service()
    {
        StubHttpMessageHandler read = new(HttpStatusCode.OK, "[]");
        await Create(read).Assignments.ListAsync("cat1");
        read.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext readAuth);
        Assert.Equal(AuthKind.Anonymous, readAuth.Kind);

        StubHttpMessageHandler write = new(HttpStatusCode.Created, string.Empty);
        await Create(write).Assignments.CreateAsync("cat1", new CategoryAssignment());
        write.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext writeAuth);
        Assert.Equal(AuthKind.Service, writeAuth.Kind);
    }

    [Fact]
    public async Task Deleting_an_assignment_addresses_it_by_id()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        CategoryService categories = Create(handler);

        await categories.Assignments.DeleteAsync("cat1", "a1");

        Assert.Equal("/category/acme/categories/cat1/assignments/a1", Uri(handler));
    }

    [Fact]
    public async Task Assignment_listing_returns_an_empty_list_rather_than_null()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, string.Empty);
        CategoryService categories = Create(handler);

        Assert.Empty(await categories.Assignments.ListAsync("cat1"));
    }

    [Fact]
    public async Task Empty_identifiers_are_rejected()
    {
        StubHttpMessageHandler handler = Categories();
        CategoryService categories = Create(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () => await categories.GetAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await categories.Assignments.ListAsync(""));
        Assert.Equal(0, handler.CallCount);
    }
}
