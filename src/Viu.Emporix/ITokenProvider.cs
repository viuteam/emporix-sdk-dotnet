namespace Viu.Emporix;

/// <summary>
/// An anonymous storefront session.
/// </summary>
/// <remarks>
/// The <see cref="SessionId"/> is what keeps a guest's cart together. It
/// survives a renewal via the refresh token — a fresh login assigns a new
/// session, and the previous cart is lost to that person.
/// </remarks>
public sealed class AnonymousSession
{
    internal AnonymousSession(
        string accessToken,
        string refreshToken,
        string sessionId,
        DateTimeOffset expiresAt)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        SessionId = sessionId;
        ExpiresAt = expiresAt;
    }

    /// <summary>The bearer token for anonymous calls.</summary>
    public string AccessToken { get; }

    /// <summary>The token used to renew the session.</summary>
    /// <remarks>
    /// Emporix rotates it on every renewal. Two concurrent renewals would
    /// therefore invalidate each other, which is why the SDK only ever runs one
    /// at a time.
    /// </remarks>
    public string RefreshToken { get; }

    /// <summary>The session identifier the cart, among other things, hangs on.</summary>
    public string SessionId { get; }

    /// <summary>
    /// When the access token counts as expired. The configured safety margin is
    /// already deducted.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Returns a description that contains no tokens.
    /// </summary>
    public override string ToString()
        => $"AnonymousSession {{ SessionId = {SessionId}, ExpiresAt = {ExpiresAt:O} }}";
}

/// <summary>
/// Obtains and manages the tokens the SDK owns itself.
/// </summary>
/// <remarks>
/// Implementing this interface is how you attach the SDK to an existing token
/// supply — an SSO or token-exchange service, for instance. Without a custom
/// implementation <see cref="DefaultTokenProvider"/> takes over.
/// </remarks>
public interface ITokenProvider
{
    /// <summary>
    /// Returns a valid client-credentials token for the named set.
    /// </summary>
    /// <param name="credentialSet">The name of the credential set.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="EmporixConfigurationException">The set is not configured.</exception>
    /// <exception cref="EmporixAuthenticationException">Emporix rejected the credentials.</exception>
    ValueTask<string> GetServiceTokenAsync(string credentialSet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a valid anonymous session, renewing it when needed.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="EmporixConfigurationException">No storefront credentials are configured.</exception>
    /// <exception cref="EmporixAuthenticationException">Emporix rejected the login.</exception>
    ValueTask<AnonymousSession> GetAnonymousSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the cached service token so the next call obtains a fresh one.
    /// </summary>
    /// <param name="credentialSet">The name of the credential set.</param>
    void InvalidateServiceToken(string credentialSet);

    /// <summary>
    /// Marks the anonymous access token as expired while keeping the refresh token.
    /// </summary>
    /// <remarks>
    /// This is how a 401 is answered: the next request renews the session rather
    /// than starting a new one, so the <see cref="AnonymousSession.SessionId"/>
    /// — and with it the cart — survives.
    /// </remarks>
    void ExpireAnonymousAccessToken();

    /// <summary>
    /// Discards the anonymous session entirely. The next call starts a new
    /// session with a new <see cref="AnonymousSession.SessionId"/>.
    /// </summary>
    void InvalidateAnonymousSession();
}
