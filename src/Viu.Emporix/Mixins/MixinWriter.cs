using System.Text.Json;

namespace Viu.Emporix.Mixins;

/// <summary>
/// Assembles mixin values and their schema URLs for writing onto an entity.
/// </summary>
/// <remarks>
/// <para>
/// Emporix wants both halves: the value under <c>mixins[key]</c> and the schema
/// URL under <c>metadata.mixins[key]</c>. Without the second, the mixin is
/// stored unvalidated. The Node SDK calls this «the part consumers get wrong».
/// </para>
/// <para>
/// The two halves are returned separately and the caller assigns both, because
/// no interface spans the entity types that carry mixins:
/// </para>
/// <code>
/// var w = MixinWriter.Create().Set(Mixins.Delivery, value);
/// product.Mixins = w.Values;
/// product.Metadata.Mixins = w.SchemaUrls;
/// </code>
/// <para>
/// Whether a null attribute reaches the wire is decided by the mixin's own
/// serializer context, not here: every generated context sets
/// <c>DefaultIgnoreCondition = WhenWritingNull</c>, since a schema declaring
/// <c>additionalProperties: false</c> has no use for an explicit null.
/// </para>
/// </remarks>
public sealed class MixinWriter
{
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _schemaUrls = new(StringComparer.Ordinal);

    private MixinWriter()
    {
    }

    /// <summary>Starts a new writer.</summary>
    public static MixinWriter Create() => new();

    /// <summary>Sets one mixin's value.</summary>
    /// <param name="descriptor">Which mixin to set.</param>
    /// <param name="value">The value.</param>
    /// <typeparam name="T">The mixin's generated type.</typeparam>
    /// <returns>The same writer, for chaining.</returns>
    public MixinWriter Set<T>(MixinDescriptor<T> descriptor, T value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        _values[descriptor.Key] = JsonSerializer.SerializeToElement(value, descriptor.TypeInfo);
        _schemaUrls[descriptor.Key] = descriptor.Url;

        return this;
    }

    /// <summary>The value for the entity's <c>mixins</c> property.</summary>
    public JsonElement Values => JsonSerializer.SerializeToElement(
        _values, MixinJsonContext.Default.DictionaryStringJsonElement);

    /// <summary>
    /// The value for the entity's <c>metadata.mixins</c> property.
    /// </summary>
    public IDictionary<string, string> SchemaUrls => _schemaUrls;
}
