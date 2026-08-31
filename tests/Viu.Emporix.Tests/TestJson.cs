using System.Text.Json.Serialization;

namespace Viu.Emporix.Tests;

/// <summary>A sample record for the HTTP core tests.</summary>
internal sealed class TestProduct
{
    public string? Id { get; set; }

    public string? Name { get; set; }
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
internal sealed partial class TestJsonContext : JsonSerializerContext;
