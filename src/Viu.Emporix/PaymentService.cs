using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.PaymentModels;

namespace Viu.Emporix;

/// <summary>
/// Payment: which methods a tenant offers, and moving money against them.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, and the difference between them is who is allowed to call which.
/// <see cref="Modes"/> configures what a tenant accepts — server-side, except
/// for <see cref="PaymentModeOperations.ListForFrontendAsync"/>, which is the
/// same information reduced to what a browser may see. The rest of this class
/// moves the money.
/// </para>
/// <para>
/// <b>Nothing here is retried.</b> Every money-moving call is a <c>POST</c> that
/// changes the world, and a repeated authorize, capture or refund is a second
/// one. Emporix offers no idempotency key on these endpoints, so a retry cannot
/// be made safe — that decision is deliberate and not an oversight.
/// </para>
/// </remarks>
public sealed class PaymentService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal PaymentService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/payment-gateway/{_tenant}";

    /// <summary>The payment methods a tenant accepts, as configured.</summary>
    public PaymentModeOperations Modes => new(_http, _tenant);

    /// <summary>Authorizes a payment.</summary>
    /// <param name="request">What to charge, against what.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The transaction, whose id every later step needs.</returns>
    /// <exception cref="EmporixValidationException">
    /// The provider declined, or the request does not describe a chargeable
    /// payment.
    /// </exception>
    /// <remarks>
    /// Not repeatable. Keep the returned transaction id: a lost id means a
    /// possibly-authorized payment that cannot be captured or cancelled from
    /// here.
    /// </remarks>
    public async Task<AuthorizePaymentResponse?> AuthorizeAsync(
        AuthorizePaymentRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/payment/authorize",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    PaymentJsonContext.Default.AuthorizePaymentRequest),
            },
            PaymentJsonContext.Default.AuthorizePaymentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Captures an authorized payment.</summary>
    /// <param name="transactionId">The transaction from <see cref="AuthorizeAsync"/>.</param>
    /// <param name="request">How much to capture.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// This is the step that takes the money. Not repeatable.
    /// </remarks>
    public async Task<CommonPaymentResponse?> CaptureAsync(
        string transactionId,
        CaptureRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/payment/{Uri.EscapeDataString(transactionId)}/capture",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    PaymentJsonContext.Default.CaptureRequest),
            },
            PaymentJsonContext.Default.CommonPaymentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refunds a captured payment.</summary>
    /// <param name="transactionId">The transaction.</param>
    /// <param name="request">How much to refund.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Not repeatable, and this one refunds twice if it is — the failure mode
    /// costs real money in the customer's favour, which makes it no less a bug.
    /// </remarks>
    public async Task<CommonPaymentResponse?> RefundAsync(
        string transactionId,
        RefundRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/payment/{Uri.EscapeDataString(transactionId)}/refund",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    PaymentJsonContext.Default.RefundRequest),
            },
            PaymentJsonContext.Default.CommonPaymentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cancels an authorized payment before it is captured.</summary>
    /// <param name="transactionId">The transaction.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Releases the hold. After a capture there is nothing to cancel — refund
    /// instead.
    /// </remarks>
    public async Task<CommonPaymentResponse?> CancelAsync(
        string transactionId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/payment/{Uri.EscapeDataString(transactionId)}/cancel",
                Auth = Defaults.Service(auth),
            },
            PaymentJsonContext.Default.CommonPaymentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts a payment the shopper's browser will complete.</summary>
    /// <param name="request">What is being paid for.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixConfigurationException">The auth context is not a shopper's.</exception>
    /// <remarks>
    /// The hosted-provider flow: this returns what the browser needs to redirect
    /// or render, and <see cref="AuthorizeFrontendAsync"/> completes it once the
    /// shopper comes back. A service token has no shopper to pay, so it is
    /// refused rather than sent.
    /// </remarks>
    public async Task<InitializePaymentResponse?> InitializeFrontendAsync(
        InitializePaymentRequest request,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/payment/frontend/initialize",
                Auth = RequireShopper(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    PaymentJsonContext.Default.InitializePaymentRequest),
            },
            PaymentJsonContext.Default.InitializePaymentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Completes a browser-side payment.</summary>
    /// <param name="request">What the provider handed back.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Not repeatable: this is the authorization.</remarks>
    public async Task<AuthorizePaymentResponse?> AuthorizeFrontendAsync(
        AuthorizeFrontendPaymentRequest request,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/payment/frontend/authorize",
                Auth = RequireShopper(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    PaymentJsonContext.Default.AuthorizeFrontendPaymentRequest),
            },
            PaymentJsonContext.Default.AuthorizePaymentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of transactions.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<PaymentTransactionResponse>> ListTransactionsAsync(
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/transactions",
                Auth = Defaults.Service(auth),
                Query = Paging(pageNumber, pageSize),
            },
            PaymentJsonContext.Default.ListPaymentTransactionResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one transaction.</summary>
    /// <param name="transactionId">The transaction id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The way to find out what actually happened after a call whose answer was
    /// lost — which is why the transaction id is worth keeping.
    /// </remarks>
    public async Task<PaymentTransactionResponse?> GetTransactionAsync(
        string transactionId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/transactions/{Uri.EscapeDataString(transactionId)}",
                Auth = Defaults.Service(auth),
            },
            PaymentJsonContext.Default.PaymentTransactionResponse,
            cancellationToken).ConfigureAwait(false);
    }

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
    ];

    private static AuthContext RequireShopper(AuthContext auth)
        => auth.Kind is AuthKind.Customer or AuthKind.Anonymous or AuthKind.Raw
            ? auth
            : throw new EmporixConfigurationException(
                "A browser-side payment belongs to a shopper and therefore requires that "
                + "customer or anonymous context. For a server-side charge use AuthorizeAsync.");
}

