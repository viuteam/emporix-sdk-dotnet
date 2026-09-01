using System.Globalization;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>Shared paging for the platform services.</summary>
internal static class PlatformPaging
{
    public static List<KeyValuePair<string, string?>> For(int pageNumber, int pageSize, string? query = null) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
        new("q", query),
    ];
}

/// <summary>
/// Sites — the storefronts a tenant runs.
/// </summary>
/// <remarks>
/// Nearly everything else is configured per site: prices, shipping zones,
/// availability. This is where the sites themselves come from, and
/// <see cref="MixinsOf"/> is where a tenant hangs its own settings on one.
/// </remarks>
public sealed class SiteService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal SiteService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/site/{_tenant}/sites";

    /// <summary>The tenant's own settings on one site.</summary>
    /// <param name="siteCode">The site.</param>
    public SiteMixinOperations MixinsOf(string siteCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);
        return new SiteMixinOperations(_http, $"{BasePath}/{Uri.EscapeDataString(siteCode)}/mixins");
    }

    /// <summary>Fetches a site.</summary>
    /// <param name="siteCode">The site code.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SiteSettingsServiceModels.SiteDto?> GetAsync(
        string siteCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(siteCode)}",
                Auth = Defaults.Anonymous(auth),
            },
            SiteJsonContext.Default.SiteDto,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the sites, with their full configuration.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<SiteSettingsServiceModels.SiteDto>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Anonymous(auth),
            },
            SiteJsonContext.Default.ListSiteDto,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the sites in short form.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Codes and names only. What a site switcher needs, without pulling every
    /// site's whole configuration.
    /// </remarks>
    public async Task<IReadOnlyList<SiteSettingsServiceModels.SiteDto>> ListShortAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/site/{_tenant}/siteslist",
                Auth = Defaults.Anonymous(auth),
            },
            SiteJsonContext.Default.ListSiteDto,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Creates a site.</summary>
    /// <param name="site">The site to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SiteSettingsServiceModels.ResourceLocation?> CreateAsync(
        SiteSettingsServiceModels.SiteDto site,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(site, SiteJsonContext.Default.SiteDto),
            },
            SiteJsonContext.Default.ResourceLocation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a site.</summary>
    /// <param name="siteCode">The site code.</param>
    /// <param name="site">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string siteCode,
        SiteSettingsServiceModels.SiteDto site,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);
        ArgumentNullException.ThrowIfNull(site);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(siteCode)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(site, SiteJsonContext.Default.SiteDto),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a site.</summary>
    /// <param name="siteCode">The site code.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string siteCode,
        SiteSettingsServiceModels.SiteDto changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(siteCode)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(changes, SiteJsonContext.Default.SiteDto),
            },
            cancellationToken);
    }

    /// <summary>Deletes a site.</summary>
    /// <param name="siteCode">The site code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Prices, zones and availability configured for it are orphaned rather than
    /// removed, so a deleted site is not a clean slate.
    /// </remarks>
    public Task DeleteAsync(
        string siteCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(siteCode)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// A tenant's own settings on one site.
/// </summary>
/// <remarks>
/// A mixin is arbitrary JSON under a name of the tenant's choosing, so it is
/// read and written as <see cref="System.Text.Json.JsonElement"/> — there is no
/// schema here to generate a type from. Reached through
/// <see cref="SiteService.MixinsOf"/>.
/// </remarks>
public sealed class SiteMixinOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal SiteMixinOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Lists the mixins.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<System.Text.Json.JsonElement> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = _basePath,
                Auth = Defaults.Anonymous(auth),
            },
            SiteJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Fetches one mixin.</summary>
    /// <param name="mixinName">The mixin name.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<System.Text.Json.JsonElement> GetAsync(
        string mixinName,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mixinName);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/{Uri.EscapeDataString(mixinName)}",
                Auth = Defaults.Anonymous(auth),
            },
            SiteJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds a mixin.</summary>
    /// <param name="mixin">The mixin, as JSON.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        System.Text.Json.JsonElement mixin,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(mixin, SiteJsonContext.Default.JsonElement),
            },
            cancellationToken);

    /// <summary>Replaces a mixin.</summary>
    /// <param name="mixinName">The mixin name.</param>
    /// <param name="mixin">The new value.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string mixinName,
        System.Text.Json.JsonElement mixin,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mixinName);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/{Uri.EscapeDataString(mixinName)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(mixin, SiteJsonContext.Default.JsonElement),
            },
            cancellationToken);
    }

    /// <summary>Changes part of a mixin.</summary>
    /// <param name="mixinName">The mixin name.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string mixinName,
        System.Text.Json.JsonElement changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mixinName);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{_basePath}/{Uri.EscapeDataString(mixinName)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(changes, SiteJsonContext.Default.JsonElement),
            },
            cancellationToken);
    }

    /// <summary>Deletes a mixin.</summary>
    /// <param name="mixinName">The mixin name.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string mixinName,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mixinName);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/{Uri.EscapeDataString(mixinName)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Vendors — who sells, in a marketplace.
