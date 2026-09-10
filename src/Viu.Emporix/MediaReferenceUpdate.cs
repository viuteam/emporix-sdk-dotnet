using System.Text.Json.Serialization;

namespace Viu.Emporix.MediaModels;

/// <summary>
/// The body for changing which products an asset belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The specification models asset updates as a union of «blob» and «link», and
/// neither half carries <c>refIds</c> even though the endpoint accepts them —
/// so the generated types cannot express this call. The type discriminator is
/// sent back unchanged, because leaving it out makes the body match neither
/// half of the union.
/// </para>
/// <para>
/// <c>access</c> is echoed back for the same reason, and its absence used to
/// make this body fail outright: the API answered «asset.access: must not be
/// null» and then «Cannot assign media of private access to other entity»,
/// having read the missing value as private. Every attach and detach failed
/// with a 400 until this was added. Found against tenant viu on 2026-09-10 —
/// the media service is not part of the smoke test, so nothing had exercised
/// the call.
/// </para>
/// </remarks>
public sealed class AssetReferenceUpdate
{
    /// <summary>The asset's own type, echoed back unchanged.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>The asset's own access, echoed back unchanged.</summary>
    [JsonPropertyName("access")]
    public AssetAccess? Access { get; init; }

    /// <summary>
    /// The asset's own link, echoed back unchanged; <see langword="null"/> for a
    /// blob.
    /// </summary>
    /// <remarks>
    /// The endpoint replaces the asset rather than merging into it, so a link
    /// asset has to carry its link: without it the API answered «'url' must be
    /// provided for assets of 'LINK' type».
    /// </remarks>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>
    /// The version the change is based on, echoed back from the asset that was
    /// read.
    /// </summary>
    /// <remarks>
    /// Optimistic locking, and not optional: without it the API answered
    /// «`metadata.version` is required for update». Echoing the version just
    /// read is what makes a concurrent change lose rather than pass unnoticed.
    /// </remarks>
    [JsonPropertyName("metadata")]
    public MetadataUpdate? Metadata { get; init; }

    /// <summary>What the asset belongs to.</summary>
    [JsonPropertyName("refIds")]
    public required IReadOnlyList<RefId> RefIds { get; init; }
}
