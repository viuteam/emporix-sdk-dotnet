using Microsoft.Extensions.Logging;

namespace Viu.Emporix;

/// <summary>
/// The SDK's log messages, emitted through the <c>LoggerMessage</c> generator.
/// </summary>
/// <remarks>
/// Source-generated rather than routed through the <c>Log*</c> extension
/// methods: that avoids boxing and formatting work when the level is disabled,
/// and needs no reflection — both requirements from ADR-0004.
/// <para>
/// None of these lines print a token, a secret or customer data. The session id
/// is included on purpose: it authorises nothing, but it is the field a cart
/// problem can be pinned to.
/// </para>
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Requesting service token for credential set {CredentialSet}.")]
    public static partial void RequestingServiceToken(ILogger logger, string credentialSet);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Obtained service token for credential set {CredentialSet}, valid for {ExpiresIn}s.")]
    public static partial void ServiceTokenObtained(ILogger logger, string credentialSet, int expiresIn);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Obtaining anonymous session ({Mode}).")]
    public static partial void RequestingAnonymousSession(ILogger logger, string mode);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Obtained anonymous session ({Mode}): SessionId {SessionId}, valid for {ExpiresIn}s.")]
    public static partial void AnonymousSessionObtained(
        ILogger logger,
        string mode,
        string sessionId,
        int expiresIn);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Renewing the anonymous session failed; starting a new one. "
            + "An existing guest cart is lost in the process.")]
    public static partial void AnonymousRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Debug,
        Message = "Response {StatusCode} for {Method} {Path} (attempt {Attempt}), retrying in {DelayMs}ms.")]
    public static partial void RetryingRequest(
        ILogger logger,
        string method,
        string path,
        int statusCode,
        int attempt,
        double delayMs);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Debug,
        Message = "401 on a {AuthKind} token — obtaining a fresh one and retrying once.")]
    public static partial void ReauthenticatingAfterUnauthorized(ILogger logger, AuthKind authKind);

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Warning,
        Message = "Skipped {Count} product codes: they contain characters Emporix' query language "
            + "uses as delimiters and cannot escape inside a list.")]
    public static partial void DroppedCodesWithDelimiters(ILogger logger, int count);
}
