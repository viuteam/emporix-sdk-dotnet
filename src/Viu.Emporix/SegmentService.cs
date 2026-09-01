using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.CustomerSegmentModels;

namespace Viu.Emporix;

/// <summary>
/// Customer segments — who gets which prices and which catalogue.
/// </summary>
/// <remarks>
/// <para>
/// A segment holds two kinds of membership and they are configured separately:
/// <see cref="Customers"/> says who is in it, <see cref="Items"/> says what they
/// may see. A segment with customers but no items grants nothing; one with items
/// but no customers reaches nobody.
/// </para>
/// <para>
/// <see cref="MatchAsync"/> is the read a storefront makes: given a shopper and
/// some items, which segments apply.
/// </para>
/// </remarks>
public sealed class SegmentService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal SegmentService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/customer-segment/{_tenant}/segments";

    /// <summary>Who belongs to a segment.</summary>
    /// <param name="segmentId">The segment.</param>
    public SegmentCustomerOperations Customers(string segmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);
        return new SegmentCustomerOperations(_http, $"{BasePath}/{Uri.EscapeDataString(segmentId)}/customers");
    }

    /// <summary>What a segment grants access to.</summary>
    /// <param name="segmentId">The segment.</param>
    public SegmentItemOperations Items(string segmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);
        return new SegmentItemOperations(_http, $"{BasePath}/{Uri.EscapeDataString(segmentId)}/items");
    }

    /// <summary>Fetches a segment.</summary>
    /// <param name="segmentId">The segment id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SegmentResponse?> GetAsync(
        string segmentId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(segmentId)}",
                Auth = Defaults.Service(auth),
            },
            SegmentJsonContext.Default.SegmentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of segments.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<SegmentResponse>> ListAsync(
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
            SegmentJsonContext.Default.ListSegmentResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches segments.</summary>
    /// <param name="query">The Emporix query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<SegmentResponse>> SearchAsync(
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
                    new SegmentsSearch { Q = query },
                    SegmentJsonContext.Default.SegmentsSearch),
                Idempotent = true,
            },
            SegmentJsonContext.Default.ListSegmentResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves which segments apply to a shopper and some items.</summary>
    /// <param name="request">The customer, the items and the site.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The storefront read: it decides which prices and which catalogue a
    /// visitor sees. A <c>POST</c> because the item list is a body, but declared
    /// repeatable — it changes nothing.
    /// </remarks>
    public async Task<IReadOnlyList<SegmentResponse>> MatchAsync(
        Match request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/match",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(request, SegmentJsonContext.Default.Match),
                Idempotent = true,
            },
            SegmentJsonContext.Default.ListSegmentResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Creates a segment.</summary>
    /// <param name="segment">The segment to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SegmentResponse?> CreateAsync(
        SegmentCreation segment,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segment);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    segment,
                    SegmentJsonContext.Default.SegmentCreation),
            },
            SegmentJsonContext.Default.SegmentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a segment.</summary>
    /// <param name="segmentId">The segment id.</param>
    /// <param name="segment">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Its members and items are untouched — this changes the segment, not who
    /// is in it.
    /// </remarks>
    public Task ReplaceAsync(
        string segmentId,
        SegmentUpdate segment,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);
        ArgumentNullException.ThrowIfNull(segment);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(segmentId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    segment,
                    SegmentJsonContext.Default.SegmentUpdate),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a segment.</summary>
    /// <param name="segmentId">The segment id.</param>
    /// <param name="operations">The JSON Patch operations to apply.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string segmentId,
        IEnumerable<PatchOperation> operations,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);
        ArgumentNullException.ThrowIfNull(operations);

        List<PatchOperation> body = [.. operations];
        ArgumentOutOfRangeException.ThrowIfZero(body.Count, nameof(operations));

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(segmentId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    body,
                    SegmentJsonContext.Default.ListPatchOperation),
            },
            cancellationToken);
    }

    /// <summary>Deletes a segment.</summary>
    /// <param name="segmentId">The segment id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Everyone in it loses whatever it granted, which for a pricing segment
    /// means they start seeing list prices.
    /// </remarks>
    public Task DeleteAsync(
        string segmentId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(segmentId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Creates several segments in one call.</summary>
    /// <param name="segments">The segments to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Each entry reports its own outcome; a 200 does not mean all landed.</remarks>
    public Task<IReadOnlyList<BulkAssignmentResponse>> CreateManyAsync(
        IEnumerable<SegmentCreation> segments,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Post, segments, auth, cancellationToken);

    /// <summary>Replaces several segments in one call.</summary>
    /// <param name="segments">The segments in their new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<BulkAssignmentResponse>> UpdateManyAsync(
        IEnumerable<SegmentCreation> segments,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Put, segments, auth, cancellationToken);

    /// <summary>Deletes several segments in one call.</summary>
    /// <param name="segmentIds">The segments to delete.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<BulkAssignmentResponse>> DeleteManyAsync(
        IEnumerable<string> segmentIds,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segmentIds);

        List<string> ids = [.. segmentIds];

        return ids.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Delete,
                    Path = $"{BasePath}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(ids, SegmentJsonContext.Default.ListString),
                },
                SegmentJsonContext.Default.ListBulkAssignmentResponse,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Lists every item any segment grants, across the tenant.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ItemAssignmentResponse>> ListAllItemsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/items",
                Auth = Defaults.Service(auth),
            },
            SegmentJsonContext.Default.ListItemAssignmentResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the category trees segments may grant.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>What is available to pick from when granting a whole tree.</remarks>
    public async Task<CategoryTreeResponse?> ListCategoryTreesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/items/category-trees",
                Auth = Defaults.Service(auth),
            },
            SegmentJsonContext.Default.CategoryTreeResponse,
            cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<BulkAssignmentResponse>> BulkAsync(
        HttpMethod method,
        IEnumerable<SegmentCreation> segments,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segments);

        List<SegmentCreation> body = [.. segments];

        return body.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = method,
                    Path = $"{BasePath}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        SegmentJsonContext.Default.ListSegmentCreation),
                },
                SegmentJsonContext.Default.ListBulkAssignmentResponse,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
    ];
}

