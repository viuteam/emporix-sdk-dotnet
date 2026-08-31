using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.CouponModels;

namespace Viu.Emporix;

/// <summary>
/// Coupons, their redemptions, and referral coupons.
/// </summary>
/// <remarks>
/// <para>
/// A cart applies a coupon through <see cref="CartService.ApplyCouponAsync"/>;
/// this is where coupons are created and where the redemption record lives.
/// </para>
/// <para>
/// A coupon is addressed by its code, not by an id — the code is the identity,
/// which is why changing it means creating a new coupon.
/// </para>
/// </remarks>
public sealed class CouponService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CouponService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/coupon/{_tenant}/coupons";

    /// <summary>Fetches a coupon by its code.</summary>
    /// <param name="code">The coupon code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No coupon has this code.</exception>
    public async Task<Coupon?> GetAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
            },
            CouponJsonContext.Default.Coupon,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of coupons.</summary>
    /// <param name="query">An optional Emporix filter.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<CouponWithIdAndStatus>> ListAsync(
        string? query = null,
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
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = Paging(pageNumber, pageSize, query),
            },
            CouponJsonContext.Default.ListCouponWithIdAndStatus,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a coupon.</summary>
    /// <param name="coupon">The coupon to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">The code is already taken, or the discount is malformed.</exception>
    public async Task<ResourceLocation?> CreateAsync(
        CouponCreation coupon,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coupon);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    coupon,
                    CouponJsonContext.Default.CouponCreation),
            },
            CouponJsonContext.Default.ResourceLocation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a coupon.</summary>
    /// <param name="code">The coupon code.</param>
    /// <param name="coupon">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Coupons already redeemed keep their redemption records; this changes what
    /// a future redemption is worth.
    /// </remarks>
    public Task ReplaceAsync(
        string code,
        BaseCoupon coupon,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(coupon);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(coupon, CouponJsonContext.Default.BaseCoupon),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a coupon.</summary>
    /// <param name="code">The coupon code.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string code,
        BaseCoupon changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(changes, CouponJsonContext.Default.BaseCoupon),
            },
            cancellationToken);
    }

    /// <summary>Deletes a coupon.</summary>
    /// <param name="code">The coupon code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Checks whether a coupon may be redeemed, without redeeming it.</summary>
    /// <param name="code">The coupon code.</param>
    /// <param name="redemption">The intended redemption — who, on what cart, for how much.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">
    /// The coupon is expired, exhausted, or its conditions are not met.
    /// </exception>
    /// <remarks>
    /// A <c>POST</c> that changes nothing, so it is declared repeatable — the
    /// point of validating separately is to ask before committing.
    /// </remarks>
    public Task ValidateAsync(
        string code,
        RedemptionCreation redemption,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(redemption);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(code)}/validation",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    redemption,
                    CouponJsonContext.Default.RedemptionCreation),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Redemptions of a coupon.</summary>
    /// <param name="code">The coupon code.</param>
    public CouponRedemptionOperations Redemptions(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new CouponRedemptionOperations(_http, _tenant, code);
    }

    /// <summary>Fetches a customer's referral coupon.</summary>
    /// <param name="customerNumber">The customer.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ReferralCoupon?> GetReferralAsync(
        string customerNumber,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/coupon/{_tenant}/referral-coupons/{Uri.EscapeDataString(customerNumber)}",
                Auth = Defaults.Service(auth),
            },
            CouponJsonContext.Default.ReferralCoupon,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a referral coupon for a customer.</summary>
    /// <param name="customerNumber">The customer to create it for.</param>
    /// <param name="coupon">What the referral is worth.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Not repeatable: a customer with two referral coupons can spend both.
    /// </remarks>
    public async Task<ReferralCoupon?> CreateReferralAsync(
        string customerNumber,
        CouponCreation coupon,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);
        ArgumentNullException.ThrowIfNull(coupon);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/coupon/{_tenant}/referral-coupons/{Uri.EscapeDataString(customerNumber)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    coupon,
                    CouponJsonContext.Default.CouponCreation),
            },
            CouponJsonContext.Default.ReferralCoupon,
            cancellationToken).ConfigureAwait(false);
    }

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize, string? query) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
        new("q", query),
    ];
}

/// <summary>
/// The redemptions of one coupon.
/// </summary>
/// <remarks>
/// A redemption is the record that a coupon was spent — by whom, on which cart,
/// for how much. Reached through <see cref="CouponService.Redemptions"/>.
/// </remarks>
public sealed class CouponRedemptionOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;
    private readonly string _code;

    internal CouponRedemptionOperations(EmporixHttpClient http, string tenant, string code)
    {
        _http = http;
        _tenant = tenant;
        _code = code;
    }

    private string BasePath
        => $"/coupon/{_tenant}/coupons/{Uri.EscapeDataString(_code)}/redemptions";

    /// <summary>Fetches one page of redemptions.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<Redemption>> ListAsync(
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
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = CouponService.Paging(pageNumber, pageSize, null),
            },
            CouponJsonContext.Default.ListRedemption,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one redemption.</summary>
    /// <param name="redemptionId">The redemption id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Redemption?> GetAsync(
        string redemptionId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redemptionId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(redemptionId)}",
                Auth = Defaults.Service(auth),
            },
            CouponJsonContext.Default.Redemption,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Redeems the coupon.</summary>
    /// <param name="redemption">Who is redeeming, on what, for how much.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Deliberately not repeatable: this is the call that spends the coupon, and
    /// a retry spends it twice. Validate first with
    /// <see cref="CouponService.ValidateAsync"/> if you need to ask before
    /// committing.
    /// </remarks>
    public async Task<ResourceLocation?> CreateAsync(
        RedemptionCreation redemption,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(redemption);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    redemption,
                    CouponJsonContext.Default.RedemptionCreation),
            },
            CouponJsonContext.Default.ResourceLocation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reverses a redemption.</summary>
    /// <param name="redemptionId">The redemption id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// What to call when an order is cancelled: it gives the coupon back rather
    /// than deleting the coupon itself.
    /// </remarks>
    public Task DeleteAsync(
        string redemptionId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redemptionId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(redemptionId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}
