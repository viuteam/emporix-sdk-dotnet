using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Attaches the token each request's <see cref="AuthContext"/> calls for, and
/// answers a 401 differently depending on who owns that token.
/// </summary>
/// <remarks>
/// For tokens the SDK owns (service, anonymous) the token is discarded and the
/// request repeated exactly once. For caller-owned tokens (customer, raw) that
/// only happens when an <see cref="ICustomerTokenRefresher"/> is registered —
/// without one the 401 stands, because the SDK does not own that token.
/// </remarks>
internal sealed class EmporixAuthenticationHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;
    private readonly EmporixOptions _options;
    private readonly CustomerTokenRefreshCoordinator? _customerRefresh;
    private readonly ILogger<EmporixAuthenticationHandler> _logger;

    public EmporixAuthenticationHandler(
        ITokenProvider tokenProvider,
        IOptions<EmporixOptions> options,
        ILogger<EmporixAuthenticationHandler> logger,
        CustomerTokenRefreshCoordinator? customerRefresh = null)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;
        _customerRefresh = customerRefresh;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth)
            || auth.Kind == AuthKind.None)
        {
            throw new EmporixConfigurationException(
                $"No AuthContext was set for {request.Method} {request.RequestUri}. "
                + "Every call must state what it is authorised with.");
        }

        ApplyTenantAndLanguage(request);

        bool canRetry = await ReplayableContent.TryPrepareAsync(request, cancellationToken)
            .ConfigureAwait(false);

        string token = await ResolveTokenAsync(auth, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized || !canRetry)
        {
            return response;
        }

        string? freshToken = await HandleUnauthorizedAsync(auth, token, cancellationToken)
            .ConfigureAwait(false);

        if (freshToken is null)
        {
            return response;
        }

        // The previous response is discarded — its body is never read.
        response.Dispose();

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a 401 and returns the token for the second attempt, or
    /// <see langword="null"/> when no retry should happen.
    /// </summary>
    private async ValueTask<string?> HandleUnauthorizedAsync(
        AuthContext auth,
        string rejectedToken,
        CancellationToken cancellationToken)
    {
        switch (auth.Kind)
        {
            case AuthKind.Service:
                Log.ReauthenticatingAfterUnauthorized(_logger, auth.Kind);
                _tokenProvider.InvalidateServiceToken(auth.CredentialSet!);
                return await _tokenProvider
                    .GetServiceTokenAsync(auth.CredentialSet!, cancellationToken)
                    .ConfigureAwait(false);

            case AuthKind.Anonymous:
                Log.ReauthenticatingAfterUnauthorized(_logger, auth.Kind);
                // Only expire the access token: the session is then renewed
                // rather than restarted, and the guest cart survives.
                _tokenProvider.ExpireAnonymousAccessToken();
                AnonymousSession session = await _tokenProvider
                    .GetAnonymousSessionAsync(cancellationToken)
                    .ConfigureAwait(false);
                return session.AccessToken;

            case AuthKind.Customer when _customerRefresh is { IsEnabled: true }:
                Log.ReauthenticatingAfterUnauthorized(_logger, auth.Kind);
                return await _customerRefresh.RefreshAsync(rejectedToken, cancellationToken)
                    .ConfigureAwait(false);

            // Customer tokens without a refresher, and raw tokens, belong to the
            // caller — the 401 is their information, not our job.
            default:
                return null;
        }
    }

    private async ValueTask<string> ResolveTokenAsync(AuthContext auth, CancellationToken cancellationToken)
        => auth.Kind switch
        {
            AuthKind.Service => await _tokenProvider
                .GetServiceTokenAsync(auth.CredentialSet!, cancellationToken)
                .ConfigureAwait(false),

            AuthKind.Anonymous => (await _tokenProvider
                .GetAnonymousSessionAsync(cancellationToken)
                .ConfigureAwait(false)).AccessToken,

            AuthKind.Customer or AuthKind.Raw => auth.Token!,

            _ => throw new EmporixConfigurationException($"Unknown token kind: {auth.Kind}."),
        };

    private void ApplyTenantAndLanguage(HttpRequestMessage request)
    {
        // The tenant appears in nearly every path but must also travel as a
        // header: Emporix validates dashboard and IAM tokens against it and
        // answers 401 without it, even when the token is correct.
        if (!request.Headers.Contains("Emporix-Tenant"))
        {
            request.Headers.Add("Emporix-Tenant", _options.Tenant);
        }

        string? language = _options.Credentials.Storefront?.Context?.Language;
        if (!string.IsNullOrWhiteSpace(language) && request.Headers.AcceptLanguage.Count == 0)
        {
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        }
    }
}
