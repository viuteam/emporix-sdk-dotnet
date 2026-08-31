using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.ShippingModels;

namespace Viu.Emporix;

/// <summary>
/// Shipping: where a tenant delivers, how, at what cost, and when.
/// </summary>
/// <remarks>
/// <para>
/// The largest service in the API, and it splits along one line: what is
/// configured per <b>site</b> and what is configured for the tenant. Zones,
/// methods, quotes, groups and customer relations all hang off a site — reach
/// them through <see cref="ForSite"/>. Delivery times and windows are
/// tenant-wide.
/// </para>
/// <para>
/// A cart cannot be delivered without a zone whose area covers the address and a
/// method inside that zone. <see cref="ShippingSiteOperations.QuoteAsync"/> is
/// what a checkout calls to find out which methods apply and what they cost.
/// </para>
/// </remarks>
public sealed class ShippingService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal ShippingService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/shipping/{_tenant}";

    /// <summary>Everything configured for one site.</summary>
    /// <param name="site">The site code.</param>
    public ShippingSiteOperations ForSite(string site)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(site);
        return new ShippingSiteOperations(_http, _tenant, site);
    }

    /// <summary>Delivery times and their slots, configured tenant-wide.</summary>
    public ShippingDeliveryTimeOperations DeliveryTimes => new(_http, _tenant);

    /// <summary>Finds which site can deliver to an address.</summary>
    /// <param name="request">Where the goods are going.</param>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A <c>POST</c> because the address is a body, but declared repeatable: it
    /// only reads. Called before a cart exists, which is why it takes no cart.
    /// </remarks>
    public async Task<IReadOnlyList<Site>> FindSiteAsync(
        FindSiteRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/findSite",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    ShippingJsonContext.Default.FindSiteRequest),
                Idempotent = true,
            },
            ShippingJsonContext.Default.ListSite,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Reads the delivery windows available for a cart.</summary>
    /// <param name="cartId">The cart.</param>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// What a checkout offers as delivery dates. The windows depend on the cart
    /// because cut-off times and lead times depend on what is in it.
    /// </remarks>
    public async Task<IReadOnlyList<ActualDeliveryWindow>> GetDeliveryWindowsAsync(
        string cartId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/actualDeliveryWindows/{Uri.EscapeDataString(cartId)}",
                Auth = Defaults.Anonymous(auth),
            },
            ShippingJsonContext.Default.ListActualDeliveryWindow,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Reads the delivery windows for one delivery area and cart.</summary>
    /// <param name="deliveryAreaId">The delivery area.</param>
    /// <param name="cartId">The cart.</param>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ActualDeliveryWindow>> GetAreaDeliveryWindowsAsync(
        string deliveryAreaId,
        string cartId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryAreaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/areaDeliveryTimes/{Uri.EscapeDataString(deliveryAreaId)}"
                    + $"/{Uri.EscapeDataString(cartId)}",
                Auth = Defaults.Anonymous(auth),
            },
            ShippingJsonContext.Default.ListActualDeliveryWindow,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Checks whether a delivery window is still available.</summary>
    /// <param name="request">Which window, for which cart.</param>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A slot can fill between being offered and being chosen. This asks without
    /// taking it, so it is declared repeatable.
    /// </remarks>
    public Task ValidateDeliveryWindowAsync(
        DeliveryWindowValidationDto request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/deliveryWindowValidation",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    ShippingJsonContext.Default.DeliveryWindowValidationDto),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Takes a place in a delivery window.</summary>
    /// <param name="request">Which window.</param>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <b>Deliberately not repeatable.</b> This consumes capacity, so a retry
    /// takes two places in the same window and the second shopper is turned
    /// away for a slot nobody is using.
    /// </remarks>
    public Task ReserveDeliveryWindowAsync(
        DeliveryWindowValidationDto request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/actualDeliveryWindows/incrementCounter",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    ShippingJsonContext.Default.DeliveryWindowValidationDto),
            },
            cancellationToken);
    }

    /// <summary>Generates the concrete delivery windows from a cycle.</summary>
    /// <param name="cycle">The recurring pattern to expand.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Administrative: turns «every Tuesday» into dated windows a shopper can
    /// pick. Not repeatable — running it twice generates the windows twice.
    /// </remarks>
    public async Task<IReadOnlyList<ActualDeliveryWindow>> GenerateDeliveryCyclesAsync(
        DeliveryCycle cycle,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/delivery-cycles/generate",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    cycle,
                    ShippingJsonContext.Default.DeliveryCycle),
            },
            ShippingJsonContext.Default.ListActualDeliveryWindow,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
    ];
}

