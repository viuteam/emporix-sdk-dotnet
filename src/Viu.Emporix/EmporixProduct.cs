namespace Viu.Emporix.ProductModels;

/// <summary>
/// What every product shape has in common, whatever its type.
/// </summary>
/// <remarks>
/// <para>
/// The specification declares a product read as a <c>oneOf</c> over five
/// schemas — basic, bundle, parent variant, variant and dynamic variant — with
/// no <c>discriminator</c> anywhere in the document. The generator therefore
/// resolved it to one alternative, and the reads on
/// <see cref="Viu.Emporix.ProductService"/> return that one for all five.
/// </para>
/// <para>
/// <c>productType</c> is a reliable discriminator in practice, which is what
/// makes resolving them possible. Reads that do so live on
/// <c>ProductAnyTypeOperations</c>, reached through
/// <c>client.Products.AnyType</c>.
/// </para>
/// <para>
/// Deliberately three members. <c>Name</c>, <c>Description</c> and
/// <c>Mixins</c> sit on <c>ProductCore</c>, which
/// <see cref="DynamicVariantProductWithId"/> does not inherit — each would need
/// another forwarding member. Everything beyond these three is what the pattern
/// match is for.
/// </para>
/// </remarks>
public interface IEmporixProduct
{
    /// <summary>The product id.</summary>
    string? Id { get; }

    /// <summary>The product code, unique within the tenant.</summary>
    string? Code { get; }

    /// <summary>Which of the five shapes this is.</summary>
    ProductType? ProductType { get; }
}

// Attached through the generated classes' own partial declarations. Editing
// Generated/ would work until the next spec sync overwrote it.
public partial class BasicProductWithId : IEmporixProduct;

public partial class BundleProductWithId : IEmporixProduct;

public partial class ParentVariantProductWithId : IEmporixProduct;

public partial class VariantProductWithId : IEmporixProduct;

/// <remarks>
/// The specification marks this type's members required, so the generator
/// emitted them non-nullable. <c>ProductType</c> and <c>ProductType?</c> are
/// different types as far as implementing an interface goes, so the three
/// members are forwarded explicitly rather than matched implicitly.
/// </remarks>
public partial class DynamicVariantProductWithId : IEmporixProduct
{
    string? IEmporixProduct.Id => Id;

    string? IEmporixProduct.Code => Code;

    ProductType? IEmporixProduct.ProductType => ProductType;
}
