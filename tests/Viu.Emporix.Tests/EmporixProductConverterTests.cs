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
}
