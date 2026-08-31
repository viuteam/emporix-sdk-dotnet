using System.Text.Json.Serialization;

namespace Viu.Emporix.MediaModels;

/// <summary>
/// The body for changing which products an asset belongs to.
/// </summary>
/// <remarks>
/// The specification models asset updates as a union of «blob» and «link», and
/// neither half carries <c>refIds</c> even though the endpoint accepts them —
/// so the generated types cannot express this call. The type discriminator is
/// sent back unchanged, because leaving it out makes the body match neither
/// half of the union.
/// </remarks>
public sealed class AssetReferenceUpdate
{
    /// <summary>The asset's own type, echoed back unchanged.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>What the asset belongs to.</summary>
    [JsonPropertyName("refIds")]
    public required IReadOnlyList<RefId> RefIds { get; init; }
}
