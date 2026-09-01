using System.Net;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

/// <summary>
/// The B2B services: legal entities, contacts, locations, customer
/// administration, approvals, quotes and segments.
/// </summary>
/// <remarks>
/// Weighted towards what a defect would hide: which of two similar paths a call
/// uses, which calls may be repeated, and the places where an optional argument
/// changes the address rather than the body.
/// </remarks>
public class B2BWaveTests
{
    private static IOptions<EmporixOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EmporixOptions { Tenant = "acme" });

    private static EmporixHttpClient Http(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Options());

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    private static bool IsRepeatable(StubHttpMessageHandler handler)
        => handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out bool v) && v;

    // ---------- Legal entities ----------

    [Fact]
    public async Task A_legal_entity_search_only_reads()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        LegalEntityService entities = new(Http(handler), Options());

        await entities.SearchAsync("name:Acme");

        Assert.Equal(HttpMethod.Post, handler.RequestMethods[0]);
        Assert.StartsWith(
            "/customer-management/acme/legal-entities/search",
            Uri(handler),
            StringComparison.Ordinal);
        Assert.True(IsRepeatable(handler));
    }

    [Fact]
    public async Task The_parent_hierarchy_hangs_off_the_entity()
    {
        // Terms can be set on a parent and inherited, so «who decides» is a
        // question about this path rather than about the entity in hand.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        LegalEntityService entities = new(Http(handler), Options());

        await entities.GetParentHierarchyAsync("le-1");

        Assert.Equal(
            "/customer-management/acme/legal-entities/le-1/parent-hierarchy",
            Uri(handler));
    }

    [Fact]
    public async Task Contacts_and_locations_are_separate_collections()
    {
        // Both come out of one specification, and both are tenant-level rather
        // than nested under the entity — using one path for the other is a 404.
        StubHttpMessageHandler contacts = new(HttpStatusCode.OK, "[]");
        StubHttpMessageHandler locations = new(HttpStatusCode.OK, "[]");

        await new ContactAssignmentService(Http(contacts), Options()).ListAsync();
        await new LocationService(Http(locations), Options()).ListAsync();

        Assert.StartsWith(
            "/customer-management/acme/contact-assignments",
            Uri(contacts),
            StringComparison.Ordinal);
        Assert.StartsWith(
            "/customer-management/acme/locations",
            Uri(locations),
            StringComparison.Ordinal);
    }

    // ---------- Approvals ----------

    [Fact]
    public async Task Asking_whether_an_approval_is_needed_only_reads()
    {
        // The call a B2B checkout makes before offering «place order» or
        // «request approval». It must not create anything.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"permitted":true}""");
        ApprovalService approvals = new(Http(handler), Options());

        ApprovalServiceModels.ApprovalPermittedResponse? permitted = await approvals.IsPermittedAsync(
            new ApprovalServiceModels.ApprovalPermittedRequest());

        Assert.True(permitted?.Permitted);
        Assert.Equal("/approval/acme/approval/permitted", Uri(handler));
        Assert.True(IsRepeatable(handler));
    }

    [Fact]
    public async Task Raising_an_approval_is_never_repeatable()
    {
        // A retry puts the same cart in the approver's queue twice.
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, "{}");
        ApprovalService approvals = new(Http(handler), Options());

        await approvals.CreateForCartAsync(new ApprovalServiceModels.CreateCartApprovalRequest());

        Assert.Equal("/approval/acme/approvals", Uri(handler));
        Assert.False(IsRepeatable(handler));
    }

    [Fact]
    public async Task Finding_approvers_reads_over_a_post()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ApprovalService approvals = new(Http(handler), Options());

        await approvals.FindApproversAsync(new ApprovalServiceModels.SearchUsersRequest());

        Assert.Equal("/approval/acme/search/users", Uri(handler));
        Assert.True(IsRepeatable(handler));
    }

    // ---------- Quotes ----------

    [Fact]
    public async Task Requesting_a_quote_is_never_repeatable()
    {
        // A retried request has a seller pricing the same enquiry twice.
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, "{}");
        QuoteService quotes = new(Http(handler), Options());

        await quotes.CreateAsync(new QuoteModels.QuoteCreateRequest());

        Assert.False(IsRepeatable(handler));
    }

    [Fact]
    public async Task Rendering_a_quote_returns_the_response_unread()
    {
        // A PDF is not JSON, so it comes back raw and the caller decides whether
        // to stream or buffer it.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "%PDF-1.7");
        QuoteService quotes = new(Http(handler), Options());

        using HttpResponseMessage response = await quotes.RenderPdfAsync("q1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/quote/acme/quotes/q1/pdf", Uri(handler));
        Assert.True(IsRepeatable(handler));
    }

    [Fact]
    public async Task Quote_reasons_are_their_own_collection()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        QuoteService quotes = new(Http(handler), Options());

        await quotes.Reasons.ListAsync();

        Assert.Equal("/quote/acme/quote-reasons", Uri(handler));
    }

    // ---------- Segments ----------

    [Fact]
    public async Task Matching_segments_only_reads()
    {
        // The storefront read that decides which prices and catalogue a visitor
        // sees. Creating anything here would be a bug with visible consequences.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        SegmentService segments = new(Http(handler), Options());

        await segments.MatchAsync(new CustomerSegmentModels.Match());

        Assert.Equal("/customer-segment/acme/segments/match", Uri(handler));
        Assert.True(IsRepeatable(handler));
    }

    [Fact]
    public async Task A_membership_scoped_to_a_legal_entity_changes_the_address()
    {
        // The same person can be in a segment when buying for one company and
        // not for another, and Emporix expresses that in the path rather than
        // in the body. Sending the entity in the body would silently apply the
        // membership everywhere.
        StubHttpMessageHandler plain = new(HttpStatusCode.OK, "{}");
        StubHttpMessageHandler scoped = new(HttpStatusCode.OK, "{}");

        await new SegmentService(Http(plain), Options()).Customers("s1").GetAsync("c1");
        await new SegmentService(Http(scoped), Options()).Customers("s1").GetAsync("c1", "le-1");

        Assert.Equal("/customer-segment/acme/segments/s1/customers/c1", Uri(plain));
        Assert.Equal("/customer-segment/acme/segments/s1/customers/c1/le-1", Uri(scoped));
    }

    [Fact]
    public async Task Segment_items_are_addressed_by_kind_and_id()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        SegmentService segments = new(Http(handler), Options());

        await segments.Items("s1").GetAsync("PRODUCT", "p1");

        Assert.Equal("/customer-segment/acme/segments/s1/items/PRODUCT/p1", Uri(handler));
    }

    [Fact]
    public async Task Bulk_calls_with_nothing_to_do_make_no_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        SegmentService segments = new(Http(handler), Options());

        Assert.Empty(await segments.CreateManyAsync([]));
        Assert.Empty(await segments.DeleteManyAsync([]));
        Assert.Empty(await segments.Customers("s1").UpsertManyAsync([]));
        Assert.Empty(await segments.Items("s1").UpsertManyAsync("PRODUCT", []));

        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Customer administration ----------

    [Fact]
    public async Task The_seller_view_addresses_a_customer_by_number()
    {
        // Never «me» — that is the storefront service, and confusing the two
        // means a back office reading its own service account.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        CustomerAdminService customers = new(Http(handler), Options());

        await customers.GetAsync("C-1");

        Assert.Equal("/customer/acme/customers/C-1", Uri(handler));
    }

    [Fact]
    public async Task The_seller_address_endpoint_routes_put_unlike_the_customer_one()
    {
        // The two services are not symmetric here: a customer's own address
        // takes PATCH only, the seller's takes both. Assuming symmetry is how
        // the earlier PUT-instead-of-PATCH defect happened.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        CustomerAdminService customers = new(Http(handler), Options());

        await customers.AddressesOf("C-1").ReplaceAsync("a1", new CustomerServiceModels.AddressUpdateDto());

        Assert.Equal(HttpMethod.Put, handler.RequestMethods[0]);
        Assert.Equal("/customer/acme/customers/C-1/addresses/a1", Uri(handler));
    }

    [Fact]
    public async Task Importing_nothing_makes_no_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        CustomerAdminService customers = new(Http(handler), Options());

        await customers.ImportAsync([]);

        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Shared ----------

    [Fact]
    public async Task Empty_identifiers_are_rejected_everywhere()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new LegalEntityService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new ContactAssignmentService(Http(handler), Options()).GetAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new LocationService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new ApprovalService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new QuoteService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new SegmentService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new CustomerAdminService(Http(handler), Options()).GetAsync(""));

        Assert.Throws<ArgumentException>(() =>
            new SegmentService(Http(handler), Options()).Customers(""));
        Assert.Throws<ArgumentException>(() =>
            new CustomerAdminService(Http(handler), Options()).AddressesOf(""));

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

        Assert.NotNull(client.LegalEntities);
        Assert.NotNull(client.ContactAssignments);
        Assert.NotNull(client.Locations);
        Assert.NotNull(client.CustomerAdmin);
        Assert.NotNull(client.Approvals);
        Assert.NotNull(client.Quotes);
        Assert.NotNull(client.Segments);
    }
}
