using System.Globalization;
using System.Text.Json;

namespace Viu.Emporix.Mixins;

/// <summary>
/// Reads typed mixin values off an entity.
/// </summary>
/// <remarks>
/// Takes the mixin container rather than the entity. C# is nominally typed, the
/// generated entity classes share no interface, and the same concept is modelled
/// two ways across the specifications — so handing in <c>product.Mixins</c> is
/// what works everywhere without changing generated code.
/// </remarks>
public static class MixinReader
{
    /// <summary>Reads one mixin, or <see langword="null"/> when it is absent.</summary>
    /// <param name="mixins">The entity's <c>mixins</c> property.</param>
    /// <param name="descriptor">Which mixin to read.</param>
    /// <typeparam name="T">The mixin's generated type.</typeparam>
    /// <returns>The value, or <see langword="null"/> when the mixin is not set.</returns>
    public static T? Read<T>(object? mixins, MixinDescriptor<T> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // Deserializing an object-typed property yields a JsonElement; anything
        // else means the entity carries no mixin object to read from.
        if (mixins is not JsonElement { ValueKind: JsonValueKind.Object } container)
        {
            return default;
        }

        return container.TryGetProperty(descriptor.Key, out JsonElement value)
            ? value.Deserialize(descriptor.TypeInfo)
            : default;
    }

    /// <summary>
    /// The schema version an entity was saved with, parsed from its metadata.
    /// </summary>
    /// <param name="metadataMixins">The entity's <c>metadata.mixins</c> map.</param>
    /// <param name="key">The mixin key.</param>
    /// <returns>The version, or <see langword="null"/> when absent or unparseable.</returns>
    /// <remarks>
    /// Compare against <see cref="MixinDescriptor{T}.Version"/> to detect that a
    /// tenant's schema moved on while the loaded type did not.
    /// </remarks>
    public static int? SavedVersion(IDictionary<string, string>? metadataMixins, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return metadataMixins is not null && metadataMixins.TryGetValue(key, out string? url)
            ? VersionFromUrl(url)
            : null;
    }

    /// <summary>
    /// The schema version, for the specifications that type the same map as
    /// <c>object</c> rather than as a dictionary.
    /// </summary>
    /// <param name="metadataMixins">The entity's <c>metadata.mixins</c> property.</param>
    /// <param name="key">The mixin key.</param>
    /// <returns>The version, or <see langword="null"/> when absent or unparseable.</returns>
    public static int? SavedVersion(object? metadataMixins, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (metadataMixins is not JsonElement { ValueKind: JsonValueKind.Object } container
            || !container.TryGetProperty(key, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return VersionFromUrl(value.GetString());
    }

    // Emporix puts the version in the file name: «…deliveryOptionsMixIn.v6.json».
    private static int? VersionFromUrl(string? url)
    {
        if (url is null)
        {
            return null;
        }

        int marker = url.LastIndexOf(".v", StringComparison.Ordinal);

        if (marker < 0)
        {
            return null;
        }

        ReadOnlySpan<char> tail = url.AsSpan(marker + 2);
        int end = tail.IndexOf('.');

        return int.TryParse(
            end < 0 ? tail : tail[..end],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int version)
            ? version
            : null;
    }
}
