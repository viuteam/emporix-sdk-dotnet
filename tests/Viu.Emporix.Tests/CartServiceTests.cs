using System.Net;
using System.Text.Json;
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
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, """{"itemId":"i1"}""");
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
    public async Task Adding_an_item_returns_the_id_of_the_new_item()
    {
        // The 201 as tenant viu answered it for a cart's first item: "0", not the
        // object id the specification's example shows. Read as the priced item,
        // that id fell among the unknown properties and Id stayed null; a stub
        // answering {"id":…} agreed, and only the live smoke test noticed.
        StubHttpMessageHandler handler = new(
            HttpStatusCode.Created,
            """{"itemId":"0","yrn":"urn:yaas:hybris:cart:cart-item:acme;c1;0"}""");
        CartService carts = Create(handler);

        CreatedCartItem? added = await carts.AddItemAsync(
            "c1",
            new CartItemRequest { ItemYrn = ProductYrn.Create("acme", "p1"), Quantity = 1 },
            Shopper);

        Assert.Equal("0", added?.ItemId);
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
    public async Task A_coupon_is_applied_as_a_discount()
    {
        // Emporix has no coupon path. A coupon is a discount carrying a code,
        // and the code travels in the body rather than the address.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, string.Empty);
        CartService carts = Create(handler);

        await carts.ApplyCouponAsync("c1", "SUMMER 20%", Shopper);

        Assert.Equal("/cart/acme/carts/c1/discounts", Uri(handler));
        Assert.Contains("SUMMER 20%", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_coupon_is_removed_by_query_parameter()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, string.Empty);
        CartService carts = Create(handler);

        await carts.RemoveCouponAsync("c1", "SUMMER 20%", Shopper);

        Assert.Equal(HttpMethod.Delete, handler.RequestMethods[0]);
        Assert.Equal("/cart/acme/carts/c1/discounts?codes=SUMMER%2020%25", Uri(handler));
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

    // ---------- Command chains: the commands ----------

    /// <summary>One command as it goes over the wire, through the cart context.</summary>
    private static JsonElement Wire(CartCommand command)
    {
        string json = JsonSerializer.Serialize(
            new CartCommandChain { Commands = [command] },
            CartJsonContext.Default.CartCommandChain);

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("commands")[0].Clone();
    }

    private static List<string> Names(JsonElement element)
        => [.. element.EnumerateObject().Select(property => property.Name)];

    [Fact]
    public void Each_command_factory_names_its_command_type()
    {
        (CartCommand Command, string Type)[] cases =
        [
            (CartCommand.AddCartItem(new CartItemRequest()), "AddCartItem"),
            (CartCommand.UpdateCartItem("i1", new UpdateCartItem()), "UpdateCartItem"),
            (CartCommand.DeleteCartItem("i1"), "DeleteCartItem"),
            (CartCommand.DeleteCartItems(), "DeleteCartItems"),
            (CartCommand.GetCart(), "GetCart"),
            (CartCommand.AddCartItemsBatch([new CartItemRequest()]), "AddCartItemsBatch"),
            (CartCommand.UpdateCartItemsBatch([new CartItemRequest()]), "UpdateCartItemsBatch"),
            (CartCommand.UpdateCart(new UpdateCart()), "UpdateCart"),
            (CartCommand.ApplyCartDiscount(new Discount { Code = "SUMMER" }), "ApplyCartDiscount"),
            (CartCommand.GetCartDiscounts(), "GetCartDiscounts"),
            (CartCommand.DeleteCartDiscounts(), "DeleteCartDiscounts"),
            (CartCommand.DeleteCartDiscount(0), "DeleteCartDiscount"),
            (CartCommand.RefreshCart(), "RefreshCart"),
            (CartCommand.ValidateCart(), "ValidateCart"),
        ];

        foreach ((CartCommand command, string type) in cases)
        {
            Assert.Equal(type, Wire(command).GetProperty("type").GetString());
        }
    }

    [Fact]
    public void A_command_body_is_sent_as_the_rest_call_sends_it()
    {
        JsonElement add = Wire(CartCommand.AddCartItem(new CartItemRequest
        {
            ItemYrn = "urn:yaas:saasag:caasproduct:product:acme;p1",
            Quantity = 2,
        }));

        Assert.Equal(
            "urn:yaas:saasag:caasproduct:product:acme;p1",
            add.GetProperty("data").GetProperty("itemYrn").GetString());
        Assert.Equal(2, add.GetProperty("data").GetProperty("quantity").GetDouble());

        // A batch command sends an array, a single-item command an object.
        JsonElement batch = Wire(CartCommand.AddCartItemsBatch([new CartItemRequest(), new CartItemRequest()]));
        Assert.Equal(2, batch.GetProperty("data").GetArrayLength());

        JsonElement discount = Wire(CartCommand.ApplyCartDiscount(new Discount { Code = "SUMMER" }));
        Assert.Equal("SUMMER", discount.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public void A_command_sends_only_the_options_it_was_given()
    {
        // The generated options start with partial = false and
        // expandCalculation = true. Neither may travel unless asked for.
        Assert.Equal(["itemId"], Names(Wire(CartCommand.DeleteCartItem("i1")).GetProperty("options")));

        JsonElement update = Wire(CartCommand.UpdateCartItem(
            "i1",
            new UpdateCartItem { Quantity = 3 },
            partial: true,
            resourceVersion: 7)).GetProperty("options");

        Assert.Equal(["itemId", "partial", "resourceVersion"], Names(update));
        Assert.True(update.GetProperty("partial").GetBoolean());
        Assert.Equal(7, update.GetProperty("resourceVersion").GetInt32());
    }

    [Fact]
    public void A_command_without_options_or_body_sends_neither()
    {
        Assert.Equal(["type"], Names(Wire(CartCommand.GetCart())));
    }

    [Fact]
    public void Reading_the_cart_takes_zip_and_country_together()
    {
        JsonElement options = Wire(CartCommand.GetCart(
            expandCalculation: false,
            zipCode: "8001",
            countryCode: "CH")).GetProperty("options");

        Assert.Equal(["expandCalculation", "zipCode", "countryCode"], Names(options));
        Assert.False(options.GetProperty("expandCalculation").GetBoolean());

        Assert.Throws<ArgumentException>(() => CartCommand.GetCart(zipCode: "8001"));
        Assert.Throws<ArgumentException>(() => CartCommand.GetCart(countryCode: "CH"));
    }

    [Fact]
    public void Discounts_are_removed_by_code_by_index_or_all_at_once()
    {
        Assert.Equal(["type"], Names(Wire(CartCommand.DeleteCartDiscounts())));

        JsonElement codes = Wire(CartCommand.DeleteCartDiscounts(["A", "B"]))
            .GetProperty("options")
            .GetProperty("codes");
        Assert.Equal(["A", "B"], codes.EnumerateArray().Select(code => code.GetString()));

        // An empty filter would read as «remove every discount».
        Assert.Throws<ArgumentException>(() => CartCommand.DeleteCartDiscounts([]));
        Assert.Throws<ArgumentException>(() => CartCommand.DeleteCartDiscounts(["A", " "]));

        Assert.Equal(
            "2",
            Wire(CartCommand.DeleteCartDiscount(2)).GetProperty("options").GetProperty("discountIndex").GetString());
        Assert.Throws<ArgumentOutOfRangeException>(() => CartCommand.DeleteCartDiscount(-1));
    }

    [Fact]
    public void Command_arguments_are_checked_when_the_command_is_built()
    {
        Assert.Throws<ArgumentNullException>(() => CartCommand.AddCartItem(null!));
        Assert.Throws<ArgumentException>(() => CartCommand.DeleteCartItem(" "));
        Assert.Throws<ArgumentNullException>(() => CartCommand.UpdateCartItem("i1", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => CartCommand.AddCartItemsBatch([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CartCommand.UpdateCartItemsBatch([]));
    }

    // ---------- Command chains: the request ----------

    private const string NoResults = """{"results":[]}""";

    private const string FailedSecond = """
        {"results":[
          {"index":0,"type":"GetCart","code":200,"status":"OK","data":{"id":"c1"}},
          {"index":1,"type":"DeleteCartItem","code":404,"status":"Not Found",
           "data":{"code":404,"status":"Not Found","message":"Cart item not found"}}
        ]}
        """;

    [Fact]
    public async Task A_command_chain_posts_to_the_execute_path_of_the_escaped_cart()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync("c 1", [CartCommand.GetCart()], Shopper);

        Assert.Equal(HttpMethod.Post, handler.RequestMethods[0]);
        Assert.Equal("/cart/acme/carts/c%201/execute", Uri(handler));
    }

    [Fact]
    public async Task A_command_chain_sends_its_commands_and_nothing_else_in_the_body()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync(
            "c1",
            [CartCommand.DeleteCartItem("i1"), CartCommand.GetCart()],
            Shopper,
            OnError.Resume,
            Versioning.Follow);

        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal(["commands"], Names(body.RootElement));
        Assert.Equal(2, body.RootElement.GetProperty("commands").GetArrayLength());
    }

    [Theory]
    [InlineData(OnError.Fail, Versioning.Skip, "?onError=fail&versioning=skip")]
    [InlineData(OnError.Resume, Versioning.Explicit, "?onError=resume&versioning=explicit")]
    [InlineData(OnError.Resume, Versioning.Follow, "?onError=resume&versioning=follow")]
    public async Task Error_mode_and_versioning_reach_the_query_as_the_specification_spells_them(
        OnError onError,
        Versioning versioning,
        string query)
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync("c1", [CartCommand.GetCart()], Shopper, onError, versioning);

        Assert.Equal("/cart/acme/carts/c1/execute" + query, Uri(handler));
    }

    [Fact]
    public async Task Without_an_error_mode_or_versioning_the_query_stays_empty()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync("c1", [CartCommand.GetCart()], Shopper);

        Assert.Equal("/cart/acme/carts/c1/execute", Uri(handler));
    }

    [Fact]
    public async Task A_command_chain_is_never_retried()
    {
        // A replay would apply every write in the chain a second time.
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync("c1", [CartCommand.RefreshCart()], Shopper);

        Assert.False(handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out _));
    }

    [Fact]
    public async Task A_command_chain_refuses_a_service_token_before_any_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await carts.ExecuteAsync("c1", [CartCommand.GetCart()], AuthContext.Service()));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task An_empty_or_broken_chain_is_refused_before_any_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await carts.ExecuteAsync("c1", [], Shopper));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await carts.ExecuteAsync("c1", null!, Shopper));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await carts.ExecuteAsync("c1", [null!], Shopper));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await carts.ExecuteAsync(" ", [CartCommand.GetCart()], Shopper));

        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Command chains: failures ----------

    [Theory]
    [InlineData(null)]
    [InlineData(OnError.Fail)]
    public async Task Unless_resumed_the_first_failed_command_is_thrown_as_its_rest_error(OnError? onError)
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, FailedSecond);
        CartService carts = Create(handler);

        EmporixNotFoundException exception = await Assert.ThrowsAsync<EmporixNotFoundException>(async () =>
            await carts.ExecuteAsync(
                "c1",
                [CartCommand.GetCart(), CartCommand.DeleteCartItem("gone")],
                Shopper,
                onError));

        Assert.Contains("command 1 (DeleteCartItem)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Cart item not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resumed_every_result_comes_back_and_nothing_is_thrown()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, FailedSecond);
        CartService carts = Create(handler);

        ExecuteResponse response = await carts.ExecuteAsync(
            "c1",
            [CartCommand.GetCart(), CartCommand.DeleteCartItem("gone")],
            Shopper,
            OnError.Resume);

        Assert.Equal([200, 404], response.Results.Select(result => result.Code));
    }

    [Fact]
    public async Task A_chain_answered_without_a_body_reads_as_no_results()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, string.Empty);
        CartService carts = Create(handler);

        ExecuteResponse response = await carts.ExecuteAsync("c1", [CartCommand.GetCart()], Shopper);

        Assert.Empty(response.Results);
    }

    // ---------- Command chains: reading results ----------

    private static ExecuteCommandResult Result(string type, int code, string data)
    {
        using JsonDocument document = JsonDocument.Parse(data);

        return new ExecuteCommandResult
        {
            Index = 0,
            Type = type,
            Code = code,
            Status = "OK",
            Data = document.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task A_chain_ending_in_GetCart_reads_back_the_created_item_and_the_cart()
    {
        // Through the wire on purpose: Data is declared as object, and this is
        // what shows it arrives as a JsonElement the readers can use.
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, """
            {"results":[
              {"index":0,"type":"AddCartItem","code":201,"status":"Created",
               "data":{"itemId":"i9","yrn":"urn:yaas:saasag:caascart:cartItem:acme;i9"}},
              {"index":1,"type":"GetCart","code":200,"status":"OK",
               "data":{"id":"c1","items":[{"id":"i9"}]}}
            ]}
            """);
        CartService carts = Create(handler);

        ExecuteResponse response = await carts.ExecuteAsync(
            "c1",
            [CartCommand.AddCartItem(new CartItemRequest()), CartCommand.GetCart()],
            Shopper);

        List<ExecuteCommandResult> results = [.. response.Results];
        Assert.Equal("i9", results[0].ReadCreatedItem()?.ItemId);

        Cart? cart = results[1].ReadCart();
        Assert.Equal("c1", cart?.Id);
        Assert.Equal("i9", Assert.Single(cart!.Items!).Id);
    }

    [Fact]
    public void Each_reader_reads_the_body_of_its_command()
    {
        Assert.Equal(
            201,
            Assert.Single(Result("AddCartItemsBatch", 200, """[{"index":0,"status":201}]""").ReadAddedItems()).Status);
        Assert.Equal(
            200,
            Assert.Single(Result("UpdateCartItemsBatch", 207, """[{"index":0,"code":200}]""").ReadUpdatedItems()).Code);
        Assert.Equal(
            "d1",
            Result("ApplyCartDiscount", 201, """{"yrn":"y","discountId":"d1","discountIndex":0}""")
                .ReadAppliedDiscount()?.DiscountId);
        Assert.Equal(
            "SUMMER",
            Assert.Single(Result("GetCartDiscounts", 200, """[{"code":"SUMMER"}]""").ReadDiscounts()).Code);
        Assert.True(Result("ValidateCart", 200, """{"isValid":true}""").ReadValidation()?.IsValid);
    }

    [Fact]
    public void A_failed_command_reads_as_nothing()
    {
        // Its data is the REST error body, not the command's type.
        const string Error = """{"code":404,"status":"Not Found","message":"gone"}""";

        Assert.Null(Result("GetCart", 404, Error).ReadCart());
        Assert.Empty(Result("GetCartDiscounts", 404, Error).ReadDiscounts());
    }

    [Fact]
    public void A_reader_refuses_the_result_of_another_command()
    {
        // Read as a cart, a validation result would come back as a mostly empty
        // Cart, and nothing would say so.
        ExecuteCommandResult validation = Result("ValidateCart", 200, """{"isValid":true}""");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => validation.ReadCart());

        Assert.Contains("ValidateCart", exception.Message, StringComparison.Ordinal);
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
