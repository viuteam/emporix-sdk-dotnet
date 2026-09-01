using Microsoft.Extensions.Options;
using Viu.Emporix.ShoppingListModels;

namespace Viu.Emporix;

/// <summary>
/// Shopping lists — what a customer means to buy later.
/// </summary>
/// <remarks>
/// <para>
/// A customer has several lists, each with a name, and a list is addressed by
/// that name rather than by an id. <c>default</c> is the usual one.
/// </para>
/// <para>
/// Every operation exists twice over: a customer acting on their own lists, and
/// an employee acting on someone else's. Which one happens is decided by the
/// token, not by the call — with <c>shoppinglist.shoppinglist_manage</c> the
/// customer is the one named in the request; without it, the one the token
/// belongs to. The methods here are named after the two cases so that the
/// distinction is visible at the call site.
/// </para>
/// </remarks>
public sealed class ShoppingListService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal ShoppingListService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/shoppinglist/{_tenant}/shopping-lists";

    /// <summary>Creates a list for the customer the token belongs to.</summary>
    /// <param name="list">The list's name and its items.</param>
    /// <param name="auth">
    /// The customer's token. A service token has no customer to attribute the
    /// list to — use <see cref="CreateForCustomerAsync"/> instead.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The customer id the list was filed under.</returns>
    public async Task<string?> CreateAsync(
        OwnShoppingListCreateRequest list,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);

        Response? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    list, ShoppingListJsonContext.Default.OwnShoppingListCreateRequest),
            },
            ShoppingListJsonContext.Default.Response,
            cancellationToken).ConfigureAwait(false);

        return response?.Id;
    }

    /// <summary>Creates a list on a named customer's behalf.</summary>
    /// <param name="list">The customer id, the list's name and its items.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The customer id the list was filed under.</returns>
    /// <remarks>
    /// Needs <c>shoppinglist.shoppinglist_manage</c>. Without that scope the
    /// <c>customerId</c> in the request is ignored and the token's own customer
    /// is used instead — so a missing scope silently writes to the wrong list
    /// rather than failing.
    /// </remarks>
    public async Task<string?> CreateForCustomerAsync(
        EmployeeShoppingListCreateRequest list,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);

        Response? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    list, ShoppingListJsonContext.Default.EmployeeShoppingListCreateRequest),
            },
            ShoppingListJsonContext.Default.Response,
            cancellationToken).ConfigureAwait(false);

        return response?.Id;
    }

    /// <summary>Lists shopping lists.</summary>
    /// <param name="name">One list by name. All of them when omitted.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// With <c>shoppinglist.shoppinglist_read</c> this returns every customer's
    /// lists and <paramref name="name"/> has no effect; without it, only the
    /// token holder's own.
    /// </remarks>
    public async Task<IReadOnlyList<GetShoppingList>> ListAsync(
        string? name = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        List<KeyValuePair<string, string?>> query = [];

        if (!string.IsNullOrWhiteSpace(name))
        {
            query.Add(new("name", name));
        }

        GetShoppingLists? lists = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = query,
                Idempotent = true,
            },
            ShoppingListJsonContext.Default.GetShoppingLists,
            cancellationToken).ConfigureAwait(false);

        return lists is null ? [] : [.. lists];
    }

    /// <summary>Fetches one customer's shopping lists.</summary>
    /// <param name="customerId">Whose lists.</param>
    /// <param name="name">One list by name. All of them when omitted.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The customer, with each list under its own name in
    /// <c>AdditionalProperties</c> — the specification models the lists as
    /// additional properties keyed by name, not as an array.
    /// </returns>
    public async Task<GetShoppingList?> GetAsync(
        string customerId,
        string? name = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        List<KeyValuePair<string, string?>> query = [];

        if (!string.IsNullOrWhiteSpace(name))
        {
            query.Add(new("name", name));
        }

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(customerId)}",
                Auth = Defaults.Service(auth),
                Query = query,
                Idempotent = true,
            },
            ShoppingListJsonContext.Default.GetShoppingList,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a customer's shopping list.</summary>
    /// <param name="customerId">Whose list.</param>
    /// <param name="list">The list, whole — this replaces rather than merges.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string customerId,
        OwnShoppingListUpdateRequest list,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(list);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(customerId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    list, ShoppingListJsonContext.Default.OwnShoppingListUpdateRequest),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Deletes a customer's shopping list.</summary>
    /// <param name="customerId">Whose list.</param>
    /// <param name="name">Which list. All of them when omitted.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Omitting <paramref name="name"/> deletes every list the customer has, not
    /// merely the default one.
    /// </remarks>
    public Task DeleteAsync(
        string customerId,
        string? name = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        List<KeyValuePair<string, string?>> query = [];

        if (!string.IsNullOrWhiteSpace(name))
        {
            query.Add(new("name", name));
        }

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(customerId)}",
                Auth = Defaults.Service(auth),
                Query = query,
                Idempotent = true,
            },
            cancellationToken);
    }
}
