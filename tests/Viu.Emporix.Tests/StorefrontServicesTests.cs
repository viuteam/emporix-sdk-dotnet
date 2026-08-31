using System.Net;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class StorefrontServicesTests
{
    private static readonly AuthContext Shopper = AuthContext.Anonymous();
    private static readonly AuthContext Customer = AuthContext.Customer("token");

    private static IOptions<EmporixOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EmporixOptions { Tenant = "acme" });

    private static EmporixHttpClient Http(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Options());

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    // ---------- Prices ----------

    [Fact]
    public async Task Price_matching_posts_the_items_and_is_repeatable()
    {
        // The item list does not fit in an address, but the call only reads.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"productId":"p1"}]""");
        PriceService prices = new(Http(handler), Options());

        IReadOnlyList<PriceModels.Match> matches = await prices.MatchByContextAsync(
            new PriceModels.MatchByContext(),
            Shopper);

        Assert.Single(matches);
        Assert.Equal("/price/acme/match-prices-by-context", Uri(handler));
        Assert.True(handler.LastRequest!.Options.TryGetValue(
            EmporixRequestOptions.Idempotent,
            out bool idempotent));
        Assert.True(idempotent);
    }

    [Fact]
    public async Task Price_matching_refuses_a_service_token()
    {
        // With a service token Emporix answers an empty list and no error, which
        // reads like «no prices configured» — the worst kind of failure.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        PriceService prices = new(Http(handler), Options());

        EmporixConfigurationException exception =
            await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
                await prices.MatchByContextAsync(new PriceModels.MatchByContext(), AuthContext.Service()));

        Assert.Contains("empty list", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Price_matching_splits_long_lists()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"productId":"p1"}]""");
        PriceService prices = new(Http(handler), Options());

        PriceModels.Items[] items = [.. Enumerable.Range(1, 120).Select(_ => new PriceModels.Items())];

        await prices.MatchByContextChunkedAsync(items, new PriceMatchOptions { ChunkSize = 50 }, Shopper);

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Matching_nothing_makes_no_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        PriceService prices = new(Http(handler), Options());

        Assert.Empty(await prices.MatchByContextChunkedAsync([], auth: Shopper));
        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Availability ----------

    [Fact]
    public async Task Availability_is_addressed_by_product_and_site()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"productId":"p1","available":true}""");
        AvailabilityService availability = new(Http(handler), Options());

        AvailabilityModels.Availability? result = await availability.GetAsync("p1", "main");

        Assert.True(result?.Available);
        Assert.Equal("/availability/acme/availability/p1/main", Uri(handler));
    }

    [Fact]
    public async Task A_missing_stock_record_raises_by_default()
    {
        // Assuming availability is the more expensive mistake, so it is opt-in.
        StubHttpMessageHandler handler = new(HttpStatusCode.NotFound, """{"message":"none"}""");
        AvailabilityService availability = new(Http(handler), Options());

        await Assert.ThrowsAsync<EmporixNotFoundException>(async () =>
            await availability.GetAsync("p1", "main"));
    }

    [Fact]
    public async Task A_missing_stock_record_can_count_as_available()
    {
        // For a catalog where most products are always in stock, a missing
        // record means «available», not «unknown».
        StubHttpMessageHandler handler = new(HttpStatusCode.NotFound, """{"message":"none"}""");
        AvailabilityService availability = new(Http(handler), Options());

        AvailabilityModels.Availability? result =
            await availability.GetAsync("p1", "main", treatMissingAsAvailable: true);

        Assert.True(result?.Available);
        Assert.Equal("p1", result?.ProductId);
        Assert.Equal("main", result?.Site);
    }

    [Fact]
    public async Task Other_failures_are_never_treated_as_available()
    {
        // Only 404 is interpreted. A 500 must not turn into «in stock».
        StubHttpMessageHandler handler = new(HttpStatusCode.InternalServerError, """{"message":"boom"}""");
        AvailabilityService availability = new(Http(handler), Options());

        await Assert.ThrowsAsync<EmporixServerException>(async () =>
            await availability.GetAsync("p1", "main", treatMissingAsAvailable: true));
    }

    // ---------- Checkout ----------

    [Fact]
    public async Task Placing_an_order_is_never_marked_repeatable()
    {
        // The whole point of the idempotency gate. A repeated checkout is a
        // duplicate order.
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, """{"orderId":"o1"}""");
        CheckoutService checkout = new(Http(handler), Options());

        await checkout.PlaceOrderAsync(new CheckoutModels.RequestCheckout(), Shopper);

        Assert.False(handler.LastRequest!.Options.TryGetValue(
            EmporixRequestOptions.Idempotent,
            out _));
        Assert.Equal("/checkout/acme/order", Uri(handler));
    }

    [Fact]
    public async Task Checkout_forwards_the_saas_token_header()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, "{}");
        CheckoutService checkout = new(Http(handler), Options());

        await checkout.PlaceOrderAsync(new CheckoutModels.RequestCheckout(), Shopper, "saas-1");

        Assert.Equal("saas-1", handler.LastHeader("saas-token"));
    }

    [Fact]
    public async Task Checkout_omits_the_saas_header_when_absent()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, "{}");
        CheckoutService checkout = new(Http(handler), Options());

        await checkout.PlaceOrderAsync(new CheckoutModels.RequestCheckout(), Shopper);

        Assert.Null(handler.LastHeader("saas-token"));
    }

    [Fact]
    public async Task Checkout_refuses_a_service_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, "{}");
        CheckoutService checkout = new(Http(handler), Options());

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await checkout.PlaceOrderAsync(new CheckoutModels.RequestCheckout(), AuthContext.Service()));

        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Orders ----------

    [Fact]
    public async Task Own_orders_follow_from_the_token()
    {
        // There is no customer parameter, and there must not be one.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"id":"o1"}]""");
        OrderService orders = new(Http(handler), Options());

        PaginatedItems<OrderV2Models.Order> page =
            await orders.ListMineAsync(Customer);

        Assert.Single(page.Items);
        Assert.DoesNotContain("customer", Uri(handler), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Own_orders_refuse_a_service_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        OrderService orders = new(Http(handler), Options());

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await orders.ListMineAsync(AuthContext.Service()));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_status_change_posts_a_transition()
    {
        // Emporix has no «set status» endpoint. A status change is a transition
        // the server may or may not allow from where the order stands.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        OrderService orders = new(Http(handler), Options());

        await orders.ChangeStatusAsync("o1", OrderV2Models.OrderStatus.SHIPPED, Customer);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/order-v2/acme/orders/o1/transitions", Uri(handler));
        Assert.Contains("SHIPPED", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_is_the_declined_transition()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        OrderService orders = new(Http(handler), Options());

        await orders.CancelAsync("o1", Customer);

        Assert.Contains("DECLINED", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Storefront_and_administrative_orders_are_separate_collections()
    {
        // /orders is what a shopper sees; /salesorders is the back office. Using
        // one for the other is a 404 at best and a data leak at worst.
        StubHttpMessageHandler shopper = new(HttpStatusCode.OK, """[{"id":"o1"}]""");
        StubHttpMessageHandler admin = new(HttpStatusCode.OK, """[{"id":"o1"}]""");

        await new OrderService(Http(shopper), Options()).ListMineAsync(Customer);
        await new SalesOrderService(Http(admin), Options()).ListAsync();

        Assert.StartsWith("/order-v2/acme/orders", Uri(shopper), StringComparison.Ordinal);
        Assert.StartsWith("/order-v2/acme/salesorders", Uri(admin), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_legal_entity_has_its_own_order_list()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"id":"o1"}]""");
        OrderService orders = new(Http(handler), Options());

        await orders.ListForLegalEntityAsync("le-1", Customer);

        Assert.StartsWith("/order-v2/acme/legal-entity-orders/le-1", Uri(handler), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Creating_a_sales_order_is_never_repeatable()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, "{}");
        SalesOrderService orders = new(Http(handler), Options());

        await orders.CreateAsync(new OrderV2Models.SalesOrderCreationDto());

        Assert.False(handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out _));
    }

    [Fact]
    public async Task Calculating_only_reads_and_is_repeatable()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        SalesOrderService orders = new(Http(handler), Options());

        await orders.CalculateAsync("o1", new OrderV2Models.OrderCalculationDto());

        Assert.True(handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out bool idempotent));
        Assert.True(idempotent);
        Assert.Equal("/order-v2/acme/salesorders/o1/calculations", Uri(handler));
    }

    // ---------- Media ----------

    [Fact]
    public async Task Media_calls_default_to_a_service_token()
    {
        // The media scopes are server-side only; storefronts read images through
        // the product they belong to.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"a1"}""");
        MediaService media = new(Http(handler), Options());

        await media.GetAsync("a1");

        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Service, auth.Kind);
        Assert.Equal("/media/acme/assets/a1", Uri(handler));
    }

    [Fact]
    public async Task Downloading_returns_the_response_unread()
    {
        // An asset can be large; buffering it is the caller's decision.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "binary-ish");
        MediaService media = new(Http(handler), Options());

        using HttpResponseMessage response = await media.DownloadAsync("a1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/media/acme/assets/a1/download", Uri(handler));
    }

    [Fact]
    public async Task Downloading_leaves_error_statuses_to_the_caller()
    {
        // A public asset answers with a redirect rather than bytes, so the raw
        // path deliberately does not translate statuses.
        StubHttpMessageHandler handler = new(HttpStatusCode.Found, string.Empty);
        MediaService media = new(Http(handler), Options());

        using HttpResponseMessage response = await media.DownloadAsync("a1");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    // ---------- Shared ----------

    [Fact]
    public async Task Empty_identifiers_are_rejected()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new AvailabilityService(Http(handler), Options()).GetAsync("", "main"));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new OrderService(Http(handler), Options()).GetAsync(" ", Customer));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new MediaService(Http(handler), Options()).GetAsync(""));

        Assert.Equal(0, handler.CallCount);
    }
}
