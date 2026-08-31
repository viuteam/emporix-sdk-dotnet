using System.Net;

namespace Viu.Emporix;

/// <summary>
/// Base class for every error this SDK raises.
/// </summary>
/// <remarks>
/// Catch this type to handle any SDK failure at once. For the usual distinction
/// between «a response with an error status» and «the request never arrived»,
/// use <see cref="EmporixApiException"/> and <see cref="EmporixTransportException"/>.
/// </remarks>
public abstract class EmporixException : Exception
{
    private protected EmporixException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The correlation id of the request that caused this failure.
    /// </summary>
    /// <remarks>
    /// The SDK assigns it per request and sends it along. Quote it in support
    /// requests — it is what makes a call findable across log boundaries.
    /// </remarks>
    public string? CorrelationId { get; internal set; }
}

/// <summary>
/// Emporix responded, but with an error status.
/// </summary>
/// <remarks>
/// Raised directly when no specialised type matches the status code.
/// </remarks>
public class EmporixApiException : EmporixException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="errorCode">Emporix' <c>errorCode</c>, if present.</param>
    /// <param name="details">The entries from <c>details</c>, if present.</param>
    /// <param name="rawBody">The unparsed response body.</param>
    internal EmporixApiException(
        string message,
        HttpStatusCode statusCode,
        string? errorCode = null,
        IReadOnlyList<string>? details = null,
        string? rawBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details ?? [];
        RawBody = rawBody;
    }

    /// <summary>The HTTP status code of the response.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Emporix' application-specific error code, if the response carried one.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// The entries from the response's <c>details</c> field — Emporix describes
    /// the individual causes there. Empty when the response carried none.
    /// </summary>
    public IReadOnlyList<string> Details { get; }

    /// <summary>
    /// The unparsed response body.
    /// </summary>
    /// <remarks>
    /// Populated even when the response was not JSON — an HTML error page from
    /// an upstream proxy, say. That is exactly what this field is for.
    /// </remarks>
    public string? RawBody { get; }
}

/// <summary>
/// 401 — authentication is missing, invalid or expired.
/// </summary>
/// <remarks>
/// For SDK-owned tokens (service, anonymous) the SDK obtains a fresh token once
/// and repeats the request; this error then means that failed too. Caller-owned
/// tokens (customer, raw) are not refreshed automatically — unless a token
/// refresher is registered.
/// </remarks>
public sealed class EmporixAuthenticationException : EmporixApiException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="errorCode">Emporix' <c>errorCode</c>, if present.</param>
    /// <param name="details">The entries from <c>details</c>, if present.</param>
    /// <param name="rawBody">The unparsed response body.</param>
    internal EmporixAuthenticationException(
        string message,
        HttpStatusCode statusCode = HttpStatusCode.Unauthorized,
        string? errorCode = null,
        IReadOnlyList<string>? details = null,
        string? rawBody = null)
        : base(message, statusCode, errorCode, details, rawBody)
    {
    }
}

/// <summary>
/// 403 — authenticated, but not permitted.
/// </summary>
public class EmporixForbiddenException : EmporixApiException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">Emporix' <c>errorCode</c>, if present.</param>
    /// <param name="details">The entries from <c>details</c>, if present.</param>
    /// <param name="rawBody">The unparsed response body.</param>
    internal EmporixForbiddenException(
        string message,
        string? errorCode = null,
        IReadOnlyList<string>? details = null,
        string? rawBody = null)
        : base(message, HttpStatusCode.Forbidden, errorCode, details, rawBody)
    {
    }
}

/// <summary>
/// A 403 where Emporix names the missing scope.
/// </summary>
/// <remarks>
/// Derives from <see cref="EmporixForbiddenException"/> so an existing catch on
/// 403 still applies.
/// </remarks>
public sealed class EmporixInsufficientScopeException : EmporixForbiddenException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="requiredScope">The scope Emporix reports as missing.</param>
    /// <param name="errorCode">Emporix' <c>errorCode</c>, if present.</param>
    /// <param name="details">The entries from <c>details</c>, if present.</param>
    /// <param name="rawBody">The unparsed response body.</param>
    internal EmporixInsufficientScopeException(
        string message,
        string? requiredScope,
        string? errorCode = null,
        IReadOnlyList<string>? details = null,
        string? rawBody = null)
        : base(message, errorCode, details, rawBody)
    {
        RequiredScope = requiredScope;
    }

    /// <summary>
    /// The scope Emporix reports as missing, exactly as it appeared in the response.
    /// </summary>
    public string? RequiredScope { get; }
}

