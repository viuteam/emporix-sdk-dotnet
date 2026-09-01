using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.CustomerManagementModels;

namespace Viu.Emporix;

/// <summary>
/// Legal entities — the companies a B2B tenant sells to.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see cref="OrderService.ListForLegalEntityAsync"/> has been
/// pointing at. A legal entity is the buying organisation; people act for it
/// through a contact assignment, and it receives at one or more locations.
/// </para>
/// <para>
/// Entities can nest — a group with subsidiaries — which is what
/// <see cref="GetParentHierarchyAsync"/> walks.
/// </para>
/// </remarks>
public sealed class LegalEntityService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal LegalEntityService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/customer-management/{_tenant}/legal-entities";

    /// <summary>Fetches a legal entity.</summary>
    /// <param name="legalEntityId">The legal entity id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<LegalEntity?> GetAsync(
        string legalEntityId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalEntityId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(legalEntityId)}",
                Auth = Defaults.Service(auth),
            },
            LegalEntityJsonContext.Default.LegalEntity,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of legal entities.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<LegalEntity>> ListAsync(
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
                Query = Paging(pageNumber, pageSize),
            },
            LegalEntityJsonContext.Default.ListLegalEntity,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches legal entities.</summary>
    /// <param name="query">The Emporix query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A <c>POST</c> because the query does not fit in an address, but declared
    /// repeatable: it only reads.
    /// </remarks>
    public async Task<PaginatedItems<LegalEntity>> SearchAsync(
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
                Auth = Defaults.Service(auth),
                Query = Paging(pageNumber, pageSize),
                Content = EmporixJsonContent.Create(
                    new QParam { Q = query },
                    LegalEntityJsonContext.Default.QParam),
                Idempotent = true,
            },
            LegalEntityJsonContext.Default.ListLegalEntity,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Walks an entity's parents up to the root.</summary>
    /// <param name="legalEntityId">The legal entity id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Terms and approvals can be set on a parent and inherited, so «which
    /// entity decides» is a question about the hierarchy rather than about the
    /// entity in hand.
    /// </remarks>
    public async Task<IReadOnlyList<LegalEntity>> GetParentHierarchyAsync(
        string legalEntityId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalEntityId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(legalEntityId)}/parent-hierarchy",
                Auth = Defaults.Service(auth),
            },
            LegalEntityJsonContext.Default.ListLegalEntity,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Creates a legal entity.</summary>
    /// <param name="legalEntity">The entity to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceId?> CreateAsync(
        LegalEntityCreate legalEntity,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legalEntity);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    legalEntity,
                    LegalEntityJsonContext.Default.LegalEntityCreate),
            },
            LegalEntityJsonContext.Default.ResourceId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a legal entity.</summary>
    /// <param name="legalEntityId">The legal entity id.</param>
    /// <param name="legalEntity">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Account limits and restrictions live here, so this is also how a
    /// company's credit line changes.
    /// </remarks>
    public Task ReplaceAsync(
        string legalEntityId,
        LegalEntityUpdate legalEntity,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalEntityId);
        ArgumentNullException.ThrowIfNull(legalEntity);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(legalEntityId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    legalEntity,
                    LegalEntityJsonContext.Default.LegalEntityUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a legal entity.</summary>
    /// <param name="legalEntityId">The legal entity id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Its orders remain and keep pointing at an entity that no longer resolves,
    /// so this is rarely the right call for a company that has ever bought
    /// anything.
    /// </remarks>
    public Task DeleteAsync(
        string legalEntityId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalEntityId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(legalEntityId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
    ];
}

/// <summary>
/// Contact assignments — who may act for which legal entity.
/// </summary>
/// <remarks>
/// The link between a person and a company, carrying what that person is
/// allowed to do. A customer with no assignment can buy for themselves but not
/// on the company's account.
/// </remarks>
public sealed class ContactAssignmentService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal ContactAssignmentService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/customer-management/{_tenant}/contact-assignments";

    /// <summary>Fetches a contact assignment.</summary>
    /// <param name="contactAssignmentId">The assignment id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ContactAssignment?> GetAsync(
        string contactAssignmentId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contactAssignmentId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(contactAssignmentId)}",
                Auth = Defaults.Service(auth),
            },
            LegalEntityJsonContext.Default.ContactAssignment,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of contact assignments.</summary>
    /// <param name="query">An optional Emporix filter, for example by legal entity.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<ContactAssignment>> ListAsync(
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
            .. LegalEntityService.Paging(pageNumber, pageSize),
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
            LegalEntityJsonContext.Default.ListContactAssignment,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Assigns a person to a legal entity.</summary>
    /// <param name="assignment">Who, to which entity, with what rights.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceId?> CreateAsync(
        ContactAssignmentCreate assignment,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    assignment,
                    LegalEntityJsonContext.Default.ContactAssignmentCreate),
            },
            LegalEntityJsonContext.Default.ResourceId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a contact assignment.</summary>
    /// <param name="contactAssignmentId">The assignment id.</param>
    /// <param name="assignment">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// This is how someone's rights over a company account change, so it takes
    /// effect on their next call rather than their next sign-in.
    /// </remarks>
    public Task ReplaceAsync(
        string contactAssignmentId,
        ContactAssignmentUpdate assignment,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contactAssignmentId);
        ArgumentNullException.ThrowIfNull(assignment);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(contactAssignmentId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    assignment,
                    LegalEntityJsonContext.Default.ContactAssignmentUpdate),
            },
            cancellationToken);
    }

    /// <summary>Removes a person from a legal entity.</summary>
    /// <param name="contactAssignmentId">The assignment id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The customer account survives; only the right to act for the company
    /// goes. What to call when someone leaves.
    /// </remarks>
    public Task DeleteAsync(
        string contactAssignmentId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contactAssignmentId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(contactAssignmentId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Locations — where a legal entity receives.
/// </summary>
/// <remarks>
/// A company can have many: a head office that is billed and several sites that
/// are delivered to. Distinct from a customer's own addresses, which belong to
/// a person rather than to an organisation.
/// </remarks>
public sealed class LocationService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal LocationService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/customer-management/{_tenant}/locations";

    /// <summary>Fetches a location.</summary>
    /// <param name="locationId">The location id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Location?> GetAsync(
        string locationId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(locationId)}",
                Auth = Defaults.Service(auth),
            },
            LegalEntityJsonContext.Default.Location,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of locations.</summary>
    /// <param name="query">An optional Emporix filter, for example by legal entity.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<Location>> ListAsync(
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
            .. LegalEntityService.Paging(pageNumber, pageSize),
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
            LegalEntityJsonContext.Default.ListLocation,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a location.</summary>
    /// <param name="location">The location to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceId?> CreateAsync(
        LocationCreate location,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    location,
                    LegalEntityJsonContext.Default.LocationCreate),
            },
            LegalEntityJsonContext.Default.ResourceId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a location.</summary>
    /// <param name="locationId">The location id.</param>
    /// <param name="location">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string locationId,
        LocationUpdate location,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        ArgumentNullException.ThrowIfNull(location);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(locationId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    location,
                    LegalEntityJsonContext.Default.LocationUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a location.</summary>
    /// <param name="locationId">The location id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Orders already shipped there keep the address they were shipped to.</remarks>
    public Task DeleteAsync(
        string locationId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(locationId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}
