using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

/// <summary>
/// The platform services: IAM, schemas, sites, vendors, currencies, countries,
/// webhooks, units, sequential identifiers, configuration and session context.
/// </summary>
/// <remarks>
/// Weighted towards what a defect would hide: calls that consume something and
/// must never be retried, the two scopes of configuration, and the places where
/// «me» and an explicit id are different endpoints rather than one with a
/// parameter.
/// </remarks>
public class PlatformWaveTests
{
    private static IOptions<EmporixOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EmporixOptions { Tenant = "acme" });

    private static EmporixHttpClient Http(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Options());

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    private static bool IsRepeatable(StubHttpMessageHandler handler)
        => handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out bool v) && v;

    // ---------- Sequential identifiers ----------

    [Fact]
    public async Task Taking_the_next_identifier_is_never_repeatable()
    {
        // A retry burns a second number and leaves a gap in a sequence that is
        // usually expected to have none — an invoice range, typically.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"ORD-1"}""");
        SequentialIdService ids = new(Http(handler), Options());

        await ids.NextAsync("order", new SequentialIdModels.NextIdCommandRequest());

        Assert.Equal("/sequential-id/acme/schemas/types/order/nextId", Uri(handler));
        Assert.False(IsRepeatable(handler));
    }

    [Fact]
    public async Task The_batch_identifier_call_is_not_tenant_scoped()
    {
        // Emporix's own shape, and easy to «fix» into a 404 by adding the tenant.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        SequentialIdService ids = new(Http(handler), Options());

        await ids.NextManyAsync(new SequentialIdModels.SchemaBatchNextIdRequest());

        Assert.Equal("/sequential-id/sequenceSchemaBatch/nextIds", Uri(handler));
        Assert.False(IsRepeatable(handler));
    }

    // ---------- IAM ----------

    [Fact]
    public async Task Adding_a_member_by_body_and_by_address_are_different_calls()
    {
        // The POST creates an assignment each time; the PUT is at the member's
        // own address and repeating it changes nothing. Making them overloads
        // would have hidden that — and the analyzer forbids it anyway.
        StubHttpMessageHandler post = new(HttpStatusCode.Created, "{}");
        StubHttpMessageHandler put = new(HttpStatusCode.NoContent, string.Empty);

        await new IamService(Http(post), Options()).Groups
            .AddMemberAsync("g1", new IamModels.AssignmentCreateRequest());
        await new IamService(Http(put), Options()).Groups
            .AssignMemberAsync("g1", "CUSTOMER", "u1");

        Assert.Equal(HttpMethod.Post, post.RequestMethods[0]);
        Assert.Equal("/iam/acme/groups/g1/users", Uri(post));
        Assert.Equal(HttpMethod.Put, put.RequestMethods[0]);
        Assert.Equal("/iam/acme/groups/g1/users/CUSTOMER/u1", Uri(put));
    }

    [Fact]
    public async Task Removing_a_member_carries_no_user_type()
    {
        // Emporix asymmetry: adding needs the type, removing does not. Adding
        // one here would 404.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        IamService iam = new(Http(handler), Options());

        await iam.Groups.RemoveMemberAsync("g1", "u1");

        Assert.Equal("/iam/acme/groups/g1/users/u1", Uri(handler));
    }

    [Fact]
    public async Task The_me_calls_are_their_own_endpoints()
    {
        // Not «users/{id}» with the caller's id — Emporix resolves the token,
        // which is how an application reads its own scopes without knowing its
        // user id.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        IamService iam = new(Http(handler), Options());

        await iam.Users.ListMyScopesAsync(AuthContext.Customer("token"));

        Assert.Equal("/iam/acme/users/me/scopes", Uri(handler));
    }

    // ---------- Configuration ----------

    [Fact]
    public async Task Tenant_and_client_configuration_are_different_scopes()
    {
        // A client value overrides the tenant's for that client only. Writing to
        // the wrong scope changes the setting for everybody.
        StubHttpMessageHandler tenant = new(HttpStatusCode.OK, "[]");
        StubHttpMessageHandler client = new(HttpStatusCode.OK, "[]");

        await new ConfigurationService(Http(tenant), Options()).ListAsync();
        await new ConfigurationService(Http(client), Options()).ForClient("storefront").ListAsync();

        Assert.Equal("/configuration/acme/configurations", Uri(tenant));
        Assert.Equal("/configuration/acme/clients/storefront/configurations", Uri(client));
    }

    // ---------- Session context ----------

    [Fact]
    public async Task Reading_someone_elses_session_is_a_different_endpoint()
    {
        StubHttpMessageHandler mine = new(HttpStatusCode.OK, "{}");
        StubHttpMessageHandler theirs = new(HttpStatusCode.OK, "{}");

        await new SessionContextService(Http(mine), Options())
            .GetMineAsync(AuthContext.Customer("token"));
        await new SessionContextService(Http(theirs), Options()).GetAsync("s1");

        Assert.Equal("/session-context/acme/me/context", Uri(mine));
        Assert.Equal("/session-context/acme/context/s1", Uri(theirs));
    }

    // ---------- Units ----------

    [Fact]
    public async Task Unit_conversion_is_a_put_that_may_be_retried()
    {
        // Emporix models the conversion as a command, so the verb looks like a
        // write. It computes and changes nothing.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        UnitService units = new(Http(handler), Options());

        await units.ConvertAsync(new UnitHandlingServiceModels.ConversionPayload());

        Assert.Equal(HttpMethod.Put, handler.RequestMethods[0]);
        Assert.Equal("/unit-handling/acme/units/convert-unit-commands", Uri(handler));
        Assert.True(IsRepeatable(handler));
    }

    // ---------- Schemas and custom entities ----------

    [Fact]
    public async Task Custom_instances_hang_off_their_type()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        SchemaService schemas = new(Http(handler), Options());

        await schemas.InstancesOf("recipe").GetAsync("r1");

        Assert.Equal("/schema/acme/custom-entities/recipe/instances/r1", Uri(handler));
    }

    [Fact]
    public async Task Searching_custom_instances_only_reads()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        SchemaService schemas = new(Http(handler), Options());

        await schemas.InstancesOf("recipe").SearchAsync(JsonDocument.Parse("{}").RootElement);

        Assert.Equal("/schema/acme/custom-entities/recipe/instances/search", Uri(handler));
        Assert.True(IsRepeatable(handler));
    }

    [Fact]
    public async Task Exporting_reads_and_importing_writes()
    {
        // The two look alike and are not: an export can be retried, an import
        // writes the data a second time.
        StubHttpMessageHandler export = new(HttpStatusCode.OK, "{}");
        StubHttpMessageHandler import = new(HttpStatusCode.OK, "{}");

        await new SchemaService(Http(export), Options())
            .ExportAsync(new SchemaModels.ExportImportRequest());
        await new SchemaService(Http(import), Options())
            .ImportAsync(new SchemaModels.ExportImportRequest());

        Assert.True(IsRepeatable(export));
        Assert.False(IsRepeatable(import));
    }

    // ---------- Sites, vendors, currencies, countries ----------

    [Fact]
    public async Task The_short_site_list_is_its_own_endpoint()
    {
        // Codes and names only. Fetching the full list to render a switcher
        // pulls every site's whole configuration.
        StubHttpMessageHandler full = new(HttpStatusCode.OK, "[]");
        StubHttpMessageHandler brief = new(HttpStatusCode.OK, "[]");

        await new SiteService(Http(full), Options()).ListAsync();
        await new SiteService(Http(brief), Options()).ListShortAsync();

        Assert.Equal("/site/acme/sites", Uri(full));
        Assert.Equal("/site/acme/siteslist", Uri(brief));
    }

    [Fact]
    public async Task Site_mixins_hang_off_the_site()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        SiteService sites = new(Http(handler), Options());

        await sites.MixinsOf("main").GetAsync("theme");

        Assert.Equal("/site/acme/sites/main/mixins/theme", Uri(handler));
    }

    [Fact]
    public async Task Vendor_locations_are_tenant_level_not_nested()
    {
        // Not /vendors/{id}/locations — filtering by vendor goes through the
        // query, and assuming otherwise is a 404.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        VendorService vendors = new(Http(handler), Options());

        await vendors.ListLocationsAsync();

        Assert.StartsWith("/vendor/acme/locations", Uri(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_country_can_only_be_switched_not_created()
    {
        // The list is Emporix's; a tenant chooses from it. There is no create,
        // and that is the API rather than an omission here.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        CountryService countries = new(Http(handler), Options());

        await countries.UpdateAsync("CH", new CountryServiceModels.CountryUpdate());

        Assert.Equal(HttpMethod.Patch, handler.RequestMethods[0]);
        Assert.Equal("/country/acme/countries/CH", Uri(handler));
    }

    [Fact]
    public async Task Reads_that_a_storefront_makes_default_to_anonymous()
    {
        // Sites, currencies, countries and units are read by a browser before
        // anyone signs in. Defaulting them to a service token would need
        // credentials a storefront does not have.
        foreach (Func<EmporixHttpClient, IOptions<EmporixOptions>, Task> call in new Func<EmporixHttpClient, IOptions<EmporixOptions>, Task>[]
        {
            (h, o) => new SiteService(h, o).ListAsync(),
            (h, o) => new CurrencyService(h, o).ListAsync(),
            (h, o) => new CountryService(h, o).ListAsync(),
            (h, o) => new UnitService(h, o).ListAsync(),
        })
        {
            StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
            await call(Http(handler), Options());

            handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
            Assert.Equal(AuthKind.Anonymous, auth.Kind);
        }
    }

    // ---------- Shared ----------

    [Fact]
    public async Task Empty_identifiers_are_rejected_everywhere()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new IamService(Http(handler), Options()).GetScopeAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new SchemaService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new SiteService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new VendorService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new CurrencyService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new CountryService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new WebhookService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new UnitService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new SequentialIdService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new ConfigurationService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new SessionContextService(Http(handler), Options()).GetAsync(""));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Every_new_service_hangs_off_the_client()
    {
        using EmporixClient client = new(new EmporixOptions
        {
            Tenant = "acme",
            Credentials = { Storefront = new EmporixStorefrontCredentials { ClientId = "x" } },
        });

        Assert.NotNull(client.Iam);
        Assert.NotNull(client.Schemas);
        Assert.NotNull(client.Sites);
        Assert.NotNull(client.Vendors);
        Assert.NotNull(client.Currencies);
        Assert.NotNull(client.Countries);
        Assert.NotNull(client.Webhooks);
        Assert.NotNull(client.Units);
        Assert.NotNull(client.SequentialIds);
        Assert.NotNull(client.Configuration);
        Assert.NotNull(client.SessionContext);
    }
}
