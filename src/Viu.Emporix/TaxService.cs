using Microsoft.Extensions.Options;
using Viu.Emporix.TaxServiceModels;

namespace Viu.Emporix;

/// <summary>
/// Tax configuration, and calculating tax for a given place.
/// </summary>
/// <remarks>
/// Tax is configured per location code, not per site: one configuration serves
/// every site selling into that place. Carts and orders already carry the
/// resulting tax as read-only values; this is where it comes from.
/// </remarks>
public sealed class TaxService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal TaxService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/tax/{_tenant}/taxes";

    /// <summary>Lists the tax configurations.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<TaxRetrieval>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
            },
            TaxJsonContext.Default.ListTaxRetrieval,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches the tax configuration for one location.</summary>
    /// <param name="locationCode">The location code, for example <c>CH</c>.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">Nothing is configured for this location.</exception>
    public async Task<TaxRetrieval?> GetAsync(
        string locationCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationCode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(locationCode)}",
                Auth = Defaults.Service(auth),
            },
            TaxJsonContext.Default.TaxRetrieval,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a tax configuration.</summary>
    /// <param name="configuration">The configuration to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<TaxCreationResponse?> CreateAsync(
        TaxCreation configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration,
                    TaxJsonContext.Default.TaxCreation),
            },
            TaxJsonContext.Default.TaxCreationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the tax configuration for one location.</summary>
    /// <param name="locationCode">The location code.</param>
    /// <param name="configuration">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Every total calculated after this uses the new rates. Nothing already
    /// ordered is recalculated.
    /// </remarks>
    public Task ReplaceAsync(
        string locationCode,
        TaxUpdate configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationCode);
        ArgumentNullException.ThrowIfNull(configuration);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(locationCode)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(configuration, TaxJsonContext.Default.TaxUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes the tax configuration for one location.</summary>
    /// <param name="locationCode">The location code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Selling into that place afterwards has no rates to apply, so this is not
    /// a way to make something tax-free.
    /// </remarks>
    public Task DeleteAsync(
        string locationCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationCode);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(locationCode)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Calculates tax for a set of amounts.</summary>
    /// <param name="request">What to calculate.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A <c>PUT</c> that changes nothing — Emporix models the calculation as a
    /// command, which is why the verb looks wrong for a read. Declared
    /// repeatable accordingly.
    /// </remarks>
    public async Task<TaxCalculationResponse?> CalculateAsync(
        TaxCalculationRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/calculation-commands",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    TaxJsonContext.Default.TaxCalculationRequest),
                Idempotent = true,
            },
            TaxJsonContext.Default.TaxCalculationResponse,
            cancellationToken).ConfigureAwait(false);
    }
}
