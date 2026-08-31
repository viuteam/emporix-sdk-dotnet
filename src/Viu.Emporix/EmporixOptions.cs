namespace Viu.Emporix;

/// <summary>
/// Configuration for an <c>EmporixClient</c>.
/// </summary>
/// <remarks>
/// A single instance safely serves many concurrent users: the auth context is
/// passed per call and never stored in the options. Register the client as a
/// singleton.
/// </remarks>
public sealed class EmporixOptions
{
    /// <summary>The default Emporix API host.</summary>
    public const string DefaultHost = "https://api.emporix.io";

    /// <summary>
    /// The tenant this client works against. Required.
    /// </summary>
    /// <remarks>
    /// Emporix requires lowercase. The tenant appears in nearly every path but
    /// is also sent as the <c>Emporix-Tenant</c> header — without it Emporix
    /// rejects dashboard and IAM tokens with 401.
    /// </remarks>
    public string Tenant { get; set; } = string.Empty;

    /// <summary>The API base URL. Defaults to <see cref="DefaultHost"/>.</summary>
    public string Host { get; set; } = DefaultHost;

    /// <summary>
    /// The credential sets the SDK uses to obtain tokens of its own.
    /// </summary>
    /// <remarks>
    /// May stay empty: a client that only forwards externally supplied tokens
    /// needs no credentials.
    /// </remarks>
    public EmporixCredentials Credentials { get; set; } = new();

    /// <summary>Time limits for HTTP calls.</summary>
    public EmporixTimeoutOptions Timeouts { get; set; } = new();

    /// <summary>Retry behaviour for 5xx and 429 responses.</summary>
    public EmporixRetryOptions Retry { get; set; } = new();

    /// <summary>Cache behaviour for SDK-managed tokens.</summary>
    public EmporixTokenCacheOptions TokenCache { get; set; } = new();
}

/// <summary>
/// The credential sets the SDK uses to obtain tokens.
/// </summary>
public sealed class EmporixCredentials
{
    /// <summary>
    /// Client credentials for server-to-server calls. Required as soon as a call
    /// needs a service token.
    /// </summary>
    public EmporixServiceCredentials? Backend { get; set; }

    /// <summary>
    /// Credentials for anonymous storefront sessions. Needs no secret and may
    /// therefore ship inside a client application.
    /// </summary>
    public EmporixStorefrontCredentials? Storefront { get; set; }

    /// <summary>
    /// Additional named client-credential sets, for example for partner
    /// integrations. Addressed by their key.
    /// </summary>
    public IDictionary<string, EmporixServiceCredentials> Custom { get; }
        = new Dictionary<string, EmporixServiceCredentials>(StringComparer.Ordinal);
}

/// <summary>A client-credentials pair for the OAuth2 client-credentials flow.</summary>
public sealed class EmporixServiceCredentials
{
    /// <summary>The client id.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The client secret. Does not belong in a client application.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Optional scope restriction for the token request. Without it Emporix
    /// grants the scopes assigned to the client.
    /// </summary>
    public string? Scope { get; set; }
}

/// <summary>Credentials for anonymous storefront sessions.</summary>
public sealed class EmporixStorefrontCredentials
{
    /// <summary>The client id. Anonymous sessions need no secret.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The context an anonymous token is bound to.
    /// </summary>
    /// <remarks>
    /// Without this context Emporix cannot resolve currency, site or country on
    /// its side. Price lookups then return an empty list — with no error, which
    /// reads exactly like «no prices configured».
    /// </remarks>
    public EmporixStorefrontContext? Context { get; set; }
}

/// <summary>Currency, site and country an anonymous session is bound to.</summary>
public sealed class EmporixStorefrontContext
{
    /// <summary>ISO 4217 currency code, for example <c>CHF</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>The site code.</summary>
    public string? SiteCode { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code, for example <c>CH</c>.</summary>
    public string? TargetLocation { get; set; }

    /// <summary>
    /// Language for the <c>Accept-Language</c> header. Applies to every request
    /// and, unlike the other fields, does not force a new token.
    /// </summary>
    public string? Language { get; set; }
}

/// <summary>Time limits for HTTP calls.</summary>
public sealed class EmporixTimeoutOptions
{
    /// <summary>Time limit for receiving response headers. Defaults to 10 seconds.</summary>
    public TimeSpan Connect { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Overall time limit including reading the response body. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan Read { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>Retry behaviour for 5xx and 429 responses.</summary>
public sealed class EmporixRetryOptions
{
    /// <summary>
    /// Total number of attempts including the first. Defaults to 3;
    /// <c>1</c> disables retrying.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Upper bound for the wait between attempts. Defaults to 8 seconds.
    /// </summary>
    /// <remarks>
    /// Also caps a server-supplied <c>Retry-After</c>. Without this bound a
    /// value such as 86400 would stall a call for a day.
    /// </remarks>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(8);
}

/// <summary>Cache behaviour for SDK-managed tokens.</summary>
public sealed class EmporixTokenCacheOptions
{
    /// <summary>
    /// Safety margin by which a token counts as expired before it actually is.
    /// Defaults to 60 seconds.
    /// </summary>
    public TimeSpan ExpirationBuffer { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Absolute maximum lifetime of a cached token, regardless of its stated
    /// expiry. Defaults to 1 hour.
    /// </summary>
    public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromHours(1);
}