/// </summary>
/// <remarks>
/// Products already carry a vendor as a read-only value. This is where vendors
/// and their locations are managed.
/// </remarks>
public sealed class VendorService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal VendorService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/vendor/{_tenant}/vendors";

    /// <summary>Fetches a vendor.</summary>
    /// <param name="vendorId">The vendor id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<VendorServiceModels.Vendor?> GetAsync(
        string vendorId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(vendorId)}",
                Auth = Defaults.Anonymous(auth),
            },
            VendorJsonContext.Default.Vendor,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of vendors.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<VendorServiceModels.Vendor>> ListAsync(
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
                Query = PlatformPaging.For(pageNumber, pageSize),
            },
            VendorJsonContext.Default.ListVendor,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches vendors.</summary>
    /// <param name="query">The Emporix query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<VendorServiceModels.Vendor>> SearchAsync(
        string query,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Anonymous(auth),
                Query = PlatformPaging.For(pageNumber, pageSize),
                Content = EmporixJsonContent.Create(
                    new VendorServiceModels.QParam { Q = query },
                    VendorJsonContext.Default.QParam),
                Idempotent = true,
            },
            VendorJsonContext.Default.ListVendor,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a vendor.</summary>
    /// <param name="vendor">The vendor to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<VendorServiceModels.ResourceId?> CreateAsync(
        VendorServiceModels.VendorCreate vendor,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vendor);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(vendor, VendorJsonContext.Default.VendorCreate),
            },
            VendorJsonContext.Default.ResourceId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a vendor.</summary>
    /// <param name="vendorId">The vendor id.</param>
    /// <param name="vendor">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string vendorId,
        VendorServiceModels.VendorUpdate vendor,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorId);
        ArgumentNullException.ThrowIfNull(vendor);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(vendorId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(vendor, VendorJsonContext.Default.VendorUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a vendor.</summary>
    /// <param name="vendorId">The vendor id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Their products keep a vendor reference that no longer resolves.</remarks>
    public Task DeleteAsync(
        string vendorId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(vendorId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists the vendor locations.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Tenant-level, not nested under a vendor — filter by vendor through the
    /// query rather than the path.
    /// </remarks>
    public async Task<PaginatedItems<VendorServiceModels.Location>> ListLocationsAsync(
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
                Path = $"/vendor/{_tenant}/locations",
                Auth = Defaults.Anonymous(auth),
                Query = PlatformPaging.For(pageNumber, pageSize),
            },
            VendorJsonContext.Default.ListLocation,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a vendor location.</summary>
    /// <param name="locationId">The location id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<VendorServiceModels.Location?> GetLocationAsync(
        string locationId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/vendor/{_tenant}/locations/{Uri.EscapeDataString(locationId)}",
                Auth = Defaults.Anonymous(auth),
            },
            VendorJsonContext.Default.Location,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a vendor location.</summary>
    /// <param name="location">The location to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<VendorServiceModels.ResourceId?> CreateLocationAsync(
        VendorServiceModels.LocationCreate location,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/vendor/{_tenant}/locations",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    location,
                    VendorJsonContext.Default.LocationCreate),
            },
            VendorJsonContext.Default.ResourceId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a vendor location.</summary>
    /// <param name="locationId">The location id.</param>
    /// <param name="location">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceLocationAsync(
        string locationId,
        VendorServiceModels.LocationUpdate location,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        ArgumentNullException.ThrowIfNull(location);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"/vendor/{_tenant}/locations/{Uri.EscapeDataString(locationId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    location,
                    VendorJsonContext.Default.LocationUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a vendor location.</summary>
    /// <param name="locationId">The location id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteLocationAsync(
        string locationId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"/vendor/{_tenant}/locations/{Uri.EscapeDataString(locationId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Currencies, and the rates between them.
/// </summary>
/// <remarks>
/// A price carries a currency code; this is where the codes a tenant accepts are
/// defined, and where the exchange rates for converting between them live.
/// </remarks>
public sealed class CurrencyService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CurrencyService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/currency/{_tenant}/currencies";

    /// <summary>Lists the currencies.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CurrencyServiceModels.CurrencyRetrieval>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Anonymous(auth),
            },
            CurrencyJsonContext.Default.ListCurrencyRetrieval,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a currency.</summary>
    /// <param name="currencyCode">The ISO 4217 code.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CurrencyServiceModels.CurrencyRetrieval?> GetAsync(
        string currencyCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(currencyCode)}",
                Auth = Defaults.Anonymous(auth),
            },
            CurrencyJsonContext.Default.CurrencyRetrieval,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds a currency.</summary>
    /// <param name="currency">The currency to add.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CurrencyServiceModels.CurrencyCreationResponse?> CreateAsync(
        CurrencyServiceModels.CurrencyCreation currency,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currency);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    currency,
                    CurrencyJsonContext.Default.CurrencyCreation),
            },
            CurrencyJsonContext.Default.CurrencyCreationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a currency.</summary>
    /// <param name="currencyCode">The ISO 4217 code.</param>
    /// <param name="currency">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string currencyCode,
        CurrencyServiceModels.CurrencyUpdate currency,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        ArgumentNullException.ThrowIfNull(currency);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(currencyCode)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    currency,
                    CurrencyJsonContext.Default.CurrencyUpdate),
            },
            cancellationToken);
    }

    /// <summary>Removes a currency.</summary>
    /// <param name="currencyCode">The ISO 4217 code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Prices in it stay stored but nothing can be sold in it.</remarks>
    public Task DeleteAsync(
        string currencyCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(currencyCode)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists the exchange rates.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CurrencyServiceModels.ExchangeRateRetrieval>> ListExchangeRatesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/currency/{_tenant}/exchanges",
                Auth = Defaults.Anonymous(auth),
            },
            CurrencyJsonContext.Default.ListExchangeRateRetrieval,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one exchange rate.</summary>
    /// <param name="code">The rate's code.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CurrencyServiceModels.ExchangeRateRetrieval?> GetExchangeRateAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/currency/{_tenant}/exchanges/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Anonymous(auth),
            },
            CurrencyJsonContext.Default.ExchangeRateRetrieval,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds an exchange rate.</summary>
    /// <param name="rate">The rate to add.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CurrencyServiceModels.ExchangeRateResponse?> CreateExchangeRateAsync(
        CurrencyServiceModels.ExchangeRateCreationRequest rate,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rate);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/currency/{_tenant}/exchanges",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    rate,
                    CurrencyJsonContext.Default.ExchangeRateCreationRequest),
            },
            CurrencyJsonContext.Default.ExchangeRateResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces an exchange rate.</summary>
    /// <param name="code">The rate's code.</param>
    /// <param name="rate">The new rate.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Conversions after this use the new rate. Nothing already converted is
    /// recalculated.
    /// </remarks>
    public Task ReplaceExchangeRateAsync(
        string code,
        CurrencyServiceModels.ExchangeRateUpdateRequest rate,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(rate);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"/currency/{_tenant}/exchanges/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    rate,
                    CurrencyJsonContext.Default.ExchangeRateUpdateRequest),
            },
            cancellationToken);
    }

    /// <summary>Removes an exchange rate.</summary>
    /// <param name="code">The rate's code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteExchangeRateAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"/currency/{_tenant}/exchanges/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Countries and regions.
