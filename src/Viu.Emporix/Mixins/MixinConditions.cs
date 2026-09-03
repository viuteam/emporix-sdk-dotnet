using System.Globalization;

namespace Viu.Emporix.Mixins;

/// <summary>A condition on a text attribute.</summary>
public readonly struct TextCondition
{
    internal TextCondition(string render) => Render = render;

    internal string Render { get; }
}

/// <summary>A condition on a numeric attribute.</summary>
public readonly struct NumberCondition
{
    internal NumberCondition(string render) => Render = render;

    internal string Render { get; }
}

/// <summary>A condition on a boolean attribute.</summary>
public readonly struct BoolCondition
{
    internal BoolCondition(string render) => Render = render;

    internal string Render { get; }
}

/// <summary>A condition on an attribute of any type.</summary>
public readonly struct AnyCondition
{
    internal AnyCondition(string render) => Render = render;

    internal string Render { get; }
}

/// <summary>
/// The conditions a mixin attribute can be filtered by.
/// </summary>
/// <remarks>
/// <para>
/// Categorised by value kind rather than generic over it. A generic condition
/// paired with a nullable property selector cannot be inferred: with a
/// <c>double?</c> attribute the compiler cannot tell whether the type argument
/// is <c>double</c> under <c>Nullable</c> or is itself nullable, and inference
/// runs before constraints, so no constraint fixes it.
/// </para>
/// <para>
/// The categories are also what gates the operators: <see cref="AtLeast"/>
/// returns a <see cref="NumberCondition"/>, which fits no text selector, so
/// misapplying it is a compile error rather than a query the backend rejects.
/// </para>
/// </remarks>
public static class Condition
{
    /// <summary>Matches a text attribute exactly.</summary>
    /// <param name="value">The value. Must not contain whitespace.</param>
    public static TextCondition EqualTo(string value) => new(Text(value));

    /// <summary>Matches any of several text values.</summary>
    /// <param name="values">The values. None may contain whitespace.</param>
    public static TextCondition OneOf(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length == 0)
        {
            throw new ArgumentException("Pass at least one value.", nameof(values));
        }

        return new($"({string.Join(',', values.Select(Text))})");
    }

    /// <summary>Matches a text attribute against a regular expression.</summary>
    /// <param name="regex">The expression, as Emporix evaluates it.</param>
    public static TextCondition Matching(string regex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regex);
        return new($"~{regex}");
    }

    /// <summary>Matches a numeric attribute exactly.</summary>
    /// <param name="value">The value.</param>
    public static NumberCondition EqualTo(double value) => new(Number(value));

    /// <summary>Matches a numeric attribute at or above a bound.</summary>
    /// <param name="value">The lower bound, inclusive.</param>
    public static NumberCondition AtLeast(double value) => new($">={Number(value)}");

    /// <summary>Matches a numeric attribute at or below a bound.</summary>
    /// <param name="value">The upper bound, inclusive.</param>
    public static NumberCondition AtMost(double value) => new($"<={Number(value)}");

    /// <summary>Matches a numeric attribute within a range.</summary>
    /// <param name="low">The lower bound, inclusive.</param>
    /// <param name="high">The upper bound, inclusive.</param>
    public static NumberCondition Between(double low, double high)
        => low > high
            ? throw new ArgumentException(
                $"The lower bound {Number(low)} exceeds the upper bound {Number(high)}.", nameof(low))
            : new NumberCondition($"(>={Number(low)} AND <={Number(high)})");

    /// <summary>Matches a boolean attribute that is true.</summary>
    public static BoolCondition True() => new("true");

    /// <summary>Matches a boolean attribute that is false.</summary>
    public static BoolCondition False() => new("false");

    /// <summary>Matches an attribute that is present.</summary>
    public static AnyCondition Present() => new("exists");

    /// <summary>Matches an attribute that is absent.</summary>
    public static AnyCondition Missing() => new("missing");

    private static string Number(double value)
        => value.ToString(CultureInfo.InvariantCulture);

    // The q DSL separates clauses with spaces and the Node SDK records the safe
    // escaping as unverified upstream. A value carrying whitespace is refused
    // rather than mangled; MixinFilter.Raw is the way past this.
    private static string Text(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        return value.AsSpan().ContainsAny(' ', '\t', '\n')
            ? throw new ArgumentException(
                $"The value \"{value}\" contains whitespace, which the q syntax uses as an AND separator. Use MixinFilter.Raw for it.",
                nameof(value))
            : value;
    }
}