/// <summary>
/// Who belongs to one segment.
/// </summary>
/// <remarks>
/// Reached through <see cref="SegmentService.Customers"/>. In B2B a membership
/// can be narrowed to one legal entity, which is why several operations take
/// both a customer and an entity: the same person can be in the segment when
/// buying for one company and not for another.
/// </remarks>
public sealed class SegmentCustomerOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal SegmentCustomerOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Fetches one page of members.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<CustomerAssignmentResponse>> ListAsync(
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
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Query = SegmentService.Paging(pageNumber, pageSize),
            },
            SegmentJsonContext.Default.ListCustomerAssignmentResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches the members.</summary>
    /// <param name="query">The Emporix query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<CustomerAssignmentResponse>> SearchAsync(
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
                Path = $"{_basePath}/search",
                Auth = Defaults.Service(auth),
                Query = SegmentService.Paging(pageNumber, pageSize),
                Content = EmporixJsonContent.Create(
                    new SegmentsSearch { Q = query },
                    SegmentJsonContext.Default.SegmentsSearch),
                Idempotent = true,
            },
            SegmentJsonContext.Default.ListCustomerAssignmentResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one membership.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="legalEntityId">The legal entity, where the membership is scoped to one.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CustomerAssignmentResponse?> GetAsync(
        string customerId,
        string? legalEntityId = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = PathFor(customerId, legalEntityId),
                Auth = Defaults.Service(auth),
            },
            SegmentJsonContext.Default.CustomerAssignmentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Puts a customer into the segment, or updates their membership.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="assignment">The membership.</param>
    /// <param name="legalEntityId">The legal entity, to scope the membership to one.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Repeating this changes nothing beyond the first call.</remarks>
    public Task UpsertAsync(
        string customerId,
        CustomerAssignmentUpsert assignment,
        string? legalEntityId = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(assignment);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = PathFor(customerId, legalEntityId),
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    assignment,
                    SegmentJsonContext.Default.CustomerAssignmentUpsert),
            },
            cancellationToken);
    }

    /// <summary>Takes a customer out of the segment.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="legalEntityId">The legal entity, where the membership was scoped to one.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string customerId,
        string? legalEntityId = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = PathFor(customerId, legalEntityId),
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Adds or updates several memberships in one call.</summary>
    /// <param name="assignments">The memberships.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<BulkAssignmentResponse>> UpsertManyAsync(
        IEnumerable<CustomerAssignmentUpsertBulk> assignments,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        List<CustomerAssignmentUpsertBulk> body = [.. assignments];

        return body.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Put,
                    Path = $"{_basePath}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        SegmentJsonContext.Default.ListCustomerAssignmentUpsertBulk),
                },
                SegmentJsonContext.Default.ListBulkAssignmentResponse,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Removes several memberships in one call.</summary>
    /// <param name="assignments">Which memberships to remove.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<BulkAssignmentResponse>> DeleteManyAsync(
        IEnumerable<CustomerAssignmentUpsertBulk> assignments,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        List<CustomerAssignmentUpsertBulk> body = [.. assignments];

        return body.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Delete,
                    Path = $"{_basePath}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        SegmentJsonContext.Default.ListCustomerAssignmentUpsertBulk),
                },
                SegmentJsonContext.Default.ListBulkAssignmentResponse,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// Builds the path, which gains a segment when the membership is scoped to
    /// one legal entity.
    /// </summary>
    private string PathFor(string customerId, string? legalEntityId)
        => legalEntityId is { Length: > 0 }
            ? $"{_basePath}/{Uri.EscapeDataString(customerId)}/{Uri.EscapeDataString(legalEntityId)}"
            : $"{_basePath}/{Uri.EscapeDataString(customerId)}";
}

