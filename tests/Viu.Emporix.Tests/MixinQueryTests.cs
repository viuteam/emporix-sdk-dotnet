using Viu.Emporix.Mixins;

namespace Viu.Emporix.Tests;

/// <summary>
/// Building an Emporix <c>q</c> filter over mixin attributes.
/// </summary>
/// <remarks>
/// The grammar is taken from the Node SDK, which runs it against real tenants.
/// Five forms in it are unverified against a tenant and are recorded as such in
/// the design spec — the range syntax, the localized path, exists and missing
/// semantics, the escaping, and whether metadata must be resent on PATCH. These
/// tests pin what the SDK emits, not that Emporix accepts it. Only the smoke
/// test can establish the latter.
/// </remarks>
public class MixinQueryTests
{
    [Fact]
    public void Text_conditions_render()
    {
        Assert.Equal("Paper", Condition.EqualTo("Paper").Render);
        Assert.Equal("(S,M,L)", Condition.OneOf("S", "M", "L").Render);
        Assert.Equal("~^Pa", Condition.Matching("^Pa").Render);
    }

    [Fact]
    public void Number_conditions_render_with_an_invariant_decimal_point()
    {
        // A Swiss or German culture would render 2,5 and the query would break.
        Assert.Equal("2.5", Condition.EqualTo(2.5).Render);
        Assert.Equal(">=10", Condition.AtLeast(10).Render);
        Assert.Equal("<=20", Condition.AtMost(20).Render);
        Assert.Equal("(>=1.5 AND <=4.5)", Condition.Between(1.5, 4.5).Render);
    }

    [Fact]
    public void Presence_and_boolean_conditions_render()
    {
        Assert.Equal("true", Condition.True().Render);
        Assert.Equal("false", Condition.False().Render);
        Assert.Equal("exists", Condition.Present().Render);
        Assert.Equal("missing", Condition.Missing().Render);
    }

    [Fact]
    public void A_text_value_carrying_whitespace_is_refused()
    {
        // The q DSL separates clauses with spaces and the Node SDK records its
        // escaping as unverified. Refusing beats mangling.
        ArgumentException error = Assert.Throws<ArgumentException>(() => Condition.EqualTo("Two Words"));
        Assert.Contains("whitespace", error.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => Condition.OneOf("fine", "not fine"));
    }

    [Fact]
    public void An_empty_text_value_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Condition.EqualTo(""));
    }
}