/// <summary>
/// Shipping configuration for one site.
/// </summary>
/// <remarks>
/// Reached through <see cref="ShippingService.ForSite"/>. A zone says where the
/// site delivers; a method inside it says how and at what price. Groups and
/// customer relations are how a tenant charges different customers differently
/// for the same delivery.
/// </remarks>
public sealed class ShippingSiteOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;
    private readonly string _site;

    internal ShippingSiteOperations(EmporixHttpClient http, string tenant, string site)
    {
        _http = http;
        _tenant = tenant;
        _site = site;
    }

    private string BasePath => $"/shipping/{_tenant}/{Uri.EscapeDataString(_site)}";

    /// <summary>The delivery methods inside one zone.</summary>
    /// <param name="zoneId">The zone.</param>
    public ShippingMethodOperations MethodsIn(string zoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        return new ShippingMethodOperations(_http, $"{BasePath}/zones/{Uri.EscapeDataString(zoneId)}/methods");
    }

    /// <summary>Lists the shipping zones.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<Zone>> ListZonesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/zones",
                Auth = Defaults.Anonymous(auth),
            },
            ShippingJsonContext.Default.ListZone,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one zone.</summary>
    /// <param name="zoneId">The zone id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Zone?> GetZoneAsync(
        string zoneId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/zones/{Uri.EscapeDataString(zoneId)}",
                Auth = Defaults.Anonymous(auth),
            },
            ShippingJsonContext.Default.Zone,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a zone.</summary>
    /// <param name="zone">The zone to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceCreatedResponse?> CreateZoneAsync(
        Zone zone,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/zones",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(zone, ShippingJsonContext.Default.Zone),
            },
            ShippingJsonContext.Default.ResourceCreatedResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a zone.</summary>
    /// <param name="zoneId">The zone id.</param>
    /// <param name="zone">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The methods inside the zone stay. Narrowing the area can leave addresses
    /// that were deliverable a moment ago without any zone at all.
    /// </remarks>
    public Task ReplaceZoneAsync(
        string zoneId,
        Zone zone,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentNullException.ThrowIfNull(zone);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/zones/{Uri.EscapeDataString(zoneId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(zone, ShippingJsonContext.Default.Zone),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a zone.</summary>
    /// <param name="zoneId">The zone id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateZoneAsync(
        string zoneId,
        Zone changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/zones/{Uri.EscapeDataString(zoneId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(changes, ShippingJsonContext.Default.Zone),
            },
            cancellationToken);
    }

    /// <summary>Deletes a zone.</summary>
    /// <param name="zoneId">The zone id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Its methods go with it, and the area stops being deliverable.</remarks>
    public Task DeleteZoneAsync(
        string zoneId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/zones/{Uri.EscapeDataString(zoneId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Asks what delivery would cost, and which methods apply.</summary>
    /// <param name="payload">The cart contents and destination.</param>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The call a checkout makes to fill its delivery step. A <c>POST</c>
    /// because the cart is a body, but declared repeatable: it only reads.
    /// </remarks>
    public async Task<IReadOnlyList<QuoteResponseItem>> QuoteAsync(
        QuotePayload payload,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/quote",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    payload,
                    ShippingJsonContext.Default.QuotePayload),
                Idempotent = true,
            },
            ShippingJsonContext.Default.ListQuoteResponseItem,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Asks for the cheapest delivery fee.</summary>
    /// <param name="payload">The cart contents and destination.</param>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// For a «from CHF x» line on a product page, where the full quote would be
    /// more than is needed.
    /// </remarks>
    public async Task<MinimumFee?> QuoteMinimumAsync(
        QuotePayload payload,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/quote/minimum",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    payload,
                    ShippingJsonContext.Default.QuotePayload),
                Idempotent = true,
            },
            ShippingJsonContext.Default.MinimumFee,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asks what delivery costs in one specific slot.</summary>
    /// <param name="payload">The cart, the destination and the slot.</param>
    /// <param name="auth">A customer or anonymous context; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>A named slot can cost more than the cheapest one — express delivery, typically.</remarks>
    public async Task<MinimumFee?> QuoteSlotAsync(
        QuoteSlot payload,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/quote/slot",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    payload,
                    ShippingJsonContext.Default.QuoteSlot),
                Idempotent = true,
            },
            ShippingJsonContext.Default.MinimumFee,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the shipping groups.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A group collects customers who share shipping terms. Which group a
    /// customer is in is a relation, not a field on the customer.
    /// </remarks>
    public async Task<IReadOnlyList<Group>> ListGroupsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/groups",
                Auth = Defaults.Service(auth),
            },
            ShippingJsonContext.Default.ListGroup,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one shipping group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Group?> GetGroupAsync(
        string groupId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/groups/{Uri.EscapeDataString(groupId)}",
                Auth = Defaults.Service(auth),
            },
            ShippingJsonContext.Default.Group,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a shipping group.</summary>
    /// <param name="group">The group to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceCreatedResponse?> CreateGroupAsync(
        Group group,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/groups",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(group, ShippingJsonContext.Default.Group),
            },
            ShippingJsonContext.Default.ResourceCreatedResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a shipping group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="group">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceGroupAsync(
        string groupId,
        Group group,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(group);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/groups/{Uri.EscapeDataString(groupId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(group, ShippingJsonContext.Default.Group),
            },
            cancellationToken);
    }

    /// <summary>Deletes a shipping group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Customers related to it fall back to the default terms.</remarks>
    public Task DeleteGroupAsync(
        string groupId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/groups/{Uri.EscapeDataString(groupId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists which customers belong to which shipping group.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CGRelation>> ListGroupRelationsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/cgrelations",
                Auth = Defaults.Service(auth),
            },
            ShippingJsonContext.Default.ListCGRelation,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Reads which shipping group one customer belongs to.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CGRelation?> GetGroupRelationAsync(
        string customerId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/cgrelations/{Uri.EscapeDataString(customerId)}",
                Auth = Defaults.Service(auth),
            },
            ShippingJsonContext.Default.CGRelation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Puts a customer into a shipping group.</summary>
    /// <param name="relation">Which customer, which group.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceCreatedResponse?> CreateGroupRelationAsync(
        CGRelation relation,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relation);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/cgrelations",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    relation,
                    ShippingJsonContext.Default.CGRelation),
            },
            ShippingJsonContext.Default.ResourceCreatedResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves a customer to another shipping group.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="relation">The new relation.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceGroupRelationAsync(
        string customerId,
        CGRelation relation,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(relation);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/cgrelations/{Uri.EscapeDataString(customerId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    relation,
                    ShippingJsonContext.Default.CGRelation),
            },
            cancellationToken);
    }

    /// <summary>Takes a customer out of their shipping group.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteGroupRelationAsync(
        string customerId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/cgrelations/{Uri.EscapeDataString(customerId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// The delivery methods inside one shipping zone.
/// </summary>
/// <remarks>
/// Reached through <see cref="ShippingSiteOperations.MethodsIn"/>. A method is
/// what a shopper picks — «standard», «express» — and it carries the fees.
/// </remarks>
public sealed class ShippingMethodOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal ShippingMethodOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Lists the methods.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<Method>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = _basePath,
                Auth = Defaults.Anonymous(auth),
            },
            ShippingJsonContext.Default.ListMethod,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one method.</summary>
    /// <param name="methodId">The method id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Method?> GetAsync(
        string methodId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/{Uri.EscapeDataString(methodId)}",
                Auth = Defaults.Anonymous(auth),
            },
            ShippingJsonContext.Default.Method,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a method.</summary>
    /// <param name="method">The method to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceCreatedResponse?> CreateAsync(
        Method method,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(method, ShippingJsonContext.Default.Method),
            },
            ShippingJsonContext.Default.ResourceCreatedResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a method.</summary>
    /// <param name="methodId">The method id.</param>
    /// <param name="method">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Carts quoted before this get the old fee until they are quoted again —
    /// which is why a checkout re-quotes rather than trusting what it stored.
    /// </remarks>
    public Task ReplaceAsync(
        string methodId,
        Method method,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodId);
        ArgumentNullException.ThrowIfNull(method);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/{Uri.EscapeDataString(methodId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(method, ShippingJsonContext.Default.Method),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a method.</summary>
    /// <param name="methodId">The method id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string methodId,
        Method changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{_basePath}/{Uri.EscapeDataString(methodId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(changes, ShippingJsonContext.Default.Method),
            },
            cancellationToken);
    }

    /// <summary>Deletes a method.</summary>
    /// <param name="methodId">The method id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A zone with no methods left is a zone nothing can be delivered to.
    /// </remarks>
    public Task DeleteAsync(
        string methodId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/{Uri.EscapeDataString(methodId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Delivery times, and the slots inside them.
/// </summary>
/// <remarks>
/// Configured tenant-wide rather than per site. A delivery time is the recurring
/// pattern — «Tuesdays, cut-off Monday noon» — and a slot narrows it to a window
/// within the day. Reached through <see cref="ShippingService.DeliveryTimes"/>.
/// </remarks>
public sealed class ShippingDeliveryTimeOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal ShippingDeliveryTimeOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/shipping/{_tenant}/delivery-times";

    /// <summary>The slots of one delivery time.</summary>
    /// <param name="deliveryTimeId">The delivery time.</param>
    public ShippingSlotOperations SlotsOf(string deliveryTimeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryTimeId);
        return new ShippingSlotOperations(_http, $"{BasePath}/{Uri.EscapeDataString(deliveryTimeId)}/slots");
    }

    /// <summary>Fetches one page of delivery times.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<DeliveryTime>> ListAsync(
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
                Auth = Defaults.Anonymous(auth),
                Query = ShippingService.Paging(pageNumber, pageSize),
            },
            ShippingJsonContext.Default.ListDeliveryTime,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one delivery time.</summary>
    /// <param name="deliveryTimeId">The delivery time id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<DeliveryTime?> GetAsync(
        string deliveryTimeId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryTimeId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(deliveryTimeId)}",
                Auth = Defaults.Anonymous(auth),
            },
            ShippingJsonContext.Default.DeliveryTime,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a delivery time.</summary>
    /// <param name="deliveryTime">The delivery time to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        BasicDeliveryTime deliveryTime,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliveryTime);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    deliveryTime,
                    ShippingJsonContext.Default.BasicDeliveryTime),
            },
            cancellationToken);
    }

    /// <summary>Creates several delivery times in one call.</summary>
    /// <param name="deliveryTimes">The delivery times to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateManyAsync(
        IEnumerable<BasicDeliveryTime> deliveryTimes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliveryTimes);

        List<BasicDeliveryTime> body = [.. deliveryTimes];

        return body.Count == 0
            ? Task.CompletedTask
            : _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Post,
                    Path = $"{BasePath}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        ShippingJsonContext.Default.ListBasicDeliveryTime),
                },
                cancellationToken);
    }

    /// <summary>Replaces a delivery time.</summary>
    /// <param name="deliveryTimeId">The delivery time id.</param>
    /// <param name="deliveryTime">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Windows already generated from this pattern are not regenerated — run
    /// <see cref="ShippingService.GenerateDeliveryCyclesAsync"/> for that.
    /// </remarks>
    public Task ReplaceAsync(
        string deliveryTimeId,
        UpdateDeliveryTime deliveryTime,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryTimeId);
        ArgumentNullException.ThrowIfNull(deliveryTime);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(deliveryTimeId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    deliveryTime,
                    ShippingJsonContext.Default.UpdateDeliveryTime),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a delivery time.</summary>
    /// <param name="deliveryTimeId">The delivery time id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string deliveryTimeId,
        UpdateDeliveryTime changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryTimeId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(deliveryTimeId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    changes,
                    ShippingJsonContext.Default.UpdateDeliveryTime),
            },
            cancellationToken);
    }

    /// <summary>Deletes a delivery time.</summary>
    /// <param name="deliveryTimeId">The delivery time id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string deliveryTimeId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryTimeId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(deliveryTimeId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// The slots of one delivery time.
