using System.Text.Json.Serialization.Metadata;

namespace Viu.Emporix.Mixins;

/// <summary>
/// One of a tenant's mixins: where it hangs, which schema describes it, and how
/// to serialize it.
/// </summary>
/// <remarks>
/// <para>
/// Produced by <c>Viu.Emporix.MixinSync</c> into the consumer's repository, one
/// per mixin. Writing one by hand is supported and is the way to use a mixin
/// without adopting the generator.
/// </para>
/// <para>
/// <see cref="TypeInfo"/> is the reason this type exists rather than a plain
/// string key. A generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// resolves no arbitrary runtime type, so serializing a mixin value requires the
/// consumer's own type information — the SDK never resolves it. That keeps the
/// path reflection-free, as ADR-0004 requires, and it is also the only thing
/// that works: assigning a plain object to an <c>object?</c> mixin property
/// throws <see cref="System.NotSupportedException"/> even with reflection enabled.
/// </para>
/// </remarks>
/// <typeparam name="T">The generated type for this mixin's schema.</typeparam>
public sealed class MixinDescriptor<T>
{
    /// <summary>The key the value sits under in <c>entity.mixins</c>.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// The entity type the schema is assigned to, for example <c>PRODUCT</c>.
    /// </summary>
    /// <remarks>
    /// Informational: it makes the generated registry readable and lets an error
    /// name where a mixin belongs. It deliberately does not decide whether a
    /// query may use <c>compoundLogicalQuery</c> — that capability belongs to the
    /// service being called, not to the entity. A schema assigned to several
    /// entity types yields one descriptor each.
    /// </remarks>
    public required string Entity { get; init; }

    /// <summary>
    /// The hosted schema URL, written to <c>entity.metadata.mixins[key]</c>.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>The schema version Emporix assigned.</summary>
    public required int Version { get; init; }

    /// <summary>Serialization metadata from the consumer's own context.</summary>
    public required JsonTypeInfo<T> TypeInfo { get; init; }

    /// <summary>
    /// CLR property name to JSON attribute name.
    /// </summary>
    /// <remarks>
    /// Lets the query builder turn a property selector into an attribute path
    /// without reading any metadata reflectively. The generator parses this out
    /// of the code it emitted rather than recomputing the names, because the
    /// conversion and the emitted result can diverge.
    /// </remarks>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
}
