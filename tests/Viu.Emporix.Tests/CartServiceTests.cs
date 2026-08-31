using System.Net;
using Microsoft.Extensions.Options;
using Viu.Emporix.CartModels;

namespace Viu.Emporix.Tests;

public class CartServiceTests
{
    private static readonly AuthContext Shopper = AuthContext.Anonymous();

    private static CartService Create(StubHttpMessageHandler handler)
    {
        IOptions<EmporixOptions> options = Options.Create(new EmporixOptions { Tenant = "acme" });

        return new CartService(new EmporixHttpClient(new HttpClient(handler), options), options);
    }

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    // ---------- Who a cart may belong to ----------

    [Theory]
    [InlineData(AuthKind.Anonymous)]
    [InlineData(AuthKind.Customer)]
    public async Task A_cart_accepts_shopper_contexts(AuthKind kind)
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        AuthContext auth = kind == AuthKind.Anonymous
            ? AuthContext.Anonymous()
            : AuthContext.Customer("token");

        await carts.GetAsync("c1", auth);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task A_service_token_is_refused_before_any_request()
    {
        // A cart belongs to a person. Defaulting silently would attach it to the
        // wrong party, and that only shows up once somebody's cart is empty.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        EmporixConfigurationException exception =
            await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
                await carts.GetAsync("c1", AuthContext.Service()));

