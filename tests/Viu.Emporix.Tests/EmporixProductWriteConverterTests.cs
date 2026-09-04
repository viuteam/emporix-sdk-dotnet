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
}
