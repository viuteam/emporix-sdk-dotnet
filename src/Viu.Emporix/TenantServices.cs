using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Tenant and client configuration.
/// </summary>
/// <remarks>
/// <para>
/// Two scopes, one service. Tenant configuration applies everywhere;
/// <see cref="ForClient"/> narrows it to one client id, which is how a
/// storefront and a back office can disagree about the same property.
/// </para>
/// <para>
/// A value is arbitrary JSON, so it is read and written as
/// <see cref="System.Text.Json.JsonElement"/> — there is no schema here to
/// generate a type from.
/// </para>
/// </remarks>
public sealed class ConfigurationService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal ConfigurationService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/configuration/{_tenant}/configurations";

    /// <summary>Configuration scoped to one client.</summary>
    /// <param name="client">The client id.</param>
    public ClientConfigurationOperations ForClient(string client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(client);
        return new ClientConfigurationOperations(
            _http,
            $"/configuration/{_tenant}/clients/{Uri.EscapeDataString(client)}/configurations");
    }

    /// <summary>Lists the tenant's configuration.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ConfigurationModels.BaseConfiguration>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
            },
            ConfigurationJsonContext.Default.ListBaseConfiguration,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the configuration Emporix sets for every tenant.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Read-only, and a tenant's own values override these.</remarks>
    public async Task<IReadOnlyList<ConfigurationModels.BaseConfiguration>> ListGlobalAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/configuration/{_tenant}/global-configurations",
                Auth = Defaults.Service(auth),
            },
            ConfigurationJsonContext.Default.ListBaseConfiguration,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the clients that have their own configuration.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ConfigurationModels.ClientConfiguration>> ListClientsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/configuration/{_tenant}/clients",
                Auth = Defaults.Service(auth),
            },
            ConfigurationJsonContext.Default.ListClientConfiguration,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Reads one tenant property.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ConfigurationModels.BaseConfiguration?> GetAsync(
        string propertyKey,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyKey);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(propertyKey)}",
                Auth = Defaults.Service(auth),
            },
            ConfigurationJsonContext.Default.BaseConfiguration,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets a tenant property.</summary>
    /// <param name="configuration">The property and its value.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        ConfigurationModels.BaseConfiguration configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration,
                    ConfigurationJsonContext.Default.BaseConfiguration),
            },
            cancellationToken);
    }

    /// <summary>Replaces a tenant property.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <param name="configuration">The new value.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Applies to every client that has no value of its own for this key.
    /// </remarks>
    public Task ReplaceAsync(
        string propertyKey,
        ConfigurationModels.BaseConfiguration configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyKey);
        ArgumentNullException.ThrowIfNull(configuration);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(propertyKey)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration,
                    ConfigurationJsonContext.Default.BaseConfiguration),
            },
            cancellationToken);
    }

    /// <summary>Removes a tenant property.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Whatever Emporix sets globally applies again.</remarks>
    public Task DeleteAsync(
        string propertyKey,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyKey);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(propertyKey)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Configuration scoped to one client.
