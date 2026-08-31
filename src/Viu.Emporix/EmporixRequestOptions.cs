namespace Viu.Emporix;

/// <summary>
/// The keys through which a request passes extra information to the handler chain.
/// </summary>
/// <remarks>
/// <see cref="HttpRequestMessage.Options"/> is the intended way to hand custom
/// information to a <see cref="DelegatingHandler"/> — a handler only sees the
/// message, not the call it originated from.
/// </remarks>
internal static class EmporixRequestOptions
{
    /// <summary>What this request is authorised with.</summary>
    public static readonly HttpRequestOptionsKey<AuthContext> Auth = new("Viu.Emporix.Auth");

    /// <summary>
    /// Whether the request may safely be repeated after a server error.
    /// </summary>
    /// <remarks>
    /// Only needed for methods that are not inherently idempotent. A 5xx can
    /// arrive <em>after</em> the server committed the write — a repeated order
    /// would be a duplicate order.
    /// </remarks>
    public static readonly HttpRequestOptionsKey<bool> Idempotent = new("Viu.Emporix.Idempotent");

    /// <summary>The header the correlation id is sent under.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";
}
