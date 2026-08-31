using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.CategoryModels;

namespace Viu.Emporix;

/// <summary>Paging options for the category list and search calls.</summary>
public sealed class CategoryPageOptions
{
    /// <summary>The page number. Emporix counts from 1.</summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>The page size. Defaults to 60, as stated in the Emporix specification.</summary>
    public int PageSize { get; init; } = 60;

    /// <summary>Requests the total number of matches. Costs Emporix a second query.</summary>
    public bool IncludeTotalCount { get; init; }
}

/// <summary>
/// The Emporix category tree.
/// </summary>
/// <remarks>
/// Read calls default to an anonymous token; write calls require a service
/// token. Assignments — which products and subcategories sit in a category —
/// live under <see cref="Assignments"/>.
/// </remarks>
public sealed class CategoryService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CategoryService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
        Assignments = new CategoryAssignmentOperations(http, _tenant);
    }

    private string BasePath => $"/category/{_tenant}/categories";

    /// <summary>
    /// What sits inside a category: products and subcategories.
    /// </summary>
    /// <remarks>
    /// Grouped into their own type because Emporix treats them as a resource of
    /// their own, under the category's path.
    /// </remarks>
    public CategoryAssignmentOperations Assignments { get; }

    /// <summary>Fetches a category by its id.</summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No category exists with this id.</exception>
    public async Task<Category?> GetAsync(
        string categoryId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(categoryId)}",
                Auth = Anonymous(auth),
            },
            CategoryJsonContext.Default.Category,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of categories.</summary>
    /// <param name="options">Page number, page size and total count.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaginatedItems<Category>> ListAsync(
        CategoryPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ListPageAsync(options ?? new CategoryPageOptions(), query: null, auth, cancellationToken);

    /// <summary>Walks every category, fetching each page only when it is needed.</summary>
    /// <param name="pageSize">The page size; 60 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    public IAsyncEnumerable<Category> ListAllAsync(
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => PaginatedItems.EnumerateAllAsync(
            (pageNumber, token) => ListAsync(
                new CategoryPageOptions { PageNumber = pageNumber, PageSize = pageSize },
                auth,
                token),
            startPage: 1,
            cancellationToken);

    /// <summary>Searches categories with an Emporix filter.</summary>
    /// <param name="query">The filter in Emporix' query language, for example <c>code:shoes</c>.</param>
    /// <param name="options">Page number, page size and total count.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaginatedItems<Category>> SearchAsync(
        string query,
        CategoryPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return ListPageAsync(options ?? new CategoryPageOptions(), query, auth, cancellationToken);
    }

    /// <summary>Lists a category's direct children.</summary>
    /// <param name="categoryId">The parent category.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// One level down, not the whole subtree — for the whole tree of a root
    /// category use <see cref="GetTreeAsync"/>.
    /// </remarks>
    public async Task<IReadOnlyList<Category>> ListSubcategoriesAsync(
        string categoryId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(categoryId)}/subcategories",
                Auth = Anonymous(auth),
            },
            CategoryJsonContext.Default.ListCategory,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Lists a category's parents.</summary>
    /// <param name="categoryId">The category.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The path back to the root — what a breadcrumb is built from.</remarks>
    public async Task<IReadOnlyList<Category>> ListParentsAsync(
        string categoryId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(categoryId)}/parents",
                Auth = Anonymous(auth),
            },
            CategoryJsonContext.Default.ListCategory,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches the category tree of a root category.</summary>
    /// <param name="rootCategoryId">The root category.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Only a root category has a tree: asking for one lower in the hierarchy
    /// is not supported by Emporix. One call returns the whole thing, which is
    /// what navigation is built from.
    /// </remarks>
    public async Task<CategoryTree?> GetTreeAsync(
        string rootCategoryId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootCategoryId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/category/{_tenant}/category-trees/{Uri.EscapeDataString(rootCategoryId)}",
                Auth = Anonymous(auth),
            },
            CategoryJsonContext.Default.CategoryTree,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the category trees.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CategoryTree>> ListTreesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/category/{_tenant}/category-trees",
                Auth = Anonymous(auth),
            },
            CategoryJsonContext.Default.ListCategoryTree,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches several category trees at once.</summary>
    /// <param name="rootCategoryIds">The root categories.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Sent as a <c>POST</c> because the id list does not fit in an address, but
    /// declared repeatable: it only reads.
    /// </remarks>
    public async Task<IReadOnlyList<CategoryTree>> SearchTreesAsync(
        IEnumerable<string> rootCategoryIds,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootCategoryIds);

        List<string> ids = [.. rootCategoryIds];

        return ids.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Post,
                    Path = $"/category/{_tenant}/category-trees/search",
                    Auth = Anonymous(auth),
                    Content = EmporixJsonContent.Create(
                        new CategoryTreeSearchRequest { CategoryIds = ids },
                        CategoryJsonContext.Default.CategoryTreeSearchRequest),
                    Idempotent = true,
                },
                CategoryJsonContext.Default.ListCategoryTree,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Rebuilds a category tree.</summary>
    /// <param name="rootCategoryId">The root category.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// An administrative repair for when the stored tree has drifted from the
    /// assignments it is built from.
    /// </remarks>
    public Task RebuildTreeAsync(
        string rootCategoryId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootCategoryId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/category/{_tenant}/category-trees"
                    + $"/{Uri.EscapeDataString(rootCategoryId)}/rebuild",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Searches categories with an Emporix query.</summary>
    /// <param name="query">The query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Sent as a <c>POST</c> because a query can outgrow an address, but declared
    /// repeatable: it only reads.
    /// </remarks>
    public async Task<IReadOnlyList<Category>> SearchByQueryAsync(
        string query,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Anonymous(auth),
                Query =
                [
                    new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
                    new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                ],
                Content = EmporixJsonContent.Create(
                    new SearchRequest { Q = query },
                    CategoryJsonContext.Default.SearchRequest),
                Idempotent = true,
            },
            CategoryJsonContext.Default.ListCategory,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches several categories by id.</summary>
    /// <param name="categoryIds">The categories to fetch.</param>
    /// <param name="chunkSize">How many ids per request. Defaults to 100.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// There is no fetch-by-id-list endpoint, so this builds an <c>id:(…)</c>
    /// query and splits it — an unbounded list would otherwise outgrow what the
    /// search accepts.
    /// </remarks>
    public async Task<IReadOnlyList<Category>> GetManyByIdAsync(
        IEnumerable<string> categoryIds,
        int chunkSize = 100,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        string[] ids = [.. categoryIds];

        if (ids.Length == 0)
        {
            return [];
        }

        List<Category> all = [];

        foreach (string[] chunk in ids.Chunk(chunkSize))
        {
            all.AddRange(await SearchByQueryAsync(
                $"id:({string.Join(',', chunk)})",
                pageSize: chunk.Length,
                auth: auth,
                cancellationToken: cancellationToken).ConfigureAwait(false));
        }

        return all;
    }

    /// <summary>Replaces a category.</summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="category">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Unlike <see cref="UpdateAsync"/> this sends the whole category: anything
    /// you leave out is cleared.
    /// </remarks>
    public Task ReplaceAsync(
        string categoryId,
        CategoryUpdateRequest category,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentNullException.ThrowIfNull(category);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(categoryId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    category,
                    CategoryJsonContext.Default.CategoryUpdateRequest),
            },
            cancellationToken);
    }

    /// <summary>Creates a category.</summary>
    /// <param name="category">The category to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixInsufficientScopeException">The token lacks <c>category.category_manage</c>.</exception>
    public async Task<CategoryIdResponse?> CreateAsync(
        CategoryCreateRequest category,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Service(auth),
                Content = EmporixJsonContent.Create(
                    category,
                    CategoryJsonContext.Default.CategoryCreateRequest),
            },
            CategoryJsonContext.Default.CategoryIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes individual fields of a category.</summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string categoryId,
        CategoryPartialUpdateRequest changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(categoryId)}",
                Auth = Service(auth),
                Content = EmporixJsonContent.Create(
                    changes,
                    CategoryJsonContext.Default.CategoryPartialUpdateRequest),
            },
            cancellationToken);
    }

    /// <summary>Deletes a category.</summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string categoryId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(categoryId)}",
                Auth = Service(auth),
            },
            cancellationToken);
    }

    private async Task<PaginatedItems<Category>> ListPageAsync(
        CategoryPageOptions options,
        string? query,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PageSize, 1);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("pageNumber", options.PageNumber.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", options.PageSize.ToString(CultureInfo.InvariantCulture)),
        ];

        if (query is { Length: > 0 })
        {
            parameters.Add(new KeyValuePair<string, string?>("q", query));
        }

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Anonymous(auth),
                Query = parameters,
                Headers = options.IncludeTotalCount ? [new("X-Total-Count", "true")] : null,
            },
            CategoryJsonContext.Default.ListCategory,
            options.PageNumber,
            options.PageSize,
            cancellationToken).ConfigureAwait(false);
    }

    private static AuthContext Anonymous(AuthContext auth)
        => auth.Kind == AuthKind.None ? AuthContext.Anonymous() : auth;

    private static AuthContext Service(AuthContext auth)
        => auth.Kind == AuthKind.None ? AuthContext.Service() : auth;
}