/// </summary>
/// <remarks>
/// Reached through <see cref="ConfigurationService.ForClient"/>. A value here
/// overrides the tenant's for that client only, which is how a storefront and a
/// back office can read different values for the same key.
/// </remarks>
public sealed class ClientConfigurationOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal ClientConfigurationOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Lists this client's configuration.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ConfigurationModels.BaseConfiguration>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = _basePath,
                Auth = Defaults.Service(auth),
            },
            ConfigurationJsonContext.Default.ListBaseConfiguration,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Reads one of this client's properties.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ConfigurationModels.BaseConfiguration?> GetAsync(
        string propertyKey,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyKey);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/{Uri.EscapeDataString(propertyKey)}",
                Auth = Defaults.Service(auth),
            },
            ConfigurationJsonContext.Default.BaseConfiguration,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets a property for this client.</summary>
    /// <param name="configuration">The property and its value.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        ConfigurationModels.BaseConfiguration configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration,
                    ConfigurationJsonContext.Default.BaseConfiguration),
            },
            cancellationToken);
    }

    /// <summary>Replaces one of this client's properties.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <param name="configuration">The new value.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string propertyKey,
        ConfigurationModels.BaseConfiguration configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyKey);
        ArgumentNullException.ThrowIfNull(configuration);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/{Uri.EscapeDataString(propertyKey)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration,
                    ConfigurationJsonContext.Default.BaseConfiguration),
            },
            cancellationToken);
    }

    /// <summary>Removes one of this client's properties.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The tenant's value applies again for this client.</remarks>
    public Task DeleteAsync(
        string propertyKey,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyKey);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/{Uri.EscapeDataString(propertyKey)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Session context — what a session carries beyond its token.
/// </summary>
/// <remarks>
/// Currency, site, language and whatever else a tenant attaches. The
/// <c>me</c> calls act on the caller's own session; the by-id calls are for a
/// back office acting on someone else's, and need a service token.
/// </remarks>
public sealed class SessionContextService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal SessionContextService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/session-context/{_tenant}";

    /// <summary>Reads the caller's own session context.</summary>
    /// <param name="auth">The caller's context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SessionContextModels.SessionContext_GET?> GetMineAsync(
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/me/context",
                Auth = auth,
            },
            SessionContextJsonContext.Default.SessionContext_GET,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Changes part of the caller's own session context.</summary>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">The caller's context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Changing currency or site here does not re-price anything already
    /// fetched — the next call sees the new context.
    /// </remarks>
    public Task UpdateMineAsync(
        SessionContextModels.SessionContext_PATCH changes,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/me/context",
                Auth = auth,
                Content = EmporixJsonContent.Create(
                    changes,
                    SessionContextJsonContext.Default.SessionContext_PATCH),
            },
            cancellationToken);
    }

    /// <summary>Adds an attribute to the caller's own session context.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="auth">The caller's context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task AddMyAttributeAsync(
        SessionContextModels.ContextAttribute attribute,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/me/context/attributes",
                Auth = auth,
                Content = EmporixJsonContent.Create(
                    attribute,
                    SessionContextJsonContext.Default.ContextAttribute),
            },
            cancellationToken);
    }

    /// <summary>Removes an attribute from the caller's own session context.</summary>
    /// <param name="attributeName">The attribute name.</param>
    /// <param name="auth">The caller's context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RemoveMyAttributeAsync(
        string attributeName,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/me/context/attributes/{Uri.EscapeDataString(attributeName)}",
                Auth = auth,
            },
            cancellationToken);
    }

    /// <summary>Reads someone else's session context.</summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The back-office view. A session id identifies a person's live session, so
    /// this reads what somebody is currently doing — a service token, not a
    /// shopper's.
    /// </remarks>
    public async Task<SessionContextModels.SessionContext_GET?> GetAsync(
        string sessionId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/context/{Uri.EscapeDataString(sessionId)}",
                Auth = Defaults.Service(auth),
            },
            SessionContextJsonContext.Default.SessionContext_GET,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces someone else's session context.</summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="context">The new context.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string sessionId,
        SessionContextModels.SessionContext_PUT context,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(context);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/context/{Uri.EscapeDataString(sessionId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    context,
                    SessionContextJsonContext.Default.SessionContext_PUT),
            },
            cancellationToken);
    }

    /// <summary>Adds an attribute to someone else's session context.</summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task AddAttributeAsync(
        string sessionId,
        SessionContextModels.ContextAttribute attribute,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(attribute);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/context/{Uri.EscapeDataString(sessionId)}/attributes",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    attribute,
                    SessionContextJsonContext.Default.ContextAttribute),
            },
            cancellationToken);
    }

    /// <summary>Removes an attribute from someone else's session context.</summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="attributeName">The attribute name.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RemoveAttributeAsync(
        string sessionId,
        string attributeName,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/context/{Uri.EscapeDataString(sessionId)}/attributes"
                    + $"/{Uri.EscapeDataString(attributeName)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}
