using System.Net;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

/// <summary>
/// The services that complete a checkout: tax, fees, coupons, payment,
/// shipping, returns and invoices.
/// </summary>
/// <remarks>
/// Weighted towards the decisions a defect would hide: which calls may be
/// retried, which refuse the wrong kind of token, and which of two
/// similar-looking paths a call actually uses.
/// </remarks>
public class CheckoutWaveTests
{
    private static readonly AuthContext Shopper = AuthContext.Anonymous();

    private static IOptions<EmporixOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EmporixOptions { Tenant = "acme" });

    private static EmporixHttpClient Http(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Options());

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    private static bool IsRepeatable(StubHttpMessageHandler handler)
        => handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out bool value) && value;

    // ---------- Payment: the money guard ----------

    [Theory]
    [InlineData("authorize")]
    [InlineData("capture")]
    [InlineData("refund")]
    [InlineData("cancel")]
    public async Task No_money_moving_call_is_ever_repeatable(string operation)
    {
        // Emporix offers no idempotency key on these endpoints, so a retry
        // cannot be made safe. Marking any of them repeatable would charge or
        // refund twice on a 5xx that arrived after the server had acted.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        PaymentService payments = new(Http(handler), Options());

        _ = operation switch
        {
            "authorize" => await payments.AuthorizeAsync(new PaymentModels.AuthorizePaymentRequest()),
            "capture" => await payments.CaptureAsync("t1", new PaymentModels.CaptureRequest()),
            "refund" => await payments.RefundAsync("t1", new PaymentModels.RefundRequest()),
            "cancel" => (object?)await payments.CancelAsync("t1"),
            _ => throw new InvalidOperationException(operation),
        };

        Assert.False(IsRepeatable(handler));
    }

    [Fact]
    public async Task A_browser_side_payment_refuses_a_service_token()
    {
        // A hosted payment belongs to a shopper. A service token has nobody to
        // charge, and Emporix would bind the payment to the wrong party.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        PaymentService payments = new(Http(handler), Options());

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await payments.InitializeFrontendAsync(
                new PaymentModels.InitializePaymentRequest(),
                AuthContext.Service()));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task The_frontend_view_of_a_payment_mode_is_a_different_endpoint()
    {
        // The configured form carries provider credentials. Serving it to a
        // browser would leak them, which is why this is a separate path rather
        // than a filter.
        StubHttpMessageHandler config = new(HttpStatusCode.OK, "[]");
        StubHttpMessageHandler frontend = new(HttpStatusCode.OK, "[]");

        await new PaymentService(Http(config), Options()).Modes.ListAsync();
        await new PaymentService(Http(frontend), Options()).Modes.ListForFrontendAsync();

        Assert.Equal("/payment-gateway/acme/paymentmodes/config", Uri(config));
        Assert.Equal("/payment-gateway/acme/paymentmodes/frontend", Uri(frontend));
    }

    [Fact]
    public async Task The_frontend_view_defaults_to_an_anonymous_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        PaymentService payments = new(Http(handler), Options());

        await payments.Modes.ListForFrontendAsync();

        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Anonymous, auth.Kind);
    }

    // ---------- Tax ----------

    [Fact]
    public async Task Calculating_tax_is_a_put_that_may_be_retried()
    {
        // Emporix models the calculation as a command, so the verb looks like a
        // write. It changes nothing, which is what decides the retry.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        TaxService taxes = new(Http(handler), Options());

        await taxes.CalculateAsync(new TaxServiceModels.TaxCalculationRequest());

        Assert.Equal(HttpMethod.Put, handler.RequestMethods[0]);
        Assert.Equal("/tax/acme/taxes/calculation-commands", Uri(handler));
        Assert.True(IsRepeatable(handler));
    }

    [Fact]
    public async Task Tax_is_addressed_by_location_not_by_site()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        TaxService taxes = new(Http(handler), Options());

        await taxes.GetAsync("CH");

        Assert.Equal("/tax/acme/taxes/CH", Uri(handler));
    }

    // ---------- Coupons ----------

    [Fact]
    public async Task Validating_a_coupon_may_be_retried_but_redeeming_may_not()
    {
        // The whole reason validation exists as its own endpoint: asking is
        // free, spending is not.
        StubHttpMessageHandler validate = new(HttpStatusCode.NoContent, string.Empty);
        StubHttpMessageHandler redeem = new(HttpStatusCode.Created, "{}");

        await new CouponService(Http(validate), Options())
            .ValidateAsync("SUMMER", new CouponModels.RedemptionCreation());
        await new CouponService(Http(redeem), Options())
            .Redemptions("SUMMER").CreateAsync(new CouponModels.RedemptionCreation());

        Assert.True(IsRepeatable(validate));
        Assert.False(IsRepeatable(redeem));
    }

    [Fact]
    public async Task A_coupon_code_is_escaped_into_every_path()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        CouponService coupons = new(Http(handler), Options());

        await coupons.Redemptions("SUMMER 20%").ListAsync();

        Assert.StartsWith(
            "/coupon/acme/coupons/SUMMER%2020%25/redemptions",
            Uri(handler),
            StringComparison.Ordinal);
    }

    // ---------- Fees ----------

    [Fact]
    public async Task Item_and_product_attachments_address_their_target_differently()
    {
        // A YRN on the product endpoint is a 404, and the other way round too.
        // Nothing in the type system says so, which is why it is pinned here.
        StubHttpMessageHandler item = new(HttpStatusCode.OK, "[]");
        StubHttpMessageHandler product = new(HttpStatusCode.OK, "[]");

        await new FeeService(Http(item), Options()).ForItem("urn:yaas:x;p1").ListAsync();
        await new FeeService(Http(product), Options()).ForProduct("p1").ListAsync();

        Assert.Equal("/fee/acme/itemFees/urn%3Ayaas%3Ax%3Bp1/fees", Uri(item));
        Assert.Equal("/fee/acme/productFees/p1/fees", Uri(product));
    }

    [Fact]
    public async Task A_fee_search_without_a_site_is_rejected_before_the_request()
    {
        // Emporix requires the site, and a fee is attached per site — an empty
        // list would be a 400 dressed up as «no fees configured».
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        FeeService fees = new(Http(handler), Options());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await fees.SearchByProductAsync("p1", []));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_fee_search_only_reads()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        FeeService fees = new(Http(handler), Options());

        await fees.SearchByProductAsync("p1", ["main"]);

        Assert.Equal(HttpMethod.Post, handler.RequestMethods[0]);
        Assert.True(IsRepeatable(handler));
    }

    [Fact]
    public async Task Replacing_the_attached_fees_sends_the_whole_set()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        FeeService fees = new(Http(handler), Options());

        await fees.ForProduct("p1").ReplaceAsync(["f1", "f2"]);

        Assert.Equal(HttpMethod.Put, handler.RequestMethods[0]);
        Assert.Contains("f2", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    // ---------- Shipping ----------

    [Fact]
    public async Task A_quote_may_be_retried_but_taking_a_slot_may_not()
    {
        // Reserving consumes capacity: a retry takes two places in one window
        // and turns the next shopper away from a slot nobody is using.
        StubHttpMessageHandler quote = new(HttpStatusCode.OK, "[]");
        StubHttpMessageHandler reserve = new(HttpStatusCode.NoContent, string.Empty);

        await new ShippingService(Http(quote), Options())
            .ForSite("main").QuoteAsync(new ShippingModels.QuotePayload());
        await new ShippingService(Http(reserve), Options())
            .ReserveDeliveryWindowAsync(new ShippingModels.DeliveryWindowValidationDto());

        Assert.True(IsRepeatable(quote));
        Assert.False(IsRepeatable(reserve));
    }

    [Fact]
    public async Task Site_scoped_paths_carry_the_site_before_the_resource()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ShippingService shipping = new(Http(handler), Options());

        await shipping.ForSite("main").MethodsIn("z1").ListAsync();

        Assert.Equal("/shipping/acme/main/zones/z1/methods", Uri(handler));
    }

    [Fact]
    public async Task Delivery_times_are_tenant_wide_and_carry_no_site()
    {
        // The one place the shipping service is not per site. Putting a site in
        // here would 404, and reading it as «no delivery times» would be worse.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ShippingService shipping = new(Http(handler), Options());

        await shipping.DeliveryTimes.SlotsOf("d1").ListAsync();

        Assert.Equal("/shipping/acme/delivery-times/d1/slots", Uri(handler));
    }

    [Fact]
    public async Task Finding_a_site_only_reads()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ShippingService shipping = new(Http(handler), Options());

        await shipping.FindSiteAsync(new ShippingModels.FindSiteRequest(), Shopper);

        Assert.Equal("/shipping/acme/findSite", Uri(handler));
        Assert.True(IsRepeatable(handler));
    }

    // ---------- Returns and invoices ----------

    [Fact]
    public async Task A_return_lives_under_the_singular_path()
    {
        // /return, not /returns — the service name and the collection differ.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        ReturnService returns = new(Http(handler), Options());

        await returns.GetAsync("r1");

        Assert.Equal("/return/acme/returns/r1", Uri(handler));
    }

    [Fact]
    public async Task Patching_a_return_with_no_operations_is_rejected()
    {
        // An empty patch is a request that cannot change anything and would
        // still count as a success.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        ReturnService returns = new(Http(handler), Options());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await returns.UpdateAsync("r1", []));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Starting_an_invoicing_job_is_never_repeatable()
    {
        // A retried start invoices the same orders twice.
        StubHttpMessageHandler handler = new(HttpStatusCode.Accepted, "{}");
        InvoiceService invoices = new(Http(handler), Options());

        await invoices.CreateJobAsync(new InvoiceModels.JobRequest());

        Assert.Equal("/invoice/acme/jobs/invoices", Uri(handler));
        Assert.False(IsRepeatable(handler));
    }

    // ---------- Shared ----------

    [Fact]
    public async Task Empty_identifiers_are_rejected_everywhere()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new TaxService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new CouponService(Http(handler), Options()).GetAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new FeeService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new PaymentService(Http(handler), Options()).GetTransactionAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new ReturnService(Http(handler), Options()).GetAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new InvoiceService(Http(handler), Options()).GetJobAsync(""));
        Assert.Throws<ArgumentException>(() =>
            new ShippingService(Http(handler), Options()).ForSite(""));

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

        Assert.NotNull(client.Taxes);
        Assert.NotNull(client.Fees);
        Assert.NotNull(client.Coupons);
        Assert.NotNull(client.Payments);
        Assert.NotNull(client.Shipping);
        Assert.NotNull(client.Returns);
        Assert.NotNull(client.Invoices);
    }
}
