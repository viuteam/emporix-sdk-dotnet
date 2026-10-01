using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

/// <summary>
/// Feeds facades the example answers their specifications show.
/// </summary>
/// <remarks>
/// <see cref="SpecResponseTests"/> compares a facade's type with the declared
/// schema without running anything. These run the deserializer on the body the
/// specification gives as its example, for the reads that check found wrong:
/// each of them threw on that body, or read it into a type where nothing landed.
/// A stub copied from the specification is still not the API, but it is not the
/// test author's belief either, which is what every earlier stub of these
/// calls was.
/// </remarks>
public class SpecExampleTests
{
    private static IOptions<EmporixOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EmporixOptions { Tenant = "acme" });

    private static EmporixHttpClient Http(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Options());

    [Fact]
    public async Task A_quote_history_reads_as_one_flat_list_of_entries()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """[{"id":"6447b45cac0475131f3a6261","op":"ADD","path":"/items","userId":"u1"}]""");

        IReadOnlyList<QuoteModels.QuoteHistoryEntry> history =
            await new QuoteService(Http(handler), Options()).GetHistoryAsync("q1");

        QuoteModels.QuoteHistoryEntry entry = Assert.Single(history);
        Assert.Equal("6447b45cac0475131f3a6261", entry.Id);
        Assert.Equal("u1", entry.UserId);
    }

    [Fact]
    public async Task Bulk_category_assignments_read_one_result_per_assignment()
    {
        const string Body = """[{"assignmentId":"a1","index":0,"code":201,"status":"CREATED"}]""";
        StubHttpMessageHandler created = new(HttpStatusCode.MultiStatus, Body);
        StubHttpMessageHandler upserted = new(HttpStatusCode.MultiStatus, Body);

        IReadOnlyList<CategoryModels.BulkAssignmentResult> first =
            await new CategoryService(Http(created), Options()).Assignments
                .CreateManyAsync("cat1", new CategoryModels.BulkAssignmentRequest());
        IReadOnlyList<CategoryModels.BulkAssignmentResult> second =
            await new CategoryService(Http(upserted), Options()).Assignments
                .BulkUpsertByReferenceAsync("cat1", new CategoryModels.BulkAssignmentUpsertRequest());

        Assert.Equal("a1", Assert.Single(first).AssignmentId);
        Assert.Equal(201, Assert.Single(second).Code);
    }

    [Fact]
    public async Task Bulk_instance_writes_read_one_result_per_instance()
    {
        const string Body = """
            [{"index":0,"code":201,"status":"Created"},
             {"index":1,"code":409,"status":"Conflict","message":"Custom instance with id='456' already exists"}]
            """;
        JsonElement instances = JsonDocument.Parse("[]").RootElement;
        CustomInstanceOperations recipes = new SchemaService(
            Http(new StubHttpMessageHandler(HttpStatusCode.MultiStatus, Body)),
            Options()).InstancesOf("recipe");

        IReadOnlyList<IReadOnlyList<SchemaModels.BulkResponseEntry>> results =
        [
            await recipes.CreateManyAsync(instances),
            await recipes.ReplaceManyAsync(instances),
            await recipes.UpdateManyAsync(new SchemaModels.BulkPatchCustomInstanceRequest()),
            await recipes.DeleteManyAsync(instances),
        ];

        Assert.All(results, result => Assert.Equal(409, result[1].Code));
    }

    [Fact]
    public async Task Client_names_and_site_codes_read_as_strings()
    {
        StubHttpMessageHandler clients = new(HttpStatusCode.OK, """["saas-ag.caas-indexing-service-client"]""");
        StubHttpMessageHandler sites = new(HttpStatusCode.OK, """["UK","FR","main","DE"]""");

        IReadOnlyList<string> names = await new ConfigurationService(Http(clients), Options()).ListClientsAsync();
        IReadOnlyList<string> codes = await new SiteService(Http(sites), Options()).ListShortAsync();

        Assert.Equal(["saas-ag.caas-indexing-service-client"], names);
        Assert.Equal(["UK", "FR", "main", "DE"], codes);
    }

    [Fact]
    public async Task A_generated_delivery_cycle_reads_as_the_string_the_specification_declares()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, "\"64a80cf4c123c90e6c263789\"");

        string? cycle = await new ShippingService(Http(handler), Options())
            .GenerateDeliveryCyclesAsync(new ShippingModels.DeliveryCycle());

        Assert.Equal("64a80cf4c123c90e6c263789", cycle);
    }

    [Fact]
    public async Task A_cart_search_reads_carts()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            [{"id":"68481e9e8bf22744fc578572","customerId":"45620894","currency":"EUR","siteCode":"GrossSite",
              "items":[{"id":"0","keepAsSeparateLineItem":true,"type":"INTERNAL"}]}]
            """);

        PaginatedItems<CartModels.CartGetAll> carts = await new CartService(Http(handler), Options())
            .SearchAsync("status:OPEN", AuthContext.Service());

        CartModels.CartGetAll cart = Assert.Single(carts.Items);
        Assert.Equal("45620894", cart.CustomerId);
        Assert.Equal("0", Assert.Single(cart.Items!).Id);
    }

    [Fact]
    public async Task Webhook_configurations_read_whole()
    {
        StubHttpMessageHandler list = new(
            HttpStatusCode.OK,
            """[{"code":"svix_shared","provider":"SVIX_SHARED","active":true},{"code":"http","provider":"HTTP","active":false}]""");
        StubHttpMessageHandler one = new(
            HttpStatusCode.OK,
            """{"code":"svix_shared","provider":"SVIX_SHARED","active":true}""");

        IReadOnlyList<WebhookModels.WebhookConfigListItem> all =
            await new WebhookService(Http(list), Options()).ListAsync();
        WebhookModels.WebhookConfig? shared =
            await new WebhookService(Http(one), Options()).GetAsync("svix_shared");

        Assert.Equal(["svix_shared", "http"], all.Select(c => c.Code));
        Assert.Equal(WebhookModels.Provider.SVIX_SHARED, shared?.Provider);
        Assert.True(shared?.Active);
    }
}
