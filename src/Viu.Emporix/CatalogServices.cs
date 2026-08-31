using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.BrandServiceModels;
using Viu.Emporix.CatalogModels;
using Viu.Emporix.LabelServiceModels;

namespace Viu.Emporix;

/// <summary>
/// Product brands.
/// </summary>
/// <remarks>
/// Reads default to a service token but also work anonymously. Note the path
/// carries no tenant segment — Emporix takes the tenant from the token here.
/// </remarks>
public sealed class BrandService
{
    private readonly EmporixHttpClient _http;

    internal BrandService(EmporixHttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    // No tenant in the path: this service derives it from the token.
    private const string BasePath = "/brand/brands";

    /// <summary>Lists every brand.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<BrandResponse>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest { Method = HttpMethod.Get, Path = BasePath, Auth = Defaults.Service(auth) },
            BrandJsonContext.Default.ListBrandResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a brand by its id.</summary>
    /// <param name="brandId">The brand id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No brand exists with this id.</exception>
    public async Task<BrandResponse?> GetAsync(
        string brandId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(brandId)}",
                Auth = Defaults.Service(auth),
            },
            BrandJsonContext.Default.BrandResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a brand.</summary>
    /// <param name="brand">The brand to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<BrandResponse?> CreateAsync(
        Brand brand,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brand);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(brand, BrandJsonContext.Default.Brand),
            },
            BrandJsonContext.Default.BrandResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes individual fields of a brand.</summary>
    /// <param name="brandId">The brand id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string brandId,
        UpdateBrand changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(brandId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(changes, BrandJsonContext.Default.UpdateBrand),
            },
            cancellationToken);
    }

    /// <summary>Replaces a brand.</summary>
    /// <param name="brandId">The brand id.</param>
    /// <param name="brand">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Unlike <see cref="UpdateAsync"/> this sends the whole brand: anything you
    /// leave out is cleared.
    /// </remarks>
    public Task ReplaceAsync(
        string brandId,
        UpdateBrand brand,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);
        ArgumentNullException.ThrowIfNull(brand);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(brandId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(brand, BrandJsonContext.Default.UpdateBrand),
            },
            cancellationToken);
    }

    /// <summary>Deletes a brand.</summary>
    /// <param name="brandId">The brand id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string brandId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brandId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(brandId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Product labels, such as «new» or «organic».
/// </summary>
/// <remarks>
/// As with brands, the path carries no tenant segment.
/// </remarks>
public sealed class LabelService
{
    private readonly EmporixHttpClient _http;

    internal LabelService(EmporixHttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    private const string BasePath = "/label/labels";

    /// <summary>Lists every label.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<Label>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest { Method = HttpMethod.Get, Path = BasePath, Auth = Defaults.Service(auth) },
            LabelJsonContext.Default.ListLabel,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a label by its id.</summary>
    /// <param name="labelId">The label id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No label exists with this id.</exception>
    public async Task<Label?> GetAsync(
        string labelId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(labelId)}",
                Auth = Defaults.Service(auth),
            },
            LabelJsonContext.Default.Label,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a label.</summary>
    /// <param name="label">The label to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        LabelCreation label,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(label, LabelJsonContext.Default.LabelCreation),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a label.</summary>
    /// <param name="labelId">The label id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string labelId,
        LabelUpdate changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(labelId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(changes, LabelJsonContext.Default.LabelUpdate),
            },
            cancellationToken);
    }

    /// <summary>Replaces a label.</summary>
    /// <param name="labelId">The label id.</param>
    /// <param name="label">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Unlike <see cref="UpdateAsync"/> this sends the whole label: anything you
    /// leave out is cleared.
    /// </remarks>
    public Task ReplaceAsync(
        string labelId,
        LabelUpdate label,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelId);
        ArgumentNullException.ThrowIfNull(label);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(labelId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(label, LabelJsonContext.Default.LabelUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a label.</summary>
    /// <param name="labelId">The label id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string labelId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(labelId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Product catalogs — the groupings a storefront selects from.
/// </summary>
public sealed class CatalogService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CatalogService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/catalog/{_tenant}/catalogs";

    /// <summary>Lists catalogs.</summary>
    /// <param name="query">An optional Emporix filter.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<Catalog>> ListAsync(
        string? query = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = query is { Length: > 0 } ? [new("q", query)] : null,
            },
            CatalogJsonContext.Default.ListCatalog,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a catalog by its id.</summary>
    /// <param name="catalogId">The catalog id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No catalog exists with this id.</exception>
    public async Task<Catalog?> GetAsync(
        string catalogId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(catalogId)}",
                Auth = Defaults.Service(auth),
            },
            CatalogJsonContext.Default.Catalog,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists every catalog that contains a given category.</summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<Catalog>> ListForCategoryAsync(
        string categoryId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/categories/{Uri.EscapeDataString(categoryId)}",
                Auth = Defaults.Service(auth),
            },
            CatalogJsonContext.Default.ListCatalog,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Creates a catalog.</summary>
    /// <param name="catalog">The catalog to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CreateCatalogResponse?> CreateAsync(
        CreateCatalog catalog,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(catalog, CatalogJsonContext.Default.CreateCatalog),
            },
            CatalogJsonContext.Default.CreateCatalogResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces a catalog, creating it when the id is unknown.
    /// </summary>
    /// <param name="catalogId">The catalog id.</param>
    /// <param name="catalog">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Emporix treats this as an upsert: an unknown id creates rather than fails.
    /// </remarks>
    public Task ReplaceAsync(
        string catalogId,
        UpdateCatalog catalog,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);
        ArgumentNullException.ThrowIfNull(catalog);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(catalogId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(catalog, CatalogJsonContext.Default.UpdateCatalog),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a catalog.</summary>
    /// <param name="catalogId">The catalog id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Unlike <see cref="ReplaceAsync"/> this leaves untouched fields alone.
    /// </remarks>
    public Task UpdateAsync(
        string catalogId,
        UpdateCatalogProperties changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(catalogId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    changes,
                    CatalogJsonContext.Default.UpdateCatalogProperties),
            },
            cancellationToken);
    }

    /// <summary>Deletes a catalog.</summary>
    /// <param name="catalogId">The catalog id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string catalogId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(catalogId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// The auth defaults every facade applies when a call states nothing.
/// </summary>
/// <remarks>
/// Kept in one place so «reads are anonymous, writes are service» does not get
/// re-decided per service, and so a deviation — the cart, which refuses both —
/// stands out.
/// </remarks>
internal static class Defaults
{
    /// <summary>Anonymous unless the call asks for something else.</summary>
    public static AuthContext Anonymous(AuthContext auth)
        => auth.Kind == AuthKind.None ? AuthContext.Anonymous() : auth;

    /// <summary>Service token unless the call asks for something else.</summary>
    public static AuthContext Service(AuthContext auth)
        => auth.Kind == AuthKind.None ? AuthContext.Service() : auth;
}
