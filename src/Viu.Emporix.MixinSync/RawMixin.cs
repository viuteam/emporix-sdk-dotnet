namespace Viu.Emporix.MixinSync;

/// <summary>
/// One mixin, normalized from the Schema Service.
/// </summary>
/// <remarks>
/// This is what the snapshot file holds, so <c>generate</c> can run without a
/// tenant and without the network. A schema assigned to several entity types
/// yields one of these per type.
/// </remarks>
public sealed class RawMixin
{
    /// <summary>The schema id, which is the key under <c>mixins</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>One entity type the schema is assigned to.</summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>The version Emporix assigned.</summary>
    public int Version { get; set; }

    /// <summary>The hosted schema URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The JSON Schema itself, as text.</summary>
    /// <remarks>
    /// Kept as text rather than parsed: it is hashed for the lockfile and handed
    /// to NJsonSchema, and both want the original bytes.
    /// </remarks>
    public string Schema { get; set; } = string.Empty;
}