        Assert.Contains("belongs to a person", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task An_unset_context_is_refused_too()
    {
        // Unlike the catalog services there is no sensible default here.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await carts.GetAsync("c1", default));

        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Finding the current cart ----------

    [Fact]
    public async Task No_current_cart_yields_null_rather_than_an_error()
    {
        // «No cart yet» is the normal state on a first visit.
        StubHttpMessageHandler handler = new(HttpStatusCode.NotFound, """{"message":"no cart"}""");
        CartService carts = Create(handler);

        Cart? cart = await carts.GetCurrentAsync(new CurrentCartQuery { SiteCode = "main" }, Shopper);

        Assert.Null(cart);
    }

    [Fact]
    public async Task Other_failures_while_finding_a_cart_still_propagate()
    {
        // Only 404 is swallowed. A 500 must not look like «no cart».
        StubHttpMessageHandler handler = new(HttpStatusCode.InternalServerError, """{"message":"boom"}""");
        CartService carts = Create(handler);

        await Assert.ThrowsAsync<EmporixServerException>(async () =>
            await carts.GetCurrentAsync(new CurrentCartQuery { SiteCode = "main" }, Shopper));
    }

    [Fact]
    public async Task The_current_cart_query_carries_every_criterion()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        await carts.GetCurrentAsync(
            new CurrentCartQuery
            {
                SiteCode = "main",
                Type = "wishlist",
                LegalEntityId = "le-1",
                Create = true,
            },
            Shopper);

        string uri = Uri(handler);
        Assert.Contains("siteCode=main", uri, StringComparison.Ordinal);
        Assert.Contains("type=wishlist", uri, StringComparison.Ordinal);
        Assert.Contains("legalEntityId=le-1", uri, StringComparison.Ordinal);
        Assert.Contains("create=true", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Optional_criteria_are_omitted_when_unset()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        await carts.GetCurrentAsync(new CurrentCartQuery { SiteCode = "main" }, Shopper);

        Assert.Equal("/cart/acme/carts?siteCode=main", Uri(handler));
    }

    // ---------- Items ----------

    [Fact]
    public async Task Adding_an_item_posts_to_the_cart_items_path()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, """{"id":"i1"}""");
        CartService carts = Create(handler);

        await carts.AddItemAsync(
            "c1",
            new CartItemRequest { ItemYrn = ProductYrn.Create("acme", "p1"), Quantity = 2 },
            Shopper);

        Assert.Equal("/cart/acme/carts/c1/items", Uri(handler));
        Assert.Contains(
            "urn:yaas:hybris:product:product:acme;p1",
            handler.RequestBodies[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_and_removing_use_different_paths()
    {
        StubHttpMessageHandler clear = new(HttpStatusCode.NoContent, string.Empty);
        await Create(clear).ClearAsync("c1", Shopper);
        Assert.Equal("/cart/acme/carts/c1/items", Uri(clear));

        StubHttpMessageHandler remove = new(HttpStatusCode.NoContent, string.Empty);
        await Create(remove).RemoveItemAsync("c1", "i1", Shopper);
        Assert.Equal("/cart/acme/carts/c1/items/i1", Uri(remove));
    }

    [Fact]
    public async Task Listing_items_returns_an_empty_list_rather_than_null()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, string.Empty);
        CartService carts = Create(handler);

        Assert.Empty(await carts.ListItemsAsync("c1", Shopper));
    }

    // ---------- Repricing ----------

    [Fact]
    public async Task Refreshing_reprices_and_then_returns_the_updated_cart()
    {
        // Emporix answers the refresh without a body, so the cart is fetched
        // afterwards. Returning the stale cart would be the more surprising outcome.
        StubHttpMessageHandler handler = new((request, _) =>
            request.Method == HttpMethod.Put
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"id":"c1","currency":"CHF"}"""));
        CartService carts = Create(handler);

        Cart? cart = await carts.RefreshAsync("c1", Shopper);

        Assert.Equal(2, handler.CallCount);
        Assert.Equal("/cart/acme/carts/c1/refresh", Uri(handler));
        Assert.Equal("/cart/acme/carts/c1", Uri(handler, 1));
        Assert.NotNull(cart);
    }

    // ---------- Coupons ----------

    [Fact]
    public async Task A_coupon_code_is_escaped_into_the_path()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, string.Empty);
        CartService carts = Create(handler);

        await carts.ApplyCouponAsync("c1", "SUMMER 20%", Shopper);

        Assert.Equal("/cart/acme/carts/c1/coupons/SUMMER%2020%25", Uri(handler));
    }

    [Fact]
    public async Task A_rejected_coupon_surfaces_as_a_validation_error()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.BadRequest,
            """{"message":"Coupon expired","errorCode":"COUPON_EXPIRED"}""");
        CartService carts = Create(handler);

        EmporixValidationException exception =
            await Assert.ThrowsAsync<EmporixValidationException>(async () =>
                await carts.ApplyCouponAsync("c1", "OLD", Shopper));

        Assert.Equal("COUPON_EXPIRED", exception.ErrorCode);
    }

    // ---------- Arguments ----------

    [Fact]
    public async Task Empty_identifiers_are_rejected()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        CartService carts = Create(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () => await carts.GetAsync(" ", Shopper));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await carts.RemoveItemAsync("c1", "", Shopper));
        Assert.Equal(0, handler.CallCount);
    }
    // ---------- Addresses, batches and merging ----------

    [Fact]
    public async Task Setting_the_shipping_address_keeps_the_billing_one()
    {
        // Emporix replaces the whole address array on write. Sending only the
        // new one would silently delete the invoice address.
        const string CartWithBilling = """
            {"id":"c1","addresses":[{"type":"BILLING","street":"Rennweg","city":"Zurich"}]}
            """;

        StubHttpMessageHandler handler = new((_, call) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(call == 2 ? "" : CartWithBilling),
            });

        CartService carts = Create(handler);

        await carts.SetShippingAddressAsync(
            "c1",
            new AddressRequest { Street = "Bahnhofstrasse", City = "Zurich" },
            Shopper);

        // read, write, read back
        Assert.Equal(3, handler.CallCount);
        string written = handler.RequestBodies.Single(b => b.Length > 0);
        Assert.Contains("Rennweg", written, StringComparison.Ordinal);
        Assert.Contains("Bahnhofstrasse", written, StringComparison.Ordinal);
        Assert.Contains("BILLING", written, StringComparison.Ordinal);
        Assert.Contains("SHIPPING", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setting_both_addresses_needs_no_prior_read()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        await carts.SetAddressesAsync(
            "c1",
            new AddressRequest { Street = "Bahnhofstrasse" },
            new AddressRequest { Street = "Rennweg" },
            Shopper);

        // write, read back — no read first
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(HttpMethod.Put, handler.RequestMethods[0]);
    }

    [Fact]
    public async Task Merging_carts_refuses_an_anonymous_token()
    {
        // The target cart belongs to the signed-in customer. An anonymous token
        // cannot own it, and Emporix checks.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await carts.MergeAsync("c1", ["anon-1"], Shopper));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Merging_carts_sends_the_anonymous_ids()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        await carts.MergeAsync("c1", ["anon-1", "anon-2"], AuthContext.Customer("token"));

        Assert.Equal("/cart/acme/carts/c1/merge", Uri(handler));
        Assert.Contains("anon-2", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_the_site_reads_the_cart_back()
    {
        // The server re-matches prices, so the cart you held is stale.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        await carts.ChangeSiteAsync("c1", "main", Shopper);

        Assert.Equal(2, handler.CallCount);
        Assert.Equal("/cart/acme/carts/c1/changeSite", Uri(handler));
        Assert.Equal("/cart/acme/carts/c1", Uri(handler, 1));
    }

    [Fact]
    public async Task Changing_the_currency_sends_the_code()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CartService carts = Create(handler);

        await carts.ChangeCurrencyAsync("c1", "CHF", Shopper);

        Assert.Equal("/cart/acme/carts/c1/changeCurrency", Uri(handler));
        Assert.Contains("CHF", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adding_items_in_bulk_reports_a_status_per_item()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """[{"index":0,"status":201},{"index":1,"status":409}]""");

        CartService carts = Create(handler);

        IReadOnlyList<SingleBatchResponse> result = await carts.AddItemsAsync(
            "c1",
            [new CartItemRequest(), new CartItemRequest()],
            Shopper);

        Assert.Equal("/cart/acme/carts/c1/itemsBatch", Uri(handler));
        Assert.Equal(2, result.Count);
        Assert.Equal(409, result[1].Status);
    }

    [Fact]
    public async Task Searching_carts_only_reads_and_is_repeatable()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"id":"c1"}]""");
        CartService carts = Create(handler);

        await carts.SearchAsync("status:ACTIVE", AuthContext.Service());

        Assert.Equal(HttpMethod.Post, handler.RequestMethods[0]);
        Assert.StartsWith("/cart/acme/carts/search", Uri(handler), StringComparison.Ordinal);
        Assert.True(handler.LastRequest!.Options.TryGetValue(
            EmporixRequestOptions.Idempotent,
            out bool idempotent));
        Assert.True(idempotent);
    }

