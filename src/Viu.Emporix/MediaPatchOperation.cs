using System.Text.Json;
using System.Text.Json.Serialization;
using Viu.Emporix.MediaModels;

namespace Viu.Emporix;

/// <summary>
/// One step of a partial update to a media asset.
/// </summary>
/// <remarks>
/// <para>
/// The SDK's own type rather than the generated <c>MediaModels.PatchOperation</c>,
/// for one reason: that one declares its value as <c>object</c>, and an
/// <c>object</c> cannot be written by a source-generated context unless the
/// runtime type inside it happens to be registered there. Probed against tenant
/// viu, the difference decides at runtime rather than at compile time — a
/// <c>RefId</c> went out fine, an array of two of them threw
/// <see cref="NotSupportedException"/>, and so did a boxed
/// <see cref="JsonElement"/>. Two calls that look the same, one of which works.
/// </para>
/// <para>
/// <see cref="JsonElement"/> writes itself verbatim and needs nothing
/// registered, so any shape the endpoint accepts can be sent, and reflection
/// stays out of it (ADR-0004). The same reasoning produced
/// <see cref="AiPatchOperation"/>; this type differs only in reusing the
/// generated operation enum, whose members carry the values the specification
/// declares.
/// </para>
/// </remarks>
public sealed class MediaPatchOperation
{
    /// <summary>What to do at the path.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PatchOperationOp>))]
    [JsonPropertyName("op")]
    public required PatchOperationOp Op { get; init; }

    /// <summary>
    /// Which field, as a JSON Pointer — <c>/url</c>, or <c>/refIds/-</c> to
    /// append to the reference list.
    /// </summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>
    /// The new value; omitted for <see cref="PatchOperationOp.Remove"/>.
    /// </summary>
    /// <remarks>
    /// Build it with <c>JsonSerializer.SerializeToElement</c> and a
    /// <c>JsonTypeInfo</c>, or parse it from text. A plain object would not
    /// survive trimming.
    /// </remarks>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }
}
