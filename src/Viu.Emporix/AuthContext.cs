namespace Viu.Emporix;

/// <summary>The kind of token a call is authorised with.</summary>
public enum AuthKind
{
    /// <summary>Not set. A call carrying this value is rejected.</summary>
    None = 0,

    /// <summary>A client-credentials token obtained by the SDK.</summary>
    Service,

    /// <summary>An anonymous storefront token obtained by the SDK.</summary>
    Anonymous,

    /// <summary>A customer token supplied by the caller.</summary>
    Customer,

    /// <summary>A token passed through unchanged.</summary>
    Raw,
}

/// <summary>
/// Determines what a single call is authorised with.
/// </summary>
/// <remarks>
/// Passed per call and never stored on the client. That is why one client
/// instance safely serves many concurrent users — including a server acting for
/// a different customer on every request.
/// <para>
/// For the SDK-owned kinds (<see cref="AuthKind.Service"/>,
/// <see cref="AuthKind.Anonymous"/>) the SDK obtains and renews the token
/// itself. For the caller-owned kinds (<see cref="AuthKind.Customer"/>,
/// <see cref="AuthKind.Raw"/>) it forwards the token as given.
/// </para>
/// </remarks>
public readonly record struct AuthContext
{
    private AuthContext(AuthKind kind, string? token, string? credentialSet)
    {
        Kind = kind;
        Token = token;
        CredentialSet = credentialSet;
    }

    /// <summary>The name of the default service credential set.</summary>
    public const string DefaultCredentialSet = "backend";

    /// <summary>The kind of token.</summary>
    public AuthKind Kind { get; }

    /// <summary>
    /// The token for <see cref="AuthKind.Customer"/> and <see cref="AuthKind.Raw"/>,
    /// otherwise <see langword="null"/>.
    /// </summary>
    public string? Token { get; }

    /// <summary>
    /// The credential set addressed for <see cref="AuthKind.Service"/>,
    /// otherwise <see langword="null"/>.
    /// </summary>
    public string? CredentialSet { get; }

    /// <summary>
    /// Authorises with a client-credentials token.
    /// </summary>
    /// <param name="credentialSet">
    /// The credential set; defaults to <see cref="DefaultCredentialSet"/>. A
    /// named set must be configured under <see cref="EmporixCredentials.Custom"/>.
    /// </param>
    public static AuthContext Service(string? credentialSet = null)
        => new(AuthKind.Service, token: null, credentialSet ?? DefaultCredentialSet);

    /// <summary>Authorises with an anonymous storefront token.</summary>
    public static AuthContext Anonymous()
        => new(AuthKind.Anonymous, token: null, credentialSet: null);

    /// <summary>
    /// Authorises with a signed-in customer's token.
    /// </summary>
    /// <param name="token">The customer token.</param>
    /// <exception cref="ArgumentException"><paramref name="token"/> is empty.</exception>
    public static AuthContext Customer(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new AuthContext(AuthKind.Customer, token, credentialSet: null);
    }

    /// <summary>
    /// Authorises with a token passed through unchanged.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <exception cref="ArgumentException"><paramref name="token"/> is empty.</exception>
    /// <remarks>
    /// The escape hatch for tokens minted outside the SDK — from an SSO or
    /// token-exchange flow, for instance.
    /// </remarks>
    public static AuthContext Raw(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new AuthContext(AuthKind.Raw, token, credentialSet: null);
    }

    /// <summary>
    /// Returns a description that does not contain the token.
    /// </summary>
    /// <remarks>
    /// Deliberately overrides the record-generated version: that one would have
    /// printed <see cref="Token"/> and thereby carried a bearer token into every
    /// log line and error message that interpolates this value.
    /// </remarks>
    public override string ToString()
        => Kind == AuthKind.Service
            ? $"AuthContext {{ Kind = {Kind}, CredentialSet = {CredentialSet} }}"
            : $"AuthContext {{ Kind = {Kind} }}";
}
