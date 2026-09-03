using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// The built-in token supply: client-credentials tokens per credential set and
/// an anonymous storefront session, both cached.
/// </summary>
/// <remarks>
/// Thread-safe and intended as a singleton. At most one acquisition runs per
/// credential set: concurrent callers wait for its result instead of each
/// requesting a token of their own.
/// </remarks>
public sealed class DefaultTokenProvider : ITokenProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly EmporixOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DefaultTokenProvider> _logger;

    // Keyed the same way credential sets are named, so one set means one cached
    // token and one gate no matter how a call spelled it.
    private readonly ConcurrentDictionary<string, CachedToken> _serviceTokens =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _serviceGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _anonymousGate = new(1, 1);

    private AnonymousSession? _anonymousSession;
    private bool _disposed;

    /// <summary>Creates a new instance.</summary>
    /// <param name="httpClient">
    /// The client for the token endpoints. Deliberately separate from the one
    /// used for API calls: token requests must not pass through the retry or
    /// authentication layers they themselves supply.
    /// </param>
    /// <param name="options">The SDK configuration.</param>
    /// <param name="logger">The log sink.</param>
    /// <param name="timeProvider">
    /// The clock used for expiry arithmetic. Defaults to system time.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public DefaultTokenProvider(
        HttpClient httpClient,
        IOptions<EmporixOptions> options,
        ILogger<DefaultTokenProvider> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<string> GetServiceTokenAsync(
        string credentialSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialSet);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // An unknown set is a configuration mistake and should surface before
        // anything is locked or requested.
        EmporixServiceCredentials credentials = ResolveCredentials(credentialSet);

        if (TryGetFreshToken(credentialSet, out string? cached))
        {
            return cached;
        }

        SemaphoreSlim gate = _serviceGates.GetOrAdd(credentialSet, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Check again: while waiting, somebody else may already have
            // obtained a token.
            if (TryGetFreshToken(credentialSet, out cached))
            {
                return cached;
            }

            return await RequestServiceTokenAsync(credentialSet, credentials, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<AnonymousSession> GetAnonymousSessionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        AnonymousSession? current = _anonymousSession;
        if (IsAnonymousSessionFresh(current))
        {
            return current;
        }

        await _anonymousGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = _anonymousSession;
            if (IsAnonymousSessionFresh(current))
            {
                return current;
            }

            // With a refresh token in hand, renew first: that preserves the
            // session id and with it the cart. A fresh login is the fallback,
            // not the normal path.
            string? refreshToken = current?.RefreshToken;
            if (!string.IsNullOrEmpty(refreshToken))
            {
                try
                {
                    return await RequestAnonymousSessionAsync(refreshToken, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (EmporixException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Log.AnonymousRefreshFailed(_logger, exception);
                }
            }

            return await RequestAnonymousSessionAsync(refreshToken: null, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _anonymousGate.Release();
        }
    }

    /// <inheritdoc />
    public void InvalidateServiceToken(string credentialSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialSet);
        _serviceTokens.TryRemove(credentialSet, out _);
    }

    /// <inheritdoc />
    public void ExpireAnonymousAccessToken()
    {
        AnonymousSession? current = _anonymousSession;
        if (current is null)
        {
            return;
        }

        // Move the expiry into the past, keep the refresh token.
        _anonymousSession = new AnonymousSession(
            current.AccessToken,
            current.RefreshToken,
            current.SessionId,
            _timeProvider.GetUtcNow() - TimeSpan.FromSeconds(1));
    }

    /// <inheritdoc />
    public void InvalidateAnonymousSession() => _anonymousSession = null;

    /// <summary>Releases the internal locks.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _anonymousGate.Dispose();

        foreach (SemaphoreSlim gate in _serviceGates.Values)
        {
            gate.Dispose();
        }

        _serviceGates.Clear();
    }

    private EmporixServiceCredentials ResolveCredentials(string credentialSet)
    {
        if (string.Equals(credentialSet, AuthContext.DefaultCredentialSet, StringComparison.OrdinalIgnoreCase))
        {
            return _options.Credentials.Backend
                ?? throw new EmporixConfigurationException(
                    "The call requires a service token, but Credentials.Backend is not set.");
        }

        return _options.Credentials.Custom.TryGetValue(credentialSet, out EmporixServiceCredentials? custom)
            ? custom
            : throw new EmporixConfigurationException(
                $"Credential set \"{credentialSet}\" is not configured. "
                + $"Add it under Credentials.Custom[\"{credentialSet}\"].");
    }

    private bool TryGetFreshToken(string credentialSet, out string token)
    {
        if (_serviceTokens.TryGetValue(credentialSet, out CachedToken cached))
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();

            // Two conditions: the server-stated expiry and an absolute maximum
            // lifetime. The latter bounds the damage when a server reports an
            // unrealistically long validity.
            if (now < cached.ExpiresAt && now - cached.ObtainedAt < _options.TokenCache.MaxLifetime)
            {
                token = cached.Token;
                return true;
            }
        }

        token = string.Empty;
        return false;
    }

    private async ValueTask<string> RequestServiceTokenAsync(
        string credentialSet,
        EmporixServiceCredentials credentials,
        CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> form =
        [
            new("grant_type", "client_credentials"),
            new("client_id", credentials.ClientId),
            new("client_secret", credentials.Secret),
        ];

        if (!string.IsNullOrWhiteSpace(credentials.Scope))
        {
            form.Add(new KeyValuePair<string, string>("scope", credentials.Scope));
        }

        using FormUrlEncodedContent content = new(form);
        using HttpRequestMessage request = new(HttpMethod.Post, BuildUri("/oauth/token"))
        {
            Content = content,
        };

        Log.RequestingServiceToken(_logger, credentialSet);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw ToAuthenticationException(
                response.StatusCode,
                $"Token request for set \"{credentialSet}\"",
                body,
                SentCorrelationId(request));
        }

        (string? accessToken, int expiresIn, _, _) = ParseTokenResponse(body);

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new EmporixAuthenticationException(
                $"The token response for set \"{credentialSet}\" contained no access_token.",
                response.StatusCode,
                rawBody: body)
            {
                CorrelationId = SentCorrelationId(request),
            };
        }

        DateTimeOffset obtainedAt = _timeProvider.GetUtcNow();
        _serviceTokens[credentialSet] = new CachedToken(
            accessToken,
            obtainedAt + TimeSpan.FromSeconds(expiresIn) - _options.TokenCache.ExpirationBuffer,
            obtainedAt);

        Log.ServiceTokenObtained(_logger, credentialSet, expiresIn);

        return accessToken;
    }

    private async ValueTask<AnonymousSession> RequestAnonymousSessionAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        EmporixStorefrontCredentials storefront = _options.Credentials.Storefront
            ?? throw new EmporixConfigurationException(
                "The call requires an anonymous token, but Credentials.Storefront is not set.");

        bool isRefresh = !string.IsNullOrEmpty(refreshToken);
        string mode = isRefresh ? "refresh" : "login";

        List<string> query =
        [
            $"tenant={Uri.EscapeDataString(_options.Tenant)}",
            $"client_id={Uri.EscapeDataString(storefront.ClientId)}",
        ];

        // Without this context Emporix binds the token to nothing, and price
        // matching later returns an empty list instead of an error.
        EmporixStorefrontContext? context = storefront.Context;
        AddIfSet(query, "currency", context?.Currency);
        AddIfSet(query, "siteCode", context?.SiteCode);
        AddIfSet(query, "targetLocation", context?.TargetLocation);

        if (isRefresh)
        {
            query.Add($"refresh_token={Uri.EscapeDataString(refreshToken!)}");
        }

        Uri uri = BuildUri($"/customerlogin/auth/anonymous/{mode}?{string.Join('&', query)}");
        using HttpRequestMessage request = new(HttpMethod.Get, uri);

        Log.RequestingAnonymousSession(_logger, mode);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw ToAuthenticationException(
                response.StatusCode,
                $"Anonymous session ({mode})",
                body,
                SentCorrelationId(request));
        }

        (string? accessToken, int expiresIn, string? newRefreshToken, string? sessionId) =
            ParseTokenResponse(body);

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new EmporixAuthenticationException(
                $"The anonymous {mode} response contained no access_token.",
                response.StatusCode,
                rawBody: body)
            {
                CorrelationId = SentCorrelationId(request),
            };
        }

        AnonymousSession session = new(
            accessToken,
            newRefreshToken ?? refreshToken ?? string.Empty,
            sessionId ?? _anonymousSession?.SessionId ?? string.Empty,
            _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(expiresIn) - _options.TokenCache.ExpirationBuffer);

        _anonymousSession = session;

        Log.AnonymousSessionObtained(_logger, mode, session.SessionId, expiresIn);

        return session;
    }

    private async ValueTask<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Token requests deliberately bypass the handler chain, so they also
        // miss the correlation id EmporixHttpClient would otherwise assign.
        // Without one, an authentication failure of all things would be
        // untraceable.
        request.Headers.TryAddWithoutValidation(
            EmporixRequestOptions.CorrelationIdHeader,
            CurrentCorrelationId());

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Not cancelled by the caller, so this is a timeout. Token requests
            // sit in front of the lock — a hung request would otherwise block
            // every call for this credential set indefinitely.
            throw new EmporixTimeoutException(
                $"The token request exceeded its time limit of {_options.Timeouts.Read}.",
                _options.Timeouts.Read,
                exception)
            {
                CorrelationId = SentCorrelationId(request),
            };
        }
        catch (HttpRequestException exception)
        {
            throw new EmporixNetworkException(
                $"The token request failed: {exception.Message}",
                exception)
            {
                CorrelationId = SentCorrelationId(request),
            };
        }
    }

    /// <summary>The id that actually went out with this request.</summary>
    private static string SentCorrelationId(HttpRequestMessage request)
        => request.Headers.TryGetValues(EmporixRequestOptions.CorrelationIdHeader, out IEnumerable<string>? values)
            ? values.FirstOrDefault() ?? string.Empty
            : string.Empty;

    /// <summary>The id under which this request can be found again.</summary>
    private static string CurrentCorrelationId()
        => Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

    private static EmporixAuthenticationException ToAuthenticationException(
        HttpStatusCode statusCode,
        string description,
        string body,
        string correlationId)
    {
        // A failure at a token endpoint is always an authentication problem,
        // whatever the status code — the parser reads the message from both
        // Emporix error formats.
        (string? message, string? errorCode, IReadOnlyList<string> details) =
            EmporixErrorParser.Parse(body);

        return new EmporixAuthenticationException(
            message is { Length: > 0 }
                ? $"{description} failed ({(int)statusCode}): {message}"
                : $"{description} failed ({(int)statusCode}).",
            statusCode,
            errorCode,
            details,
            body)
        {
            CorrelationId = correlationId,
        };
    }

    /// <summary>
    /// Reads the fields of a token response. As with the error parser this goes
    /// through <see cref="JsonDocument"/> rather than a serializer:
    /// reflection-free and unfazed by unexpected response shapes.
    /// </summary>
    private static (string? AccessToken, int ExpiresIn, string? RefreshToken, string? SessionId)
        ParseTokenResponse(string body)
    {
        const int fallbackExpiresIn = 3600;

        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, fallbackExpiresIn, null, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, fallbackExpiresIn, null, null);
            }

            return (
                ReadString(root, "access_token"),
                ReadExpiresIn(root) ?? fallbackExpiresIn,
                ReadString(root, "refresh_token"),
                ReadString(root, "sessionId"));
        }
        catch (JsonException)
        {
            return (null, fallbackExpiresIn, null, null);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static int? ReadExpiresIn(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out JsonElement value))
        {
            return null;
        }

        // Depending on the endpoint, Emporix sends this as a number or a string.
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed) => parsed,
            _ => null,
        };
    }

    private bool IsAnonymousSessionFresh([NotNullWhen(true)] AnonymousSession? session)
        => session is not null && _timeProvider.GetUtcNow() < session.ExpiresAt;

    private Uri BuildUri(string pathAndQuery) => new(new Uri(_options.Host), pathAndQuery);

    private static void AddIfSet(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private readonly record struct CachedToken(
        string Token,
        DateTimeOffset ExpiresAt,
        DateTimeOffset ObtainedAt)
    {
        /// <summary>Keeps the token from leaking through a string conversion.</summary>
        public override string ToString() => $"CachedToken {{ ExpiresAt = {ExpiresAt:O} }}";
    }
}
