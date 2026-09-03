using System.Linq.Expressions;

namespace Viu.Emporix.Mixins;

/// <summary>
/// Builds an Emporix <c>q</c> filter over a mixin's attributes.
/// </summary>
/// <example>
/// <code>
/// string q = MixinQuery.For(Mixins.Delivery)
///     .Where(d =&gt; d.Packaging, Condition.EqualTo("Paper"))
///     .Where(d =&gt; d.Weight, Condition.AtLeast(2))
///     .Build()
///     .Build();
///
/// await client.Products.SearchAsync(q);
/// </code>
/// </example>
public static class MixinQuery
{
    /// <summary>Starts a filter for one mixin.</summary>
    /// <param name="descriptor">The mixin to filter on.</param>
    /// <typeparam name="T">The mixin's generated type.</typeparam>
    public static MixinQueryBuilder<T> For<T>(MixinDescriptor<T> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new MixinQueryBuilder<T>(descriptor);
    }
}

/// <summary>
/// Collects conditions on one mixin's attributes.
/// </summary>
/// <typeparam name="T">The mixin's generated type.</typeparam>
/// <remarks>
/// The <c>Where</c> overloads resolve from the selector's return type, so the
/// condition's category decides which operators an attribute accepts. Selecting
/// a property is the only supported expression: nothing is evaluated, only the
/// member's name is read, which is why no reflection or expression compilation
/// is involved and the whole builder stays AOT-safe.
/// </remarks>
public sealed class MixinQueryBuilder<T>
{
    private readonly MixinDescriptor<T> _descriptor;
    private readonly List<string> _clauses = [];

    internal MixinQueryBuilder(MixinDescriptor<T> descriptor) => _descriptor = descriptor;

    /// <summary>Adds a condition on a text attribute.</summary>
    /// <param name="selector">The attribute, for example <c>d =&gt; d.Packaging</c>.</param>
    /// <param name="condition">The condition.</param>
    public MixinQueryBuilder<T> Where(Expression<Func<T, string?>> selector, TextCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds a condition on a decimal attribute.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="condition">The condition.</param>
    public MixinQueryBuilder<T> Where(Expression<Func<T, double?>> selector, NumberCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds a condition on an integer attribute.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="condition">The condition.</param>
    public MixinQueryBuilder<T> Where(Expression<Func<T, int?>> selector, NumberCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds a condition on a boolean attribute.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="condition">The condition.</param>
    public MixinQueryBuilder<T> Where(Expression<Func<T, bool?>> selector, BoolCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds a presence condition, which any attribute type accepts.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="condition">The condition.</param>
    /// <typeparam name="TAttr">The attribute's type, unconstrained.</typeparam>
    public MixinQueryBuilder<T> Where<TAttr>(Expression<Func<T, TAttr?>> selector, AnyCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds an equality condition on an enum attribute.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="value">The value to match.</param>
    /// <typeparam name="TEnum">The generated enum type.</typeparam>
    /// <remarks>
    /// The generator emits an enum for a schema declaring <c>enum</c>, and the
    /// wire form is the member name.
    /// </remarks>
    public MixinQueryBuilder<T> WhereEnum<TEnum>(Expression<Func<T, TEnum?>> selector, TEnum value)
        where TEnum : struct, Enum
        => Add(selector, value.ToString(), language: null);

    /// <summary>
    /// Adds a condition on one language of a localized attribute.
    /// </summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="language">The language tag, for example <c>en</c>.</param>
    /// <param name="condition">The condition.</param>
    /// <typeparam name="TAttr">The attribute's type, unconstrained.</typeparam>
    /// <remarks>
    /// A separate name rather than an overload: a localized attribute is an
    /// object of language keys, so its selector type says nothing about the
    /// compared value, and as an overload it was ambiguous.
    /// </remarks>
    public MixinQueryBuilder<T> WhereLocalized<TAttr>(
        Expression<Func<T, TAttr?>> selector,
        string language,
        TextCondition condition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return Add(selector, condition.Render, language);
    }

    /// <summary>The filter.</summary>
    /// <returns>The built filter.</returns>
    /// <exception cref="InvalidOperationException">No condition was added.</exception>
    public MixinFilter Build()
        => _clauses.Count == 0
            ? throw new InvalidOperationException(
                $"No condition was added for the mixin \"{_descriptor.Key}\".")
            : MixinFilter.FromClauses(string.Join(' ', _clauses));

    private MixinQueryBuilder<T> Add<TAttr>(
        Expression<Func<T, TAttr>> selector,
        string render,
        string? language)
    {
        string attribute = Attribute(selector);

        _clauses.Add(language is null
            ? $"mixins.{_descriptor.Key}.{attribute}:{render}"
            : $"mixins.{_descriptor.Key}.{attribute}.{language}:{render}");

        return this;
    }

    // The selector yields the CLR name; the descriptor's generated table maps it
    // to the JSON name. Nothing is read that is not already in the tree, so this
    // needs neither reflection nor expression compilation.
    private string Attribute<TAttr>(Expression<Func<T, TAttr>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (selector.Body is not MemberExpression member)
        {
            throw new ArgumentException(
                "Select a property of the mixin, for example d => d.Packaging.",
                nameof(selector));
        }

        return _descriptor.Attributes.TryGetValue(member.Member.Name, out string? json)
            ? json
            : throw new ArgumentException(
                $"{member.Member.Name} is not an attribute of the mixin \"{_descriptor.Key}\".",
                nameof(selector));
    }
}
