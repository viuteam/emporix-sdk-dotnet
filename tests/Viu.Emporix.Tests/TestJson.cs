using System.Text.Json;
using System.Text.Json.Serialization;

namespace Viu.Emporix.Tests;

/// <summary>A sample record for the HTTP core tests.</summary>
internal sealed class TestProduct
{
    public string? Id { get; set; }

    public string? Name { get; set; }
}

/// <summary>A stand-in for a generated mixin type.</summary>
/// <remarks>
/// Shaped like what the generator emits: an explicit <c>JsonPropertyName</c> on
/// every attribute, all of them optional, and one nested object for a localized
/// field.
/// </remarks>
internal sealed class TestDeliveryMixin
{
    [JsonPropertyName("packaging")]
    public string? Packaging { get; set; }

    [JsonPropertyName("weight")]
    public double? Weight { get; set; }

    [JsonPropertyName("note")]
    public TestLocalizedNote? Note { get; set; }
}

/// <summary>The nested object of a localized mixin attribute.</summary>
internal sealed class TestLocalizedNote
{
    [JsonPropertyName("en")]
    public string? En { get; set; }
}

/// <summary>
/// The source-generated serialization context for the tests.
/// </summary>
/// <remarks>
/// The tests go through generated type information too — the same requirement as
/// the SDK itself (ADR-0004). A test falling back on reflection would fail to
/// exercise AOT compatibility exactly where it matters.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TestProduct))]
[JsonSerializable(typeof(List<TestProduct>))]
[JsonSerializable(typeof(LocalizedString))]
[JsonSerializable(typeof(TestDeliveryMixin))]
[JsonSerializable(typeof(TestLocalizedNote))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

/// <summary>
/// A stand-in for a context the mixin generator emits.
/// </summary>
/// <remarks>
/// Separate from <see cref="TestJsonContext"/> for one reason: it sets
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/>, which every generated
/// mixin context does. A schema declaring <c>additionalProperties: false</c> has
/// no use for an explicit null, so suppressing them belongs to the context that
/// describes the mixin — not to the writer, which would otherwise need its own
/// serializer options and a resolver to go with them.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TestDeliveryMixin))]
[JsonSerializable(typeof(TestLocalizedNote))]
internal sealed partial class TestMixinContext : JsonSerializerContext;
