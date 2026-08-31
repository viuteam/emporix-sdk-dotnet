namespace Viu.Emporix;

/// <summary>
/// Builds and reads the product references Emporix calls YRNs.
/// </summary>
/// <remarks>
/// Cart and order line items carry only a reference of the form
/// <c>urn:yaas:hybris:product:product:&lt;tenant&gt;;&lt;productId&gt;</c>, never a
/// bare product id. Anything else is rejected with «Given yrn does not match
/// yaas urn scheme», so the format is encapsulated here rather than assembled at
/// each call site.
/// </remarks>
public static class ProductYrn
{
    private const string Prefix = "urn:yaas:hybris:product:product:";

    /// <summary>
    /// Builds the product reference that adding a cart item requires.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="productId">The product id.</param>
    /// <exception cref="ArgumentException">An argument is empty.</exception>
    public static string Create(string tenant, string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        return $"{Prefix}{tenant};{productId}";
    }

    /// <summary>
    /// Reads the product id out of a product reference.
    /// </summary>
    /// <param name="yrn">The reference, as it appears on a cart or order line.</param>
    /// <returns>
    /// The product id, or an empty string when the reference is missing or
    /// carries no id segment.
    /// </returns>
    /// <remarks>
    /// Approval resource items are the exception: their reference is frequently
    /// the bare product id with no wrapper. This then returns an empty string,
    /// and the neighbouring <c>itemId</c> is what you want instead. Measured on
    /// tenant `viu`, 2026-08-18.
    /// </remarks>
    public static string GetProductId(string? yrn)
    {
        if (string.IsNullOrEmpty(yrn))
        {
            return string.Empty;
        }

        int separator = yrn.LastIndexOf(';');

        return separator >= 0 ? yrn[(separator + 1)..] : string.Empty;
    }
}
