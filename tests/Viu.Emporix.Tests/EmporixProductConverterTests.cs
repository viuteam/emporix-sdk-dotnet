using System.Text.Json;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix.Tests;

/// <summary>
/// Resolving a product response into the type its productType names.
/// </summary>
/// <remarks>
/// The specification declares a product read as a oneOf over five schemas with
/// no discriminator, so the generator picked one and every read returned
/// BasicProductWithId. productType is a usable discriminator in practice, which
/// is what makes this possible at all.
///
/// The converter is a pure function — JSON in, type out, no HTTP — which is why
/// the tests here carry weight. A stubbed handler would only confirm the same
/// expectation the code builds.
/// </remarks>
public class EmporixProductConverterTests
{
    [Fact]
    public void All_five_generated_types_carry_the_interface()
    {
        // The interface is what the facade returns, so every shape the
        // specification's oneOf lists has to implement it. A sixth type added
        // upstream would fail here rather than at a customer.
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(BasicProductWithId)));
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(BundleProductWithId)));
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(ParentVariantProductWithId)));
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(VariantProductWithId)));
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(DynamicVariantProductWithId)));
    }

    [Fact]
    public void The_interface_reads_through_to_the_concrete_properties()
    {
        // DynamicVariantProductWithId declares Id, Code and ProductType
        // non-nullable, because the specification marks them required. An enum
        // and its Nullable are different types for interface implementation, so
        // that one needs explicit members — this checks they forward rather
        // than returning default.
        IEmporixProduct dynamic = new DynamicVariantProductWithId
        {
            Id = "d1",
            Code = "dyn",
            ProductType = ProductType.DYNAMIC_VARIANT,
        };

        Assert.Equal(ProductType.DYNAMIC_VARIANT, dynamic.ProductType);

        AssertIdentity(dynamic, "d1", "dyn");
        AssertIdentity(new BasicProductWithId { Id = "b1", Code = "plain" }, "b1", "plain");

        // Through the interface deliberately, and in a local function because
        // CA1859 would otherwise ask for the concrete type — which is the one
        // thing this test must not use.
        static void AssertIdentity(IEmporixProduct product, string id, string code)
        {
            Assert.Equal(id, product.Id);
            Assert.Equal(code, product.Code);
        }
    }

    [Theory]
    [InlineData("BASIC", typeof(BasicProductWithId))]
    [InlineData("BUNDLE", typeof(BundleProductWithId))]
    [InlineData("PARENT_VARIANT", typeof(ParentVariantProductWithId))]
    [InlineData("VARIANT", typeof(VariantProductWithId))]
    [InlineData("DYNAMIC_VARIANT", typeof(DynamicVariantProductWithId))]
    public void Each_product_type_resolves_to_its_own_shape(string productType, Type expected)
    {
        IEmporixProduct? product = JsonSerializer.Deserialize(
            $$"""{"id":"p1","code":"c1","productType":"{{productType}}"}""",
            ProductJsonContext.Default.IEmporixProduct);

        Assert.IsType(expected, product);
    }

    [Fact]
    public void A_bundle_exposes_its_bundled_products_typed()
    {
        // The point of the whole exercise: these fields were reachable only as
        // extension data before.
        IEmporixProduct? product = JsonSerializer.Deserialize(
            """
            {"id":"g1","code":"gift","productType":"BUNDLE",
             "bundledProducts":[{"productId":"p1","amount":2},{"productId":"p2","amount":1}]}
            """,
            ProductJsonContext.Default.IEmporixProduct);

        BundleProductWithId bundle = Assert.IsType<BundleProductWithId>(product);
        Assert.Equal(2, bundle.BundledProducts.Count);
        Assert.Equal("p1", bundle.BundledProducts[0].ProductId);
        Assert.Equal(2, bundle.BundledProducts[0].Amount);
    }

    [Fact]
    public void A_variant_exposes_its_parent_typed()
    {
        IEmporixProduct? product = JsonSerializer.Deserialize(
            """{"id":"v1","code":"red-m","productType":"VARIANT","parentVariantId":"parent-42"}""",
            ProductJsonContext.Default.IEmporixProduct);

        VariantProductWithId variant = Assert.IsType<VariantProductWithId>(product);
        Assert.Equal("parent-42", variant.ParentVariantId);
    }

    [Fact]
    public void An_unknown_product_type_throws_the_way_the_plain_read_does()
    {
        // The converter routes an unrecognised value to the basic shape, and
        // then the generated enum property refuses it: NSwag emits
        // JsonStringEnumConverter<ProductType> as a property-level attribute,
        // which beats any converter the context declares.
        //
        // So this is not the resolving read's behaviour but the SDK's — the
        // same JSON through ProductJsonContext.Default.BasicProductWithId
        // throws identically, which is what every plain read on ProductService
        // uses. Emporix has extended the list before, so it is a real defect;
        // it belongs in a SpecSync GeneratedCodeFixer rule, since it affects
        // every enum in every specification rather than products alone.
        //
        // Asserted rather than left undiscovered: if a fix lands upstream this
        // test fails and says where to look.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"id":"x1","code":"new","productType":"SOMETHING_NEW"}""",
            ProductJsonContext.Default.IEmporixProduct));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"id":"x1","code":"new","productType":"SOMETHING_NEW"}""",
            ProductJsonContext.Default.BasicProductWithId));
    }

    [Fact]
    public void An_absent_product_type_falls_back_to_the_basic_shape()
    {
        // The specification does not require productType on variantProductWithId,
        // so this is a case Emporix is allowed to produce. Recorded as a
        // limitation in the design spec rather than guessed at from other fields.
        IEmporixProduct? product = JsonSerializer.Deserialize(
            """{"id":"v1","code":"red-m","parentVariantId":"parent-42"}""",
            ProductJsonContext.Default.IEmporixProduct);

        Assert.IsType<BasicProductWithId>(product);
    }

    [Fact]
    public void A_nested_product_type_does_not_decide_the_shape()
    {
        // A dynamic variant carries a variants map whose entries have their own
        // productType. Scanning without a depth guard would let the inner value
        // win — and the first version of that guard was off by one, which made
        // every product resolve to the fallback with no error at all.
        IEmporixProduct? product = JsonSerializer.Deserialize(
            """
            {"id":"d1","code":"dyn","productType":"DYNAMIC_VARIANT",
             "variants":{"v1":{"productType":"VARIANT"}}}
            """,
            ProductJsonContext.Default.IEmporixProduct);

        Assert.IsType<DynamicVariantProductWithId>(product);
    }

    [Fact]
    public void A_mixed_list_resolves_every_element_on_its_own()
    {
        // The reason this is a converter rather than logic in the facade: lists
        // need no second code path.
        List<IEmporixProduct>? products = JsonSerializer.Deserialize(
            """
            [{"id":"b1","code":"plain","productType":"BASIC"},
             {"id":"g1","code":"gift","productType":"BUNDLE"},
             {"id":"p1","code":"shirt","productType":"PARENT_VARIANT"},
             {"id":"v1","code":"red-m","productType":"VARIANT"},
             {"id":"d1","code":"dyn","productType":"DYNAMIC_VARIANT"}]
            """,
            ProductJsonContext.Default.ListIEmporixProduct);

        Assert.Collection(
            products!,
            p => Assert.IsType<BasicProductWithId>(p),
            p => Assert.IsType<BundleProductWithId>(p),
            p => Assert.IsType<ParentVariantProductWithId>(p),
            p => Assert.IsType<VariantProductWithId>(p),
            p => Assert.IsType<DynamicVariantProductWithId>(p));
    }

    [Fact]
    public void Writing_through_the_interface_is_refused()
    {
        // Products are written through the concrete update types, whose field
        // sets differ from the response schemas. A writable converter would
        // suggest a read product can be sent back.
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(
            (IEmporixProduct)new BasicProductWithId { Id = "b1" },
            ProductJsonContext.Default.IEmporixProduct));
    }
}