/// <summary>
/// What sits inside a category: products and subcategories.
/// </summary>
public sealed class CategoryAssignmentOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CategoryAssignmentOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string PathFor(string categoryId)
        => $"/category/{_tenant}/categories/{Uri.EscapeDataString(categoryId)}/assignments";

    /// <summary>Lists everything assigned to a category.</summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CategoryAssignment>> ListAsync(
        string categoryId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = PathFor(categoryId),
                Auth = auth.Kind == AuthKind.None ? AuthContext.Anonymous() : auth,
            },
            CategoryJsonContext.Default.ListCategoryAssignment,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Assigns a product or subcategory to a category.</summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="assignment">What to assign.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        string categoryId,
        CategoryAssignment assignment,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentNullException.ThrowIfNull(assignment);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = PathFor(categoryId),
                Auth = auth.Kind == AuthKind.None ? AuthContext.Service() : auth,
                Content = EmporixJsonContent.Create(
                    assignment,
                    CategoryJsonContext.Default.CategoryAssignment),
            },
            cancellationToken);
    }

    /// <summary>Removes an assignment from a category.</summary>
    /// <param name="categoryId">The category id.</param>
    /// <param name="assignmentId">The id of the assignment.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string categoryId,
        string assignmentId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{PathFor(categoryId)}/{Uri.EscapeDataString(assignmentId)}",
                Auth = auth.Kind == AuthKind.None ? AuthContext.Service() : auth,
            },
            cancellationToken);
    }

    /// <summary>Creates several assignments in one call.</summary>
    /// <param name="categoryId">The category.</param>
    /// <param name="assignments">What to assign.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<BulkAssignmentResponse>> CreateManyAsync(
        string categoryId,
        BulkAssignmentRequest assignments,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentNullException.ThrowIfNull(assignments);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{PathFor(categoryId)}/bulk",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    assignments,
                    CategoryJsonContext.Default.BulkAssignmentRequest),
            },
            CategoryJsonContext.Default.ListBulkAssignmentResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Removes every assignment from a category.</summary>
    /// <param name="categoryId">The category.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Empties the category; the assigned products themselves are untouched.</remarks>
    public Task RemoveAllAsync(
        string categoryId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = PathFor(categoryId),
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Assigns something to a category, addressed by what it is.</summary>
    /// <param name="categoryId">The category.</param>
    /// <param name="referenceId">The reference, for example a product id.</param>
    /// <param name="assignment">What to assign.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Addressing by reference rather than by assignment id means a caller who
    /// knows the product does not first have to look up the assignment. Repeating
    /// the call updates rather than duplicates.
    /// </remarks>
    public Task UpsertByReferenceAsync(
        string categoryId,
        string referenceId,
        AssignmentRequest assignment,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);
        ArgumentNullException.ThrowIfNull(assignment);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{PathFor(categoryId)}/references/{Uri.EscapeDataString(referenceId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    assignment,
                    CategoryJsonContext.Default.AssignmentRequest),
            },
            cancellationToken);
    }

    /// <summary>Removes an assignment from a category, addressed by reference.</summary>
    /// <param name="categoryId">The category.</param>
    /// <param name="referenceId">The reference, for example a product id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RemoveByReferenceAsync(
        string categoryId,
        string referenceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{PathFor(categoryId)}/references/{Uri.EscapeDataString(referenceId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Assigns several things to a category by reference in one call.</summary>
    /// <param name="categoryId">The category.</param>
    /// <param name="assignments">What to assign.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<BulkAssignmentResponse>> BulkUpsertByReferenceAsync(
        string categoryId,
        BulkAssignmentUpsertRequest assignments,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentNullException.ThrowIfNull(assignments);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{PathFor(categoryId)}/references/bulk",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    assignments,
                    CategoryJsonContext.Default.BulkAssignmentUpsertRequest),
            },
            CategoryJsonContext.Default.ListBulkAssignmentResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Lists the categories something is assigned to.</summary>
    /// <param name="referenceId">The reference, for example a product id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The reverse lookup: «which categories is this product in», without
    /// walking the tree.
    /// </remarks>
    public async Task<IReadOnlyList<Category>> ListCategoriesByReferenceAsync(
        string referenceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/category/{_tenant}/assignments/references"
                    + $"/{Uri.EscapeDataString(referenceId)}",
                Auth = auth.Kind is AuthKind.None ? AuthContext.Anonymous() : auth,
            },
            CategoryJsonContext.Default.ListCategory,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Removes something from every category it is assigned to.</summary>
    /// <param name="referenceId">The reference, for example a product id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>What to call when a product is retired: it leaves the catalog everywhere at once.</remarks>
    public Task RemoveAllByReferenceAsync(
        string referenceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"/category/{_tenant}/assignments/references"
                    + $"/{Uri.EscapeDataString(referenceId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists what is assigned to a category, of one kind.</summary>
    /// <param name="categoryId">The category.</param>
    /// <param name="referenceType">Which kind to keep, for example products or categories.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The referenced ids, in the order Emporix returned them.</returns>
    /// <remarks>
    /// A category holds products and subcategories in the same assignment list,
    /// so «what products are in this category» means reading the list and
    /// keeping one kind. Only the ids come back: fetching the products
    /// themselves is a call to another service, and doing it here would tie the
    /// two together for a convenience.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ListAssignedIdsAsync(
        string categoryId,
        CategoryAssignmentRefQueryDocumentType referenceType,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CategoryAssignment> assignments =
            await ListAsync(categoryId, auth, cancellationToken).ConfigureAwait(false);

        return
        [
            .. assignments
                .Where(a => a.Ref?.Type == referenceType && a.Ref.Id is { Length: > 0 })
                .Select(a => a.Ref!.Id),
        ];
    }
}