/// </summary>
/// <remarks>
/// Reached through <see cref="ShippingDeliveryTimeOperations.SlotsOf"/>. A slot
/// is a window within a delivery day, with its own capacity — which is what
/// <see cref="ShippingService.ReserveDeliveryWindowAsync"/> consumes.
/// </remarks>
public sealed class ShippingSlotOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal ShippingSlotOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Lists the slots.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<SlotCreation>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = _basePath,
                Auth = Defaults.Anonymous(auth),
            },
            ShippingJsonContext.Default.ListSlotCreation,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one slot.</summary>
    /// <param name="slotId">The slot id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SlotCreation?> GetAsync(
        string slotId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/{Uri.EscapeDataString(slotId)}",
                Auth = Defaults.Anonymous(auth),
            },
            ShippingJsonContext.Default.SlotCreation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a slot.</summary>
    /// <param name="slot">The slot to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        SlotCreation slot,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    slot,
                    ShippingJsonContext.Default.SlotCreation),
            },
            cancellationToken);
    }

    /// <summary>Replaces a slot.</summary>
    /// <param name="slotId">The slot id.</param>
    /// <param name="slot">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string slotId,
        SlotCreation slot,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(slot);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/{Uri.EscapeDataString(slotId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    slot,
                    ShippingJsonContext.Default.SlotCreation),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a slot.</summary>
    /// <param name="slotId">The slot id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string slotId,
        SlotCreation changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{_basePath}/{Uri.EscapeDataString(slotId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    changes,
                    ShippingJsonContext.Default.SlotCreation),
            },
            cancellationToken);
    }

    /// <summary>Deletes one slot.</summary>
    /// <param name="slotId">The slot id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string slotId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/{Uri.EscapeDataString(slotId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Deletes every slot of this delivery time.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The delivery time survives with no slots, which means it offers whole
    /// days rather than windows.
    /// </remarks>
    public Task DeleteAllAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = _basePath,
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
}
