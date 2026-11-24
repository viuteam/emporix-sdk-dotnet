using System.Text.Json;
using System.Text.Json.Serialization;
using Viu.Emporix.AvailabilityModels;

namespace Viu.Emporix;

/// <summary>
/// One step of a partial update to a stored availability.
/// </summary>
/// <remarks>
/// <para>
/// The SDK's own type rather than the generated
/// <c>AvailabilityModels.AvailabilityPatchOperation</c>, for the reason
/// <see cref="MediaPatchOperation"/> gives: the generated one declares its value
/// as <c>object</c>, which a source-generated context can write only when the
/// runtime type inside happens to be registered. <see cref="JsonElement"/>
/// writes itself verbatim.
/// </para>
/// <para>
/// The operation names are the specification's, <c>REPLACE</c> and
/// <c>REMOVE</c>, in upper case. JSON Patch spells them in lower case, and the
/// approval service rejects the upper-case spelling its own specification
/// declares. Whether the availability service accepts it is not verified
/// against a live tenant.
/// </para>
/// </remarks>
public sealed class AvailabilityChange
{
    /// <summary>
    /// What to do at the path: <c>REPLACE</c> sets the field, <c>REMOVE</c>
    /// removes it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AvailabilityPatchOperationOp>))]
    [JsonPropertyName("op")]
    public required AvailabilityPatchOperationOp Op { get; init; }

    /// <summary>Which field, as a JSON Pointer — <c>/stockLevel</c>, say.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>
    /// The new value; omitted for <see cref="AvailabilityPatchOperationOp.REMOVE"/>.
    /// </summary>
    /// <remarks>
    /// Build it with <c>JsonSerializer.SerializeToElement</c> and a
    /// <c>JsonTypeInfo</c>, or parse it from text.
    /// </remarks>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }
}

/// <summary>
/// The changes to one product's availability on one site, as an entry of a
/// bulk partial update.
/// </summary>
public sealed class AvailabilityBulkPatch
{
    /// <summary>The product.</summary>
    [JsonPropertyName("productId")]
    public required string ProductId { get; init; }

    /// <summary>The site code.</summary>
    [JsonPropertyName("site")]
    public required string Site { get; init; }

    /// <summary>The JSON Patch operations to apply.</summary>
    [JsonPropertyName("data")]
    public required IReadOnlyList<AvailabilityChange> Changes { get; init; }
}
