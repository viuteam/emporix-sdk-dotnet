using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.QuoteModels;

namespace Viu.Emporix;

/// <summary>
/// Quotes — a negotiated price, before it becomes an order.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see cref="CheckoutService.PlaceOrderFromQuoteAsync"/> has been
/// pointing at. A buyer asks, a seller prices, and the accepted quote becomes an
/// order without re-pricing anything.
/// </para>
/// <para>
/// A quote moves through statuses rather than being edited freely: use
/// <see cref="ChangeStatusAsync"/> to advance it and <see cref="UpdateAsync"/>
/// to change what is in it.
/// </para>
/// </remarks>
public sealed class QuoteService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal QuoteService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/quote/{_tenant}/quotes";

    /// <summary>The reasons a quote may be rejected or requested.</summary>
    public QuoteReasonOperations Reasons => new(_http, _tenant);

    /// <summary>Fetches a quote.</summary>
    /// <param name="quoteId">The quote id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<QuoteResponse?> GetAsync(
        string quoteId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(quoteId)}",
                Auth = Defaults.Service(auth),
            },
            QuoteJsonContext.Default.QuoteResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of quotes.</summary>
    /// <param name="query">An optional Emporix filter.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// With a customer's own token this lists that customer's quotes; with a
    /// service token, the tenant's. Emporix decides from the token.
    /// </remarks>
    public async Task<PaginatedItems<QuoteResponse>> ListAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            new("q", query),
        ];

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = parameters,
            },
            QuoteJsonContext.Default.ListQuoteResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests a quote.</summary>
    /// <param name="quote">What is being asked for.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Deliberately not repeatable: a retried request is a second quote for the
    /// same enquiry, and a seller then prices the same thing twice.
    /// </remarks>
    public async Task<QuoteIdResponse?> CreateAsync(
        QuoteCreateRequest quote,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quote);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    quote,
                    QuoteJsonContext.Default.QuoteCreateRequest),
            },
            QuoteJsonContext.Default.QuoteIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes what a quote contains.</summary>
    /// <param name="quoteId">The quote id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// This is the seller's side: prices, items, validity. Whether it is allowed
    /// depends on the quote's status — a quote already accepted does not change.
    /// </remarks>
    public Task UpdateAsync(
        string quoteId,
        QuoteUpdateRequest changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(quoteId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    changes,
                    QuoteJsonContext.Default.QuoteUpdateRequest),
            },
            cancellationToken);
    }

    /// <summary>Moves a quote to another status.</summary>
    /// <param name="quoteId">The quote id.</param>
    /// <param name="status">The target status.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">
    /// The transition is not allowed from where the quote stands.
    /// </exception>
    /// <remarks>
    /// Accepting is what makes a quote usable by
    /// <see cref="CheckoutService.PlaceOrderFromQuoteAsync"/>.
    /// </remarks>
    public Task ChangeStatusAsync(
        string quoteId,
        QuoteUpdateStatus status,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteId);
        ArgumentNullException.ThrowIfNull(status);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(quoteId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    status,
                    QuoteJsonContext.Default.QuoteUpdateStatus),
            },
            cancellationToken);
    }

    /// <summary>Deletes a quote.</summary>
    /// <param name="quoteId">The quote id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string quoteId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(quoteId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Reads a quote's history.</summary>
    /// <param name="quoteId">The quote id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Who changed what, and when — the record a negotiation leaves behind.
    /// </remarks>
    public async Task<IReadOnlyList<QuoteHistory>> GetHistoryAsync(
        string quoteId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(quoteId)}/history",
                Auth = Defaults.Service(auth),
            },
            QuoteJsonContext.Default.ListQuoteHistory,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Renders a quote as a PDF.</summary>
    /// <param name="quoteId">The quote id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, undisposed — the caller owns it.</returns>
    /// <remarks>
    /// The response is a document, not JSON, so it comes back raw and the caller
    /// decides whether to stream or buffer it. Error statuses are not translated
    /// here for the same reason.
    /// </remarks>
    public Task<HttpResponseMessage> RenderPdfAsync(
        string quoteId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteId);

        return _http.SendRawAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(quoteId)}/pdf",
                Auth = Defaults.Service(auth),

                // Rendering produces a document rather than changing the quote,
                // so a retry costs a second render and nothing else.
                Idempotent = true,
            },
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// The reasons a quote carries.
/// </summary>
/// <remarks>
/// Configured per tenant: why a quote was requested, or why it was turned down.
/// Reached through <see cref="QuoteService.Reasons"/>.
/// </remarks>
public sealed class QuoteReasonOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal QuoteReasonOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/quote/{_tenant}/quote-reasons";

    /// <summary>Lists the reasons.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<QuoteReasonResponse>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
            },
            QuoteJsonContext.Default.ListQuoteReasonResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one reason.</summary>
    /// <param name="quoteReasonId">The reason id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<QuoteReasonResponse?> GetAsync(
        string quoteReasonId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteReasonId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(quoteReasonId)}",
                Auth = Defaults.Service(auth),
            },
            QuoteJsonContext.Default.QuoteReasonResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a reason.</summary>
    /// <param name="reason">The reason to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<QuoteReasonIdResponse?> CreateAsync(
        QuoteReasonCreateRequest reason,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reason);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    reason,
                    QuoteJsonContext.Default.QuoteReasonCreateRequest),
            },
            QuoteJsonContext.Default.QuoteReasonIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a reason.</summary>
    /// <param name="quoteReasonId">The reason id.</param>
    /// <param name="reason">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string quoteReasonId,
        QuoteReasonUpdateRequest reason,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteReasonId);
        ArgumentNullException.ThrowIfNull(reason);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(quoteReasonId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    reason,
                    QuoteJsonContext.Default.QuoteReasonUpdateRequest),
            },
            cancellationToken);
    }

    /// <summary>Deletes a reason.</summary>
    /// <param name="quoteReasonId">The reason id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Quotes already carrying it keep the reason they were given.</remarks>
    public Task DeleteAsync(
        string quoteReasonId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteReasonId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(quoteReasonId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}