/// <summary>
/// The payment methods a tenant accepts.
/// </summary>
/// <remarks>
/// The configured form carries what a browser must never see — provider
/// credentials among it — which is why <see cref="ListForFrontendAsync"/> exists
/// as a separate, reduced view rather than a filter applied afterwards.
/// </remarks>
public sealed class PaymentModeOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal PaymentModeOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/payment-gateway/{_tenant}/paymentmodes";

    /// <summary>Lists the configured payment methods.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Server-side only. Use <see cref="ListForFrontendAsync"/> for anything a
    /// browser will receive.
    /// </remarks>
    public async Task<IReadOnlyList<PaymentModeResponse>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/config",
                Auth = Defaults.Service(auth),
            },
            PaymentJsonContext.Default.ListPaymentModeResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one configured payment method.</summary>
    /// <param name="id">The payment method id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaymentModeResponse?> GetAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/config/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            PaymentJsonContext.Default.PaymentModeResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Configures a payment method.</summary>
    /// <param name="mode">The method to configure.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaymentModeResponse?> CreateAsync(
        PaymentModeRequest mode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/config",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    mode,
                    PaymentJsonContext.Default.PaymentModeRequest),
            },
            PaymentJsonContext.Default.PaymentModeResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a payment method's configuration.</summary>
    /// <param name="id">The payment method id.</param>
    /// <param name="mode">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Takes effect for the next checkout. Payments already authorized against
    /// the old configuration are unaffected.
    /// </remarks>
    public Task ReplaceAsync(
        string id,
        PaymentMethodUpdateRequest mode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(mode);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/config/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    mode,
                    PaymentJsonContext.Default.PaymentMethodUpdateRequest),
            },
            cancellationToken);
    }

    /// <summary>Removes a payment method.</summary>
    /// <param name="id">The payment method id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Nothing can be paid with it afterwards; past transactions remain.</remarks>
    public Task DeleteAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/config/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists the payment methods a shopper may be offered.</summary>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The reduced view: what a checkout page needs and nothing a browser must
    /// not have. This is the one to call from a storefront.
    /// </remarks>
    public async Task<IReadOnlyList<PaymentModeFrontendResponse>> ListForFrontendAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/frontend",
                Auth = Defaults.Anonymous(auth),
            },
            PaymentJsonContext.Default.ListPaymentModeFrontendResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one payment method as a shopper may see it.</summary>
    /// <param name="id">The payment method id.</param>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaymentModeFrontendResponse?> GetForFrontendAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/frontend/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Anonymous(auth),
            },
            PaymentJsonContext.Default.PaymentModeFrontendResponse,
            cancellationToken).ConfigureAwait(false);
    }
}