/// <summary>404 — the requested resource does not exist.</summary>
public sealed class EmporixNotFoundException : EmporixApiException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">Emporix' <c>errorCode</c>, if present.</param>
    /// <param name="details">The entries from <c>details</c>, if present.</param>
    /// <param name="rawBody">The unparsed response body.</param>
    internal EmporixNotFoundException(
        string message,
        string? errorCode = null,
        IReadOnlyList<string>? details = null,
        string? rawBody = null)
        : base(message, HttpStatusCode.NotFound, errorCode, details, rawBody)
    {
    }
}

/// <summary>
/// 400 or 422 — Emporix rejected the request.
/// </summary>
/// <remarks>
/// What exactly was objected to is in <see cref="EmporixApiException.Details"/>.
/// Emporix supplies these as free text, not as field-value pairs — the SDK
/// forwards them unchanged rather than inventing a structure the API does not
/// promise.
/// </remarks>
public sealed class EmporixValidationException : EmporixApiException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code, typically 400 or 422.</param>
    /// <param name="errorCode">Emporix' <c>errorCode</c>, if present.</param>
    /// <param name="details">The entries from <c>details</c>, if present.</param>
    /// <param name="rawBody">The unparsed response body.</param>
    internal EmporixValidationException(
        string message,
        HttpStatusCode statusCode = HttpStatusCode.BadRequest,
        string? errorCode = null,
        IReadOnlyList<string>? details = null,
        string? rawBody = null)
        : base(message, statusCode, errorCode, details, rawBody)
    {
    }
}

/// <summary>
/// 429 — the rate limit is exhausted.
/// </summary>
/// <remarks>
/// The SDK already retries 429 responses for idempotent calls. This error means
/// the final attempt was rejected too. Wait <see cref="RetryAfter"/> before
/// trying again.
/// </remarks>
public sealed class EmporixRateLimitException : EmporixApiException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="retryAfter">The value of the <c>Retry-After</c> header.</param>
    /// <param name="errorCode">Emporix' <c>errorCode</c>, if present.</param>
    /// <param name="details">The entries from <c>details</c>, if present.</param>
    /// <param name="rawBody">The unparsed response body.</param>
    internal EmporixRateLimitException(
        string message,
        TimeSpan? retryAfter = null,
        string? errorCode = null,
        IReadOnlyList<string>? details = null,
        string? rawBody = null)
        : base(message, HttpStatusCode.TooManyRequests, errorCode, details, rawBody)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// The wait the server asked for via <c>Retry-After</c>, or
    /// <see langword="null"/> when the header was absent or unreadable.
    /// </summary>
    /// <remarks>
    /// Uncapped: the SDK bounds this value by
    /// <see cref="EmporixRetryOptions.MaxBackoff"/> for its own retries but
    /// forwards it verbatim here — what you do with it is your decision.
    /// </remarks>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>5xx — Emporix reports a server-side problem.</summary>
public sealed class EmporixServerException : EmporixApiException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="errorCode">Emporix' <c>errorCode</c>, if present.</param>
    /// <param name="details">The entries from <c>details</c>, if present.</param>
    /// <param name="rawBody">The unparsed response body.</param>
    internal EmporixServerException(
        string message,
        HttpStatusCode statusCode = HttpStatusCode.InternalServerError,
        string? errorCode = null,
        IReadOnlyList<string>? details = null,
        string? rawBody = null)
        : base(message, statusCode, errorCode, details, rawBody)
    {
    }
}

/// <summary>
/// The request never reached Emporix, or no usable response came back.
/// </summary>
/// <remarks>
/// There is no status code here because there was no response. Catch this type
/// to handle network and timeout problems together.
/// </remarks>
public abstract class EmporixTransportException : EmporixException
{
    private protected EmporixTransportException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The request exceeded its time limit.</summary>
public sealed class EmporixTimeoutException : EmporixTransportException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="timeout">The time limit that was exceeded.</param>
    /// <param name="innerException">The triggering exception, if any.</param>
    internal EmporixTimeoutException(string message, TimeSpan timeout, Exception? innerException = null)
        : base(message, innerException)
    {
        Timeout = timeout;
    }

    /// <summary>The time limit that was exceeded.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>
/// The connection failed — DNS, TLS, or an abort mid-transfer.
/// </summary>
public sealed class EmporixNetworkException : EmporixTransportException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The triggering exception, if any.</param>
    internal EmporixNetworkException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The configuration does not allow the requested call.
/// </summary>
/// <remarks>
/// Unlike the other errors this one arises without any network traffic — for
/// example when a call needs a service token but no backend credentials are
/// configured. A setup mistake, not a runtime problem.
/// </remarks>
public sealed class EmporixConfigurationException : EmporixException
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="message">The error message.</param>
    internal EmporixConfigurationException(string message)
        : base(message)
    {
    }
}
