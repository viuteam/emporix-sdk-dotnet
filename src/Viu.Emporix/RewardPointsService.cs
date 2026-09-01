using Microsoft.Extensions.Options;
using Viu.Emporix.RewardPointsModels;

namespace Viu.Emporix;

/// <summary>
/// Reward points — earning them, spending them, and what they can be spent on.
/// </summary>
/// <remarks>
/// <para>
/// Two audiences. Employees work against <c>/customer/{customerId}</c> with a
/// service token and the <c>rewardspoints.*</c> scopes; a signed-in shopper
/// reads their own balance under <c>/public/customer</c> with their own token
/// and no customer id at all — Emporix takes it from the token.
/// </para>
/// <para>
/// Unlike the rest of the API, most of this service does not name the tenant in
/// the address either: it too comes from the access token. Only the redemption
/// options do, which is why those methods build a different path.
/// </para>
/// <para>
/// Points are worth money. <see cref="AddPointsAsync"/>,
/// <see cref="RedeemPointsAsync"/> and <see cref="RedeemForCouponAsync"/> are
/// deliberately not repeatable: a retried award grants twice, and a retried
/// redemption spends twice.
/// </para>
/// </remarks>
public sealed class RewardPointsService
{
    private const string BasePath = "/reward-points";

    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal RewardPointsService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    /// <summary>Reads the point summary of every customer.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Employees only. The tenant comes from the token.</remarks>
    public async Task<IReadOnlyList<PointsSummaryOut>> GetSummaryBatchAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        CustomerSummaryBatchOut? summary = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/summaryBatch",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            RewardPointsJsonContext.Default.CustomerSummaryBatchOut,
            cancellationToken).ConfigureAwait(false);

        return summary is null ? [] : [.. summary];
    }

    /// <summary>Reads one customer's point balance.</summary>
    /// <param name="customerId">Whose balance.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The number of points. The endpoint answers with a bare number, not an object.</returns>
    public async Task<int> GetPointsAsync(
        string customerId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/customer/{Uri.EscapeDataString(customerId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            RewardPointsJsonContext.Default.Int32,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one customer's point history.</summary>
    /// <param name="customerId">Whose history.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Every award and every redemption, not just the balance.</remarks>
    public async Task<PointsSummaryOut?> GetSummaryAsync(
        string customerId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/customer/{Uri.EscapeDataString(customerId)}/summary",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            RewardPointsJsonContext.Default.PointsSummaryOut,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a point account for a customer.</summary>
    /// <param name="customerId">Whose account.</param>
    /// <param name="customer">The opening entry.</param>
    /// <param name="siteCode">Which site the account belongs to. Emporix defaults to <c>main</c>.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateCustomerAsync(
        string customerId,
        NewCustomerIn customer,
        string? siteCode = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(customer);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/customer/{Uri.EscapeDataString(customerId)}",
                Auth = Defaults.Service(auth),
                Query = SiteCode(siteCode),
                Content = EmporixJsonContent.Create(
                    customer, RewardPointsJsonContext.Default.NewCustomerIn),
            },
            cancellationToken);
    }

    /// <summary>Deletes a customer's point account.</summary>
    /// <param name="customerId">Whose account.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The points go with it.</remarks>
    public Task DeleteCustomerAsync(
        string customerId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/customer/{Uri.EscapeDataString(customerId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Awards points to a customer.</summary>
    /// <param name="customerId">Who receives them.</param>
    /// <param name="points">How many, and what for.</param>
    /// <param name="siteCode">Which site. Emporix defaults to <c>main</c>.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Not repeatable. A retry after a timeout would award the points a second
    /// time, and nothing in the request lets Emporix recognise the repeat.
    /// </remarks>
    public Task AddPointsAsync(
        string customerId,
        AddedPoints points,
        string? siteCode = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(points);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/customer/{Uri.EscapeDataString(customerId)}/addPoints",
                Auth = Defaults.Service(auth),
                Query = SiteCode(siteCode),
                Content = EmporixJsonContent.Create(
                    points, RewardPointsJsonContext.Default.AddedPoints),
            },
            cancellationToken);
    }

    /// <summary>Spends a customer's points without issuing a coupon.</summary>
    /// <param name="customerId">Whose points.</param>
    /// <param name="points">How many, and what for.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Not repeatable, for the same reason as awarding. For the coupon-issuing
    /// variant see <see cref="RedeemForCouponAsync"/>.
    /// </remarks>
    public Task RedeemPointsAsync(
        string customerId,
        RedeemedPoints points,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(points);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/customer/{Uri.EscapeDataString(customerId)}/redeemPoints",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    points, RewardPointsJsonContext.Default.RedeemedPoints),
            },
            cancellationToken);
    }

    /// <summary>Reads the signed-in customer's own point balance.</summary>
    /// <param name="auth">The customer's token. Required — there is no customer id in the address.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixConfigurationException">
    /// <paramref name="auth"/> is not a customer context.
    /// </exception>
    public async Task<int> GetMyPointsAsync(
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        RequireCustomer(auth, "reading your own points");

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/public/customer",
                Auth = auth,
                Idempotent = true,
            },
            RewardPointsJsonContext.Default.Int32,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the signed-in customer's own point history.</summary>
    /// <param name="auth">The customer's token. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixConfigurationException">
    /// <paramref name="auth"/> is not a customer context.
    /// </exception>
    public async Task<PointsSummaryOut?> GetMySummaryAsync(
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        RequireCustomer(auth, "reading your own point history");

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/public/customer/summary",
                Auth = auth,
                Idempotent = true,
            },
            RewardPointsJsonContext.Default.PointsSummaryOut,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns the signed-in customer's points into a coupon.</summary>
    /// <param name="option">Which redemption option to use.</param>
    /// <param name="auth">The customer's token. Required.</param>
    /// <param name="siteCode">Which site. Emporix defaults to <c>main</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The coupon that was created.</returns>
    /// <exception cref="EmporixConfigurationException">
    /// <paramref name="auth"/> is not a customer context.
    /// </exception>
    /// <remarks>
    /// Not repeatable. Points are deducted and a coupon is issued; running it
    /// twice does both twice.
    /// </remarks>
    public async Task<RedeemCouponOut?> RedeemForCouponAsync(
        RedeemOptionIn option,
        AuthContext auth,
        string? siteCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        RequireCustomer(auth, "redeeming your own points");

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/public/customer/redeem",
                Auth = auth,
                Query = SiteCode(siteCode),
                Content = EmporixJsonContent.Create(
                    option, RewardPointsJsonContext.Default.RedeemOptionIn),
            },
            RewardPointsJsonContext.Default.RedeemCouponOut,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists what points can be exchanged for.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Open to any token — a storefront can show the rewards catalogue before
    /// anyone signs in.
    /// </remarks>
    public async Task<IReadOnlyList<RedeemOption>> ListRedeemOptionsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        RedeemOptions? options = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{_tenant}/redeemOptions",
                Auth = Defaults.Anonymous(auth),
                Idempotent = true,
            },
            RewardPointsJsonContext.Default.RedeemOptions,
            cancellationToken).ConfigureAwait(false);

        return options is null ? [] : [.. options];
    }

    /// <summary>Adds a redemption option.</summary>
    /// <param name="option">What the points buy, and how many it costs.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The options after the addition.</returns>
    public async Task<IReadOnlyList<RedeemOption>> CreateRedeemOptionAsync(
        RedeemOption option,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);

        RedeemOptions? options = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{_tenant}/redeemOptions",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    option, RewardPointsJsonContext.Default.RedeemOption),
            },
            RewardPointsJsonContext.Default.RedeemOptions,
            cancellationToken).ConfigureAwait(false);

        return options is null ? [] : [.. options];
    }

    /// <summary>Replaces a redemption option.</summary>
    /// <param name="redeemOptionId">Which option.</param>
    /// <param name="option">The option, whole.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Answers <c>409</c> when someone changed the option in the meantime.</remarks>
    public Task UpdateRedeemOptionAsync(
        string redeemOptionId,
        RedeemOption option,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redeemOptionId);
        ArgumentNullException.ThrowIfNull(option);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{_tenant}/redeemOptions/{Uri.EscapeDataString(redeemOptionId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    option, RewardPointsJsonContext.Default.RedeemOption),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Removes a redemption option.</summary>
    /// <param name="redeemOptionId">Which option.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteRedeemOptionAsync(
        string redeemOptionId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redeemOptionId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{_tenant}/redeemOptions/{Uri.EscapeDataString(redeemOptionId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            cancellationToken);
    }

    private static List<KeyValuePair<string, string?>> SiteCode(string? siteCode)
        => string.IsNullOrWhiteSpace(siteCode) ? [] : [new("siteCode", siteCode)];

    private static void RequireCustomer(AuthContext auth, string what)
    {
        if (auth.Kind is not AuthKind.Customer)
        {
            throw new EmporixConfigurationException(
                $"A customer auth context is required for {what}. These endpoints "
                + "carry no customer id: Emporix reads it from the token.");
        }
    }
}
