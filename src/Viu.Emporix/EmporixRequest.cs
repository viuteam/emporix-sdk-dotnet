using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Viu.Emporix;

/// <summary>
/// Describes a call against the Emporix API.
/// </summary>
internal sealed class EmporixRequest
{
    /// <summary>The HTTP method.</summary>
    public required HttpMethod Method { get; init; }

    /// <summary>The path relative to the configured host, with a leading slash.</summary>
    public required string Path { get; init; }

    /// <summary>What this call is authorised with.</summary>
    public required AuthContext Auth { get; init; }

    /// <summary>
    /// The query parameters. Entries without a value are omitted so an unset
    /// optional parameter does not arrive as an empty value.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string?>>? Query { get; init; }

    /// <summary>The request body.</summary>
    public HttpContent? Content { get; init; }

    /// <summary>Additional headers for this call.</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? Headers { get; init; }

    /// <summary>
    /// Declares a <c>POST</c> or <c>PATCH</c> safe to repeat.
    /// </summary>
    /// <remarks>
    /// Only set this when a second call demonstrably has no second effect. For
    /// <c>GET</c>, <c>PUT</c> and <c>DELETE</c> it is unnecessary — those count
    /// as repeatable anyway.
    /// </remarks>
    public bool Idempotent { get; init; }
}

/// <summary>Builds JSON request bodies.</summary>
internal static class EmporixJsonContent
{
    /// <summary>
    /// Serializes <paramref name="value"/> into a JSON body.
    /// </summary>
    /// <remarks>
    /// The result is a byte array and can therefore be sent any number of times —
    /// exactly what a retry after a 5xx or 401 needs. Serialization goes through
    /// the supplied type information, not reflection (ADR-0004).
    /// </remarks>
    public static HttpContent Create<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        ByteArrayContent content = new(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };

        return content;
    }
}
