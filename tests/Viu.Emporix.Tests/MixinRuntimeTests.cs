using System.Text.Json;
using Viu.Emporix.Mixins;

namespace Viu.Emporix.Tests;

/// <summary>
/// Typed access to a tenant's mixins.
/// </summary>
/// <remarks>
/// The descriptor carries a <c>JsonTypeInfo</c> because a generated serializer
/// context has no resolver for an arbitrary runtime type: assigning a POCO to an
/// <c>object?</c> mixin property throws <c>NotSupportedException</c>, and it does
/// so with reflection enabled too. Going through the consumer's own type
/// information is the only path that works, not merely the AOT-safe one.
/// </remarks>
public class MixinRuntimeTests
{
    private static MixinDescriptor<TestDeliveryMixin> Delivery => new()
    {
        Key = "deliveryOptions",
        Entity = "PRODUCT",
        Url = "https://cdn.emporix.io/deliveryOptionsMixIn.v6.json",
        Version = 6,
        TypeInfo = TestJsonContext.Default.TestDeliveryMixin,
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Packaging"] = "packaging",
            ["Weight"] = "weight",
            ["Note"] = "note",
        },
    };

    [Fact]
    public void A_mixin_is_read_from_the_container()
    {
        JsonElement mixins = JsonSerializer.Deserialize(
            """{"deliveryOptions":{"packaging":"Paper","weight":2.5}}""",
            TestJsonContext.Default.JsonElement);

        TestDeliveryMixin? value = MixinReader.Read(mixins, Delivery);

        Assert.Equal("Paper", value?.Packaging);
        Assert.Equal(2.5, value?.Weight);
    }

    [Fact]
    public void An_absent_mixin_reads_as_null_rather_than_throwing()
    {
        JsonElement mixins = JsonSerializer.Deserialize(
            """{"somethingElse":{}}""", TestJsonContext.Default.JsonElement);

        Assert.Null(MixinReader.Read(mixins, Delivery));
    }

    [Fact]
    public void A_container_that_is_not_an_object_reads_as_null()
    {
        // An entity carrying no mixins at all leaves the property null, and some
        // services have been seen sending an empty string instead of an object.
        Assert.Null(MixinReader.Read(null, Delivery));
        Assert.Null(MixinReader.Read(
            JsonSerializer.Deserialize("\"\"", TestJsonContext.Default.JsonElement), Delivery));
    }

    [Fact]
    public void The_saved_version_is_parsed_from_the_schema_url()
    {
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["deliveryOptions"] = "https://cdn.emporix.io/deliveryOptionsMixIn.v6.json",
        };

        Assert.Equal(6, MixinReader.SavedVersion(metadata, "deliveryOptions"));
    }

    [Fact]
    public void A_url_without_a_version_marker_yields_no_version()
    {
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["deliveryOptions"] = "https://cdn.emporix.io/deliveryOptions.json",
        };

        Assert.Null(MixinReader.SavedVersion(metadata, "deliveryOptions"));
        Assert.Null(MixinReader.SavedVersion(metadata, "absentKey"));
        Assert.Null(MixinReader.SavedVersion((IDictionary<string, string>?)null, "deliveryOptions"));
    }

    [Fact]
    public void The_saved_version_is_also_read_from_an_object_typed_metadata_property()
    {
        // Fourteen specifications type metadata.mixins as IDictionary<string,
        // string>; the other hundred-odd type the very same concept as object,
        // which deserializes to a JsonElement. Both must work.
        JsonElement metadata = JsonSerializer.Deserialize(
            """{"deliveryOptions":"https://cdn.emporix.io/deliveryOptionsMixIn.v7.json"}""",
            TestJsonContext.Default.JsonElement);

        Assert.Equal(7, MixinReader.SavedVersion(metadata, "deliveryOptions"));
    }
}
