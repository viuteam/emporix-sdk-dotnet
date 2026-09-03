namespace Viu.Emporix.Mixins;

/// <summary>
/// A built <c>q</c> fragment that any q-capable endpoint accepts.
/// </summary>
/// <remarks>
/// Pass <see cref="Build"/> to any service method taking a <c>q</c> filter.
/// </remarks>
public sealed class MixinFilter
{
    private MixinFilter(string fragment) => Fragment = fragment;

    internal string Fragment { get; }

    internal static MixinFilter FromClauses(string fragment) => new(fragment);

    /// <summary>The fragment, for a service method's <c>q</c> parameter.</summary>
    public string Build() => Fragment;

    /// <summary>
    /// Combines with another filter using AND.
    /// </summary>
    /// <param name="other">The filter to combine with.</param>
    /// <remarks>
    /// A space is the q syntax's AND and every q endpoint understands it, so
    /// this needs no capability.
    /// </remarks>
    public MixinFilter And(MixinFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new($"{Fragment} {other.Fragment}");
    }

    /// <summary>
    /// Combines with another filter using OR.
    /// </summary>
    /// <param name="other">The filter to combine with.</param>
    /// <returns>
    /// A compound filter, whose <see cref="CompoundMixinFilter.Build"/> requires
    /// naming the target service.
    /// </returns>
    /// <remarks>
    /// OR needs the <c>compoundLogicalQuery</c> operator, which only some
    /// services accept, so the result is a different type: it has no
    /// argumentless <c>Build</c>, and the capability cannot be forgotten.
    /// </remarks>
    public CompoundMixinFilter Or(MixinFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return CompoundMixinFilter.FromFragment(
            $"compoundLogicalQuery:(({Fragment}) OR ({other.Fragment}))");
    }

    /// <summary>
    /// Wraps a fragment written by hand.
    /// </summary>
    /// <param name="fragment">The q fragment, escaped by the caller.</param>
    /// <remarks>
    /// The way past the whitespace guard, and the way to combine a mixin filter
    /// with a non-mixin clause such as <c>published:true</c>.
    /// </remarks>
    public static MixinFilter Raw(string fragment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment);
        return new(fragment);
    }
}

/// <summary>
/// A service endpoint, and whether it can run a compound query.
/// </summary>
/// <remarks>
/// <para>
/// <c>compoundLogicalQuery</c> is a per-service capability, not a per-entity
/// one: the Emporix documentation scopes the operator to Approval, Audit Logs,
/// Availability, Product, Quote and Schema, and the Node SDK carries the same
/// flag per method. Knowing which entity a mixin hangs on says nothing about
/// which endpoint is being called.
/// </para>
/// <para>
/// Add no value here without a source for it.
/// </para>
/// </remarks>
public sealed class EmporixQuery
{
    private EmporixQuery(string service, bool compound)
    {
        Service = service;
        Compound = compound;
    }

    internal string Service { get; }

    internal bool Compound { get; }

    /// <summary>Product search. Accepts compound queries.</summary>
    public static EmporixQuery ProductSearch { get; } = new("Product", true);

    /// <summary>Availability search. Accepts compound queries.</summary>
    public static EmporixQuery AvailabilitySearch { get; } = new("Availability", true);

    /// <summary>Quote search. Accepts compound queries.</summary>
    public static EmporixQuery QuoteSearch { get; } = new("Quote", true);

    /// <summary>Approval search. Accepts compound queries.</summary>
    public static EmporixQuery ApprovalSearch { get; } = new("Approval", true);

    /// <summary>Schema and custom entity search. Accepts compound queries.</summary>
    public static EmporixQuery SchemaSearch { get; } = new("Schema", true);

    /// <summary>Audit log search. Accepts compound queries.</summary>
    public static EmporixQuery AuditLogSearch { get; } = new("AuditLog", true);

    /// <summary>Category search. Rejects compound queries.</summary>
    public static EmporixQuery CategorySearch { get; } = new("Category", false);

    /// <summary>Order listing. Rejects compound queries.</summary>
    public static EmporixQuery OrderList { get; } = new("Order", false);

    /// <summary>Vendor search. Rejects compound queries.</summary>
    public static EmporixQuery VendorSearch { get; } = new("Vendor", false);

    /// <summary>Customer search, seller side. Rejects compound queries.</summary>
    public static EmporixQuery CustomerAdminSearch { get; } = new("CustomerAdmin", false);
}

/// <summary>
/// A <c>q</c> fragment using <c>compoundLogicalQuery</c>, which only some
/// services accept.
/// </summary>
/// <remarks>
/// Deliberately unrelated to <see cref="MixinFilter"/> by inheritance:
/// inheriting its argumentless <c>Build</c> would let the capability gate be
/// skipped without a diagnostic.
/// </remarks>
public sealed class CompoundMixinFilter
{
    private CompoundMixinFilter(string fragment) => Fragment = fragment;

    internal string Fragment { get; }

    internal static CompoundMixinFilter FromFragment(string fragment) => new(fragment);

    /// <summary>
    /// The fragment, if the target service can run it.
    /// </summary>
    /// <param name="target">The endpoint this filter is going to.</param>
    /// <returns>The q fragment.</returns>
    /// <exception cref="InvalidOperationException">
    /// The service does not accept <c>compoundLogicalQuery</c>.
    /// </exception>
    public string Build(EmporixQuery target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.Compound
            ? Fragment
            : throw new InvalidOperationException(
                $"The {target.Service} service does not accept compoundLogicalQuery. Combine the conditions with And instead of Or.");
    }

    /// <summary>Combines with another filter using AND.</summary>
    /// <param name="other">The filter to combine with.</param>
    public CompoundMixinFilter And(MixinFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new($"compoundLogicalQuery:(({Fragment}) AND ({other.Fragment}))");
    }

    /// <summary>Combines with another filter using OR.</summary>
    /// <param name="other">The filter to combine with.</param>
    public CompoundMixinFilter Or(MixinFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new($"compoundLogicalQuery:(({Fragment}) OR ({other.Fragment}))");
    }
}
