namespace Viu.Emporix.ProductModels;

/// <summary>
/// A product in one of the five shapes <c>POST /products</c> accepts.
/// </summary>
/// <remarks>
/// <para>
/// <c>specs/product.yml</c> declares the creation body as a <c>oneOf</c> over
/// <c>basicProductCreation</c>, <c>bundleProductCreation</c>,
/// <c>parentVariantProductCreation</c>, <c>variantProductCreation</c> and
/// <c>dynamicVariantProductCreation</c>. This interface is the parameter type
/// that lets a caller pass any of them.
/// </para>
/// <para>
/// Deliberately without members. Nothing reads through this — the caller holds
/// the concrete type and the creation converter dispatches on it. Members would
/// be forwarding for nobody.
/// </para>
/// <para>
/// <b>Implementing this outside the SDK does not work.</b> The converter
/// dispatches on exact type and throws for anything it does not know. The five
/// types below are the whole set.
/// </para>
/// </remarks>
public interface IEmporixProductCreation;

/// <summary>
/// A product in one of the five shapes <c>PUT /products/{productId}</c> accepts.
/// </summary>
/// <remarks>
/// The specification's <c>productUpdateBody</c> is a <c>oneOf</c> over the five
/// <c>*Update</c> schemas. Note that <c>PATCH</c> is <b>not</b> one of these:
/// it declares a single flat <c>productPartialUpdate</c>, which is why
/// <see cref="Viu.Emporix.ProductService"/> takes
/// <see cref="ProductPartialUpdate"/> there and this interface here.
/// </remarks>
public interface IEmporixProductUpdate;

/// <summary>
/// A product in one of the five shapes <c>PUT /products/bulk</c> accepts.
/// </summary>
/// <remarks>
/// The specification declares an array whose <c>items</c> are a <c>oneOf</c>,
/// so one call may carry a mix of shapes. That is what this interface plus its
/// converter deliver; a per-type method could not.
/// </remarks>
public interface IEmporixProductBulkUpdate;

// Attached through the generated classes' own partial declarations. Editing
// Generated/ would work until the next spec sync overwrote it.
//
// No explicit members anywhere below: the interfaces have none. All five
// creation types reach ProductCore, including DynamicVariantProductCreation
// through DynamicVariantProduct — unlike DynamicVariantProductWithId on the
// read side, which has no base and needed forwarding.

public partial class BasicProductCreation : IEmporixProductCreation;

public partial class BundleProductCreation : IEmporixProductCreation;

public partial class ParentVariantProductCreation : IEmporixProductCreation;

public partial class VariantProductCreation : IEmporixProductCreation;

public partial class DynamicVariantProductCreation : IEmporixProductCreation;

public partial class BasicProductUpdate : IEmporixProductUpdate;

public partial class BundleProductUpdate : IEmporixProductUpdate;

public partial class ParentVariantProductUpdate : IEmporixProductUpdate;

public partial class VariantProductUpdate : IEmporixProductUpdate;

public partial class DynamicVariantProductUpdate : IEmporixProductUpdate;

// These five derive from the *Update types above, so they satisfy
// IEmporixProductUpdate as well. Nothing can be done about that from here —
// it is why the update converter checks exact types.
public partial class BasicProductBulkUpdate : IEmporixProductBulkUpdate;

public partial class BundleProductBulkUpdate : IEmporixProductBulkUpdate;

public partial class ParentVariantProductBulkUpdate : IEmporixProductBulkUpdate;

public partial class VariantProductBulkUpdate : IEmporixProductBulkUpdate;

public partial class DynamicVariantProductBulkUpdate : IEmporixProductBulkUpdate;
