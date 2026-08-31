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

    /// <summary>
    /// Fetches a category subtree.
    /// </summary>
    /// <param name="categoryId">The root of the subtree.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// One call returns the whole subtree — useful for building navigation
    /// without walking level by level.
    /// </remarks>
    public async Task<CategoryTree?> GetTreeAsync(
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
            CategoryJsonContext.Default.CategoryTree,
            cancellationToken).ConfigureAwait(false);
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
}
