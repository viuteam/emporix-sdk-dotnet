namespace Viu.Emporix.QuoteModels;

/// <summary>
/// A quote in one of the two shapes <c>POST /quotes</c> accepts.
/// </summary>
/// <remarks>
/// <para>
/// <c>specs/quote.yml</c> declares the create body as a <c>oneOf</c> over
/// <c>QuoteCreateRequest</c> and <c>QuoteCreateFromCartRequest</c>, and the
/// specification's own descriptions make them opposites: one is for «creating a
/// quote manually», carrying its own <c>items</c>; the other for «creating a
/// quote from the cart», carrying a required <c>cartId</c> and no items,
/// because Emporix copies them across.
/// </para>
/// <para>
/// Before this interface the SDK sent only the first. That made the central B2B
/// flow — a shopper fills a cart, then asks for a quote — impossible rather than
/// merely awkward: <see cref="QuoteCreateRequest"/> has no <c>cartId</c> and no
/// extension data either, so the field could not be set by any route.
/// </para>
/// <para>
/// Deliberately without members. Nothing reads through this — the caller holds
/// the concrete type and the converter dispatches on it.
/// </para>
/// </remarks>
public interface IEmporixQuoteCreation;

// Attached through the generated classes' own partial declarations. Editing
// Generated/ would work until the next spec sync overwrote it.
//
// The two are unrelated classes, so neither can be passed where the other is
// expected — the inheritance traps that the product write interfaces had do
// not arise here.

public partial class QuoteCreateRequest : IEmporixQuoteCreation;

public partial class QuoteCreateFromCartRequest : IEmporixQuoteCreation;