/// </summary>
/// <remarks>
/// Mostly read-only: Emporix ships the list and a tenant switches entries on or
/// off. Addresses and tax configuration key off these codes.
/// </remarks>
public sealed class CountryService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CountryService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    /// <summary>Lists the countries.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CountryServiceModels.Country>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/country/{_tenant}/countries",
                Auth = Defaults.Anonymous(auth),
            },
            CountryJsonContext.Default.ListCountry,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a country.</summary>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 code.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CountryServiceModels.Country?> GetAsync(
        string countryCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/country/{_tenant}/countries/{Uri.EscapeDataString(countryCode)}",
                Auth = Defaults.Anonymous(auth),
            },
            CountryJsonContext.Default.Country,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes a country's settings.</summary>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 code.</param>
    /// <param name="changes">The fields to change — normally whether it is active.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Countries are not created or deleted, only switched on and off: the list
    /// is Emporix's, the selection is the tenant's.
    /// </remarks>
    public Task UpdateAsync(
        string countryCode,
        CountryServiceModels.CountryUpdate changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"/country/{_tenant}/countries/{Uri.EscapeDataString(countryCode)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    changes,
                    CountryJsonContext.Default.CountryUpdate),
            },
            cancellationToken);
    }

    /// <summary>Lists the regions.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CountryServiceModels.Region>> ListRegionsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/country/{_tenant}/regions",
                Auth = Defaults.Anonymous(auth),
            },
            CountryJsonContext.Default.ListRegion,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a region.</summary>
    /// <param name="regionCode">The region code.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CountryServiceModels.Region?> GetRegionAsync(
        string regionCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionCode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/country/{_tenant}/regions/{Uri.EscapeDataString(regionCode)}",
                Auth = Defaults.Anonymous(auth),
            },
            CountryJsonContext.Default.Region,
            cancellationToken).ConfigureAwait(false);
    }
}
