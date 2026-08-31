using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Viu.Emporix;

/// <summary>
/// Translates an error response into the matching <see cref="EmporixApiException"/>.
/// </summary>
/// <remarks>
/// Emporix uses two error formats side by side: the documented
/// <c>{ code, status, message, details[], errorCode }</c> and, for 401 responses
/// from the upstream gateway,
/// <c>{ fault: { faultstring, detail: { errorcode } } }</c>. Both are read.
/// <para>
/// Parsing goes exclusively through <see cref="JsonDocument"/>: reflection-free
/// and therefore AOT-safe. More importantly, an unexpected body never throws
/// here — an HTML error page from a proxy must not raise a
/// <see cref="JsonException"/> and hide the actual HTTP information. Whatever
/// cannot be read stays available through <c>RawBody</c>.
/// </para>
/// </remarks>
internal static partial class EmporixErrorParser
{
    /// <summary>
    /// Detects the missing-scope hint inside the free-text error details.
    /// </summary>
    [GeneratedRegex(@"missing scope[:\s]+([a-zA-Z0-9._-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MissingScopePattern { get; }

    /// <summary>
    /// Builds the exception matching the status code.
    /// </summary>
    /// <param name="statusCode">The status code of the response.</param>
    /// <param name="requestDescription">Short description of the call, e.g. <c>GET /product/acme/products</c>.</param>
    /// <param name="body">The response body, if it was read.</param>
    /// <param name="retryAfter">The parsed <c>Retry-After</c> header.</param>
    public static EmporixApiException CreateException(
        HttpStatusCode statusCode,
        string requestDescription,
        string? body,
        TimeSpan? retryAfter = null)
    {
        (string? parsedMessage, string? errorCode, IReadOnlyList<string> details) = Parse(body);

        string message = parsedMessage is { Length: > 0 }
            ? $"{requestDescription} → {(int)statusCode}: {parsedMessage}"
            : $"{requestDescription} → {(int)statusCode}";

        return statusCode switch
        {
            HttpStatusCode.Unauthorized
                => new EmporixAuthenticationException(message, statusCode, errorCode, details, body),

            HttpStatusCode.Forbidden when FindMissingScope(details) is { } scope
                => new EmporixInsufficientScopeException(message, scope, errorCode, details, body),

            HttpStatusCode.Forbidden
                => new EmporixForbiddenException(message, errorCode, details, body),

            HttpStatusCode.NotFound
                => new EmporixNotFoundException(message, errorCode, details, body),

            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                => new EmporixValidationException(message, statusCode, errorCode, details, body),

            HttpStatusCode.TooManyRequests
                => new EmporixRateLimitException(message, retryAfter, errorCode, details, body),

            _ when (int)statusCode >= 500
                => new EmporixServerException(message, statusCode, errorCode, details, body),

            _ => new EmporixApiException(message, statusCode, errorCode, details, body),
        };
    }

    /// <summary>
    /// Reads message, error code and details from the response body. Unreadable
    /// bodies yield empty values rather than an exception.
    /// </summary>
    internal static (string? Message, string? ErrorCode, IReadOnlyList<string> Details) Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null, []);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null, []);
            }

            // The gateway format Emporix returns for 401. Checked first because
            // such a body carries none of the standard format's fields.
            if (root.TryGetProperty("fault", out JsonElement fault)
                && fault.ValueKind == JsonValueKind.Object)
            {
                string? faultMessage = ReadString(fault, "faultstring");
                string? faultCode = fault.TryGetProperty("detail", out JsonElement faultDetail)
                    && faultDetail.ValueKind == JsonValueKind.Object
                        ? ReadString(faultDetail, "errorcode")
                        : null;

                return (faultMessage, faultCode, []);
            }

            return (
                ReadString(root, "message"),
                ReadString(root, "errorCode"),
                ReadDetails(root));
        }
        catch (JsonException)
        {
            // Not JSON — the body remains available through RawBody.
            return (null, null, []);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static List<string> ReadDetails(JsonElement root)
    {
        if (!root.TryGetProperty("details", out JsonElement details)
            || details.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> result = new(details.GetArrayLength());

        foreach (JsonElement detail in details.EnumerateArray())
        {
            // Emporix specifies strings. An entry of another shape is taken as
            // raw JSON text — nothing is lost, and no model is needed that the
            // API does not promise.
            string? text = detail.ValueKind switch
            {
                JsonValueKind.String => detail.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => detail.GetRawText(),
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static string? FindMissingScope(IReadOnlyList<string> details)
    {
        foreach (string detail in details)
        {
            Match match = MissingScopePattern.Match(detail);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }
}
