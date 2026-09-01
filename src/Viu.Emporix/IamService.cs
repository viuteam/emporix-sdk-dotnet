using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.IamModels;

namespace Viu.Emporix;

/// <summary>
/// Identity and access — users, groups, and what they may do.
/// </summary>
/// <remarks>
/// <para>
/// The widest service in the API, and it divides along what is configurable and
/// what is not. <see cref="Users"/>, <see cref="Groups"/> and
/// <see cref="AccessControls"/> are managed; permissions, resources, roles,
/// scopes and templates are Emporix's own catalogue and read-only here.
/// </para>
/// <para>
/// B2B group membership lives here rather than under customers: putting a
/// company's people into a group is <see cref="IamGroupOperations.AddMemberAsync"/>,
/// not anything on <see cref="CustomerAdminService"/>.
/// </para>
/// </remarks>
public sealed class IamService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal IamService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/iam/{_tenant}";

    /// <summary>Users, and what each of them may do.</summary>
    public IamUserOperations Users => new(_http, _tenant);

    /// <summary>Groups, and who is in them.</summary>
    public IamGroupOperations Groups => new(_http, _tenant);

    /// <summary>Access controls — a role granted on a resource.</summary>
    public IamAccessControlOperations AccessControls => new(_http, _tenant);

    /// <summary>Lists the scopes.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CustomScopeQueryDocument>> ListScopesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/scopes",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListCustomScopeQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a scope.</summary>
    /// <param name="scopeId">The scope id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CustomScopeQueryDocument?> GetScopeAsync(
        string scopeId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/scopes/{Uri.EscapeDataString(scopeId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.CustomScopeQueryDocument,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a scope.</summary>
    /// <param name="scopeId">The scope id.</param>
    /// <param name="scope">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CustomScopeIdResponse?> ReplaceScopeAsync(
        string scopeId,
        CustomScopeUpsertRequest scope,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(scope);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/scopes/{Uri.EscapeDataString(scopeId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    scope,
                    IamJsonContext.Default.CustomScopeUpsertRequest),
            },
            IamJsonContext.Default.CustomScopeIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a scope.</summary>
    /// <param name="scopeId">The scope id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Tokens already minted keep the scope until they expire — revoking here
    /// is not immediate.
    /// </remarks>
    public Task DeleteScopeAsync(
        string scopeId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/scopes/{Uri.EscapeDataString(scopeId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists the permissions Emporix defines.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Read-only: the catalogue is Emporix's, not the tenant's.</remarks>
    public async Task<IReadOnlyList<PermissionQueryDocument>> ListPermissionsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/permissions",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListPermissionQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a permission.</summary>
    /// <param name="permissionId">The permission id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PermissionQueryDocument?> GetPermissionAsync(
        string permissionId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/permissions/{Uri.EscapeDataString(permissionId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.PermissionQueryDocument,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the resources access can be granted on.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ResourceQueryDocument>> ListResourcesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/resources",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListResourceQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a resource.</summary>
    /// <param name="resourceId">The resource id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceQueryDocument?> GetResourceAsync(
        string resourceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/resources/{Uri.EscapeDataString(resourceId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ResourceQueryDocument,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the roles.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<RoleQueryDocument>> ListRolesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/roles",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListRoleQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a role.</summary>
    /// <param name="roleId">The role id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<RoleQueryDocument?> GetRoleAsync(
        string roleId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/roles/{Uri.EscapeDataString(roleId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.RoleQueryDocument,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the access-control templates.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The ready-made grants a tenant can start from.</remarks>
    public async Task<IReadOnlyList<TemplateQueryDocument>> ListTemplatesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/templates",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListTemplateQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
    ];
}

/// <summary>
/// Users, and what each of them may do.
/// </summary>
/// <remarks>
/// Reached through <see cref="IamService.Users"/>. The <c>me</c> calls answer
/// for whoever the token belongs to, which is how an application finds out its
/// own scopes without knowing its user id.
/// </remarks>
public sealed class IamUserOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal IamUserOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/iam/{_tenant}/users";

    /// <summary>Fetches a user.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<UserExtendedResponse?> GetAsync(
        string userId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.UserExtendedResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches the user the token belongs to.</summary>
    /// <param name="auth">What to authorise with. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<UserResponse?> GetMeAsync(
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/me",
                Auth = auth,
            },
            IamJsonContext.Default.UserResponse,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Fetches one page of users.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<UserExtendedResponse>> ListAsync(
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
                Query = IamService.Paging(pageNumber, pageSize),
            },
            IamJsonContext.Default.ListUserExtendedResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists a vendor's users.</summary>
    /// <param name="vendorId">The vendor.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<UserResponse>> ListForVendorAsync(
        string vendorId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/vendors/{Uri.EscapeDataString(vendorId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListUserResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Creates a user.</summary>
    /// <param name="user">The user to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<UserIdResponse?> CreateAsync(
        UserCreateRequest user,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(user, IamJsonContext.Default.UserCreateRequest),
            },
            IamJsonContext.Default.UserIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a user.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="user">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string userId,
        UserUpdateRequest user,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(user);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(user, IamJsonContext.Default.UserUpdateRequest),
            },
            cancellationToken);
    }

    /// <summary>Deletes a user.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string userId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists a user's access controls.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AccessControlQueryDocument>> ListAccessControlsAsync(
        string userId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}/access-controls",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListAccessControlQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Lists a user's access controls on one resource.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="resourceId">The resource.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AccessControlQueryDocument>> ListAccessControlsForResourceAsync(
        string userId,
        string resourceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}/access-controls"
                    + $"/{Uri.EscapeDataString(resourceId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListAccessControlQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Lists a user's permissions on one resource.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="resourceId">The resource.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The resolved answer: what this person may actually do here, after groups
    /// and templates have been applied.
    /// </remarks>
    public async Task<IReadOnlyList<PermissionQueryDocument>> ListPermissionsAsync(
        string userId,
        string resourceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}/permissions"
                    + $"/{Uri.EscapeDataString(resourceId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListPermissionQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Lists a user's scopes.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<UserScopesResponse?> ListScopesAsync(
        string userId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}/scopes",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.UserScopesResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the access controls of whoever the token belongs to.</summary>
    /// <param name="auth">What to authorise with. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AccessControlQueryDocument>> ListMyAccessControlsAsync(
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/me/access-controls",
                Auth = auth,
            },
            IamJsonContext.Default.ListAccessControlQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the scopes of whoever the token belongs to.</summary>
    /// <param name="auth">What to authorise with. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// How an application finds out what its own token is good for, without
    /// knowing its user id.
    /// </remarks>
    public async Task<UserScopesResponse?> ListMyScopesAsync(
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/me/scopes",
                Auth = auth,
            },
            IamJsonContext.Default.UserScopesResponse,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Lists the groups a user belongs to.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<GroupsQueryDocument>> ListGroupsAsync(
        string userId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}/groups",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListGroupsQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Reads one of a user's group memberships.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="groupId">The group.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<GroupsQueryDocument?> GetGroupAsync(
        string userId,
        string groupId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}/groups"
                    + $"/{Uri.EscapeDataString(groupId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.GroupsQueryDocument,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a user from every group.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// What to call when someone leaves: it strips whatever their group
    /// memberships granted, without deleting the user.
    /// </remarks>
    public Task RemoveFromAllGroupsAsync(
        string userId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(userId)}/groups",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Groups, and who is in them.
/// </summary>
/// <remarks>
/// Reached through <see cref="IamService.Groups"/>. This is where B2B group
/// membership lives — a company's people are put into a group here, not through
/// the customer services.
/// </remarks>
public sealed class IamGroupOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal IamGroupOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/iam/{_tenant}/groups";

    /// <summary>Fetches one page of groups.</summary>
    /// <param name="query">An optional Emporix filter, for example by legal entity.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<GroupsQueryDocument>> ListAsync(
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
            .. IamService.Paging(pageNumber, pageSize),
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
            IamJsonContext.Default.ListGroupsQueryDocument,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<GroupsQueryDocument?> GetAsync(
        string groupId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(groupId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.GroupsQueryDocument,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a group.</summary>
    /// <param name="group">The group to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<GroupIdResponse?> CreateAsync(
        GroupCreateRequest group,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    group,
                    IamJsonContext.Default.GroupCreateRequest),
            },
            IamJsonContext.Default.GroupIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="group">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string groupId,
        GroupUpdateRequest group,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(group);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(groupId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    group,
                    IamJsonContext.Default.GroupUpdateRequest),
            },
            cancellationToken);
    }

    /// <summary>Deletes a group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Its members lose whatever the group granted them.</remarks>
    public Task DeleteAsync(
        string groupId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(groupId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists a group's access controls.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>What the group grants, before it reaches any particular member.</remarks>
    public async Task<IReadOnlyList<AccessControlQueryDocument>> ListAccessControlsAsync(
        string groupId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(groupId)}/access-controls",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListAccessControlQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Lists a group's members.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AssignmentQueryDocument>> ListMembersAsync(
        string groupId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(groupId)}/users",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.ListAssignmentQueryDocument,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Adds a member to a group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="assignment">Who to add.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The B2B call: this is how a company's people are put into a group, and
    /// there is nothing equivalent on the customer services.
    /// </remarks>
    public async Task<AssignmentIdResponse?> AddMemberAsync(
        string groupId,
        AssignmentCreateRequest assignment,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(assignment);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(groupId)}/users",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    assignment,
                    IamJsonContext.Default.AssignmentCreateRequest),
            },
            IamJsonContext.Default.AssignmentIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Puts one user of a given kind in a group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="userType">The kind of user, for example a customer or an employee.</param>
    /// <param name="userId">The user.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Not an overload of <see cref="AddMemberAsync(string, AssignmentCreateRequest, AuthContext, CancellationToken)"/>,
    /// and not only because the analyzer forbids two overloads with optional
    /// parameters: this is a <c>PUT</c> at the member's own address, so
    /// repeating it changes nothing, while adding by request body creates an
    /// assignment each time.
    /// </remarks>
    public Task AssignMemberAsync(
        string groupId,
        string userType,
        string userId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userType);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(groupId)}/users"
                    + $"/{Uri.EscapeDataString(userType)}/{Uri.EscapeDataString(userId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Removes one member from a group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="userId">The user.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Takes no user type, unlike adding — Emporix asymmetry, not an omission
    /// here.
    /// </remarks>
    public Task RemoveMemberAsync(
        string groupId,
        string userId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(groupId)}/users"
                    + $"/{Uri.EscapeDataString(userId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Empties a group.</summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RemoveAllMembersAsync(
        string groupId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(groupId)}/users",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Access controls — a role granted on a resource.
