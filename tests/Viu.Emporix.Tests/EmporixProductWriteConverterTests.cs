using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix.Tests;

/// <summary>
/// Writing a product as the type the specification's oneOf names.
/// </summary>
/// <remarks>
/// Four of the five write bodies in specs/product.yml are a oneOf over five
/// type-specific schemas, and the SDK sent the basic shape for all of them. The
/// converters here are pure functions — object in, JSON out, no HTTP — which is
/// why these tests carry weight. What they cannot establish is whether Emporix
/// accepts the bodies; that needs the smoke test.
/// </remarks>
public class EmporixProductWriteConverterTests
{
    [Fact]
    public void The_five_creation_types_carry_the_creation_interface()
    {
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(BasicProductCreation)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(BundleProductCreation)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(ParentVariantProductCreation)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(VariantProductCreation)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(DynamicVariantProductCreation)));
    }

    [Fact]
    public void The_five_update_types_carry_the_update_interface()
    {
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(BasicProductUpdate)));
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(BundleProductUpdate)));
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(ParentVariantProductUpdate)));
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(VariantProductUpdate)));
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(DynamicVariantProductUpdate)));
    }

    [Fact]
    public void The_five_bulk_types_carry_the_bulk_interface()
    {
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(BasicProductBulkUpdate)));
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(BundleProductBulkUpdate)));
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(ParentVariantProductBulkUpdate)));
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(VariantProductBulkUpdate)));
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(DynamicVariantProductBulkUpdate)));
    }

    [Fact]
    public void The_generated_hierarchy_lets_the_wrong_type_satisfy_an_interface()
    {
        // Not a wish, a fact about the generated code — and the whole reason the
        // converters dispatch on exact runtime type. Asserted so that a spec
        // sync which happened to break these inheritances would show up here,
        // where the comment explains why anyone cared.
        //
        // A bulk update derives from its plain update:
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(BasicProductBulkUpdate)));

        // And four response types derive from their creation type:
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(BasicProductWithId)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(BundleProductWithId)));

        // DynamicVariantProductWithId is the exception: it has no base at all,
        // which is also why the read side needed explicit members for it.
        Assert.False(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(DynamicVariantProductWithId)));
    }

    // ---------- Each type reaches the wire in its own shape ----------

    [Fact]
    public void Each_creation_type_serializes_as_itself()
    {
        // The distinguishing fields come from the generated code: a bundle's
        // bundledProducts and a parent variant's variantAttributes are
        // non-nullable with default instances, so their keys are always
        // present; the other two are set here explicitly.
        Assert.DoesNotContain(
            "bundledProducts",
            Write<IEmporixProductCreation>(
                new BasicProductCreation { Code = "plain" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);

        Assert.Contains(
            "bundledProducts",
            Write<IEmporixProductCreation>(
                new BundleProductCreation { Code = "gift" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);

        Assert.Contains(
            "variantAttributes",
            Write<IEmporixProductCreation>(
                new ParentVariantProductCreation { Code = "shirt" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"parentVariantId\":\"parent-42\"",
            Write<IEmporixProductCreation>(
                new VariantProductCreation { Code = "red-m", ParentVariantId = "parent-42" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"dynamicVariantType\":\"H1_L1\"",
            Write<IEmporixProductCreation>(
                new DynamicVariantProductCreation { Code = "dyn", DynamicVariantType = "H1_L1" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Each_update_type_serializes_as_itself()
    {
        Assert.DoesNotContain(
            "bundledProducts",
            Write<IEmporixProductUpdate>(
                new BasicProductUpdate { Code = "plain" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "bundledProducts",
            Write<IEmporixProductUpdate>(
                new BundleProductUpdate { Code = "gift" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "variantAttributes",
            Write<IEmporixProductUpdate>(
                new ParentVariantProductUpdate { Code = "shirt" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"parentVariantId\":\"parent-42\"",
            Write<IEmporixProductUpdate>(
                new VariantProductUpdate { Code = "red-m", ParentVariantId = "parent-42" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"dynamicVariantType\":\"H1_L1\"",
            Write<IEmporixProductUpdate>(
                new DynamicVariantProductUpdate { Code = "dyn", DynamicVariantType = "H1_L1" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Each_bulk_type_serializes_as_itself_and_keeps_its_id()
    {
        // The id is what says which product to change, and it exists only on
        // the bulk types. Losing it is the failure mode this whole design
        // guards against.
        Assert.Contains(
            "\"id\":\"b1\"",
            Write<IEmporixProductBulkUpdate>(
                new BasicProductBulkUpdate { Id = "b1" },
                ProductJsonContext.Default.IEmporixProductBulkUpdate),
            StringComparison.Ordinal);

        string bundle = Write<IEmporixProductBulkUpdate>(
            new BundleProductBulkUpdate { Id = "g1" },
            ProductJsonContext.Default.IEmporixProductBulkUpdate);

        Assert.Contains("\"id\":\"g1\"", bundle, StringComparison.Ordinal);
        Assert.Contains("bundledProducts", bundle, StringComparison.Ordinal);

        Assert.Contains(
            "variantAttributes",
            Write<IEmporixProductBulkUpdate>(
                new ParentVariantProductBulkUpdate { Id = "p1" },
                ProductJsonContext.Default.IEmporixProductBulkUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"parentVariantId\":\"parent-42\"",
            Write<IEmporixProductBulkUpdate>(
                new VariantProductBulkUpdate { Id = "v1", ParentVariantId = "parent-42" },
                ProductJsonContext.Default.IEmporixProductBulkUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"dynamicVariantType\":\"H1_L1\"",
            Write<IEmporixProductBulkUpdate>(
                new DynamicVariantProductBulkUpdate { Id = "d1", DynamicVariantType = "H1_L1" },
                ProductJsonContext.Default.IEmporixProductBulkUpdate),
            StringComparison.Ordinal);
    }

    // ---------- No field is lost on the way out ----------

    [Fact]
    public void A_bundle_survives_the_round_trip_through_the_interface()
    {
        BundleProductCreation original = new()
        {
            Code = "gift",
            BundledProducts = { new BundledProduct { ProductId = "p1", Amount = 2 } },
        };

        string json = Write<IEmporixProductCreation>(
            original, ProductJsonContext.Default.IEmporixProductCreation);

        // Back through the concrete type: if the converter had serialized the
        // value through a base contract, the bundled products would be gone
        // and this would be an empty collection rather than a failure.
        BundleProductCreation? read = JsonSerializer.Deserialize(
            json, ProductJsonContext.Default.BundleProductCreation);

        Assert.Equal("gift", read?.Code);
        Assert.Single(read!.BundledProducts);
        Assert.Equal("p1", read.BundledProducts[0].ProductId);
        Assert.Equal(2, read.BundledProducts[0].Amount);
    }

    [Fact]
    public void Unset_properties_are_not_written_as_null()
    {
        // What makes a partial write safe: a body that sent every untouched
        // property as null would clear fields the caller never mentioned.
        // DefaultIgnoreCondition on ProductJsonContext is what guarantees it.
        string json = Write<IEmporixProductCreation>(
            new BasicProductCreation { Code = "plain" },
            ProductJsonContext.Default.IEmporixProductCreation);

        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\"", json, StringComparison.Ordinal);
    }

    // ---------- The two traps ----------

    [Fact]
    public void A_bulk_update_passed_as_a_plain_update_is_refused()
    {
        // BasicProductBulkUpdate derives from BasicProductUpdate, so this
        // compiles. A pattern match in the converter would serialize it
        // through the base contract and drop the id — the field that says
        // which product to change — with no error at all. Exact-type dispatch
        // turns that silence into this exception.
        IEmporixProductUpdate wrong = new BasicProductBulkUpdate { Id = "b1" };

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => Write(wrong, ProductJsonContext.Default.IEmporixProductUpdate));

        Assert.Contains("BasicProductBulkUpdate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_product_that_was_read_cannot_be_created()
    {
        // BasicProductWithId derives from BasicProductCreation, so this
        // compiles too. The message has to say why, or the exception reads as
        // nonsense to someone who just passed a product they fetched.
        IEmporixProductCreation wrong = new BasicProductWithId { Id = "b1", Code = "plain" };

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => Write(wrong, ProductJsonContext.Default.IEmporixProductCreation));

        Assert.Contains("BasicProductWithId", error.Message, StringComparison.Ordinal);
        Assert.Contains("derives from", error.Message, StringComparison.Ordinal);
    }

    // ---------- One array, five shapes ----------

    [Fact]
    public void A_mixed_bulk_array_writes_each_element_in_its_own_shape()
    {
        // The capability no per-type method could offer: the specification
        // declares PUT /products/bulk as an array whose items are a oneOf.
        List<IEmporixProductBulkUpdate> products =
        [
            new BasicProductBulkUpdate { Id = "b1" },
            new BundleProductBulkUpdate { Id = "g1" },
            new ParentVariantProductBulkUpdate { Id = "p1" },
            new VariantProductBulkUpdate { Id = "v1", ParentVariantId = "parent-42" },
            new DynamicVariantProductBulkUpdate { Id = "d1", DynamicVariantType = "H1_L1" },
        ];

        string json = JsonSerializer.Serialize(
            products, ProductJsonContext.Default.ListIEmporixProductBulkUpdate);

        Assert.Contains("\"id\":\"b1\"", json, StringComparison.Ordinal);
        Assert.Contains("bundledProducts", json, StringComparison.Ordinal);
        Assert.Contains("variantAttributes", json, StringComparison.Ordinal);
        Assert.Contains("\"parentVariantId\":\"parent-42\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dynamicVariantType\":\"H1_L1\"", json, StringComparison.Ordinal);
    }

    // ---------- Reading is refused ----------

    [Fact]
    public void Reading_through_the_write_interfaces_is_refused()
    {
        // These are request bodies; nothing in the API returns them. The read
        // side's converter throws on Write for the mirror-image reason.
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(
            """{"code":"plain"}""", ProductJsonContext.Default.IEmporixProductCreation));

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(
            """{"code":"plain"}""", ProductJsonContext.Default.IEmporixProductUpdate));

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(
            """{"id":"b1"}""", ProductJsonContext.Default.IEmporixProductBulkUpdate));
    }

    private static string Write<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(value, typeInfo);
}