/// <summary>
/// What one segment grants access to.
/// </summary>
/// <remarks>
/// Reached through <see cref="SegmentService.Items"/>. An item is addressed by
/// its kind and its id — a product, a category, a whole tree — so every
/// operation here takes the type as well.
/// </remarks>
public sealed class SegmentItemOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal SegmentItemOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Fetches one page of granted items.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<ItemAssignmentResponse>> ListAsync(
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
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Query = SegmentService.Paging(pageNumber, pageSize),
            },
            SegmentJsonContext.Default.ListItemAssignmentResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches the granted items.</summary>
    /// <param name="query">The Emporix query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<ItemAssignmentResponse>> SearchAsync(
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
                Path = $"{_basePath}/search",
                Auth = Defaults.Service(auth),
                Query = SegmentService.Paging(pageNumber, pageSize),
                Content = EmporixJsonContent.Create(
                    new SegmentsSearch { Q = query },
                    SegmentJsonContext.Default.SegmentsSearch),
                Idempotent = true,
            },
            SegmentJsonContext.Default.ListItemAssignmentResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one granted item.</summary>
    /// <param name="type">The item kind, for example a product or a category.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ItemAssignmentResponse?> GetAsync(
        string type,
        string itemId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(itemId)}",
                Auth = Defaults.Service(auth),
            },
            SegmentJsonContext.Default.ItemAssignmentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Grants an item, or updates the grant.</summary>
    /// <param name="type">The item kind.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="assignment">The grant.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpsertAsync(
        string type,
        string itemId,
        ItemAssignmentUpsert assignment,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(assignment);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(itemId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    assignment,
                    SegmentJsonContext.Default.ItemAssignmentUpsert),
            },
            cancellationToken);
    }

    /// <summary>Withdraws a granted item.</summary>
    /// <param name="type">The item kind.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Members of the segment stop seeing it, which for a catalogue segment
    /// means the product disappears from their storefront.
    /// </remarks>
    public Task DeleteAsync(
        string type,
        string itemId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(itemId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Grants several items of one kind in one call.</summary>
    /// <param name="type">The item kind.</param>
    /// <param name="assignments">The grants.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<BulkAssignmentResponse>> UpsertManyAsync(
        string type,
        IEnumerable<ItemAssignmentUpsertBulk> assignments,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Put, type, assignments, auth, cancellationToken);

    /// <summary>Withdraws several items of one kind in one call.</summary>
    /// <param name="type">The item kind.</param>
    /// <param name="assignments">Which grants to withdraw.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<BulkAssignmentResponse>> DeleteManyAsync(
        string type,
        IEnumerable<ItemAssignmentUpsertBulk> assignments,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Delete, type, assignments, auth, cancellationToken);

    private async Task<IReadOnlyList<BulkAssignmentResponse>> BulkAsync(
        HttpMethod method,
        string type,
        IEnumerable<ItemAssignmentUpsertBulk> assignments,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(assignments);

        List<ItemAssignmentUpsertBulk> body = [.. assignments];

        return body.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = method,
                    Path = $"{_basePath}/{Uri.EscapeDataString(type)}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        SegmentJsonContext.Default.ListItemAssignmentUpsertBulk),
                },
                SegmentJsonContext.Default.ListBulkAssignmentResponse,
                cancellationToken).ConfigureAwait(false) ?? [];
    }
}