/// </summary>
/// <remarks>
/// Reached through <see cref="IamService.AccessControls"/>. Emporix creates
/// these through templates rather than directly, which is why there is no create
/// here: an access control is upserted at its own address.
/// </remarks>
public sealed class IamAccessControlOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal IamAccessControlOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/iam/{_tenant}/access-controls";

    /// <summary>Fetches one page of access controls.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<AccessControlQueryDocument>> ListAsync(
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
                Query = IamService.Paging(pageNumber, pageSize),
            },
            IamJsonContext.Default.ListAccessControlQueryDocument,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches an access control.</summary>
    /// <param name="accessControlId">The access control id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<AccessControlQueryDocument?> GetAsync(
        string accessControlId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessControlId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(accessControlId)}",
                Auth = Defaults.Service(auth),
            },
            IamJsonContext.Default.AccessControlQueryDocument,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Grants or replaces an access control.</summary>
    /// <param name="accessControlId">The access control id.</param>
    /// <param name="accessControl">What is granted.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Takes effect on the holder's next call, not on their next sign-in.
    /// </remarks>
    public async Task<AccessControlIdResponse?> UpsertAsync(
        string accessControlId,
        AccessControlUpsertRequest accessControl,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessControlId);
        ArgumentNullException.ThrowIfNull(accessControl);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(accessControlId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    accessControl,
                    IamJsonContext.Default.AccessControlUpsertRequest),
            },
            IamJsonContext.Default.AccessControlIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Revokes an access control.</summary>
    /// <param name="accessControlId">The access control id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string accessControlId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessControlId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(accessControlId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}