    [Fact]
    public async Task Removing_a_discount_rejects_a_negative_index()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        CartService carts = Create(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await carts.RemoveDiscountAsync("c1", -1, Shopper));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Delivery_restrictions_are_read_from_the_cart()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """{"leadTime":2,"nonDelivery":["SUNDAY"]}""");

        CartService carts = Create(handler);

        CartDTRestrictions? restrictions =
            await carts.GetDeliveryRestrictionsAsync("c1", Shopper);

        Assert.Equal(2, restrictions?.LeadTime);
        Assert.Equal("/cart/acme/carts/c1/dtRestrictions", Uri(handler));
    }
}

public class ProductYrnTests
{
    [Fact]
    public void Builds_the_reference_emporix_expects()
    {
        // Anything else is rejected with «Given yrn does not match yaas urn scheme».
        Assert.Equal(
            "urn:yaas:hybris:product:product:acme;p1",
            ProductYrn.Create("acme", "p1"));
    }

    [Fact]
    public void Reads_the_product_id_back_out()
    {
        Assert.Equal("p1", ProductYrn.GetProductId(ProductYrn.Create("acme", "p1")));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("p1", "")]
    public void A_reference_without_an_id_segment_yields_an_empty_string(string? yrn, string expected)
    {
        // Approval resource items carry the bare product id with no wrapper. The
        // empty result is the signal to fall back to the neighbouring itemId.
        Assert.Equal(expected, ProductYrn.GetProductId(yrn));
    }

    [Fact]
    public void Empty_arguments_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ProductYrn.Create("", "p1"));
        Assert.Throws<ArgumentException>(() => ProductYrn.Create("acme", " "));
    }
}
