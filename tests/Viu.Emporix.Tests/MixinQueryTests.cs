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

    [Fact]
    public void Plain_filters_join_with_a_space_which_every_q_endpoint_understands()
    {
        MixinFilter joined = MixinFilter.Raw("mixins.a.x:1").And(MixinFilter.Raw("mixins.a.y:2"));

        Assert.Equal("mixins.a.x:1 mixins.a.y:2", joined.Build());
    }

    [Fact]
    public void Or_produces_a_compound_query()
    {
        CompoundMixinFilter either = MixinFilter.Raw("mixins.a.x:1").Or(MixinFilter.Raw("mixins.a.x:2"));

        Assert.Equal(
            "compoundLogicalQuery:((mixins.a.x:1) OR (mixins.a.x:2))",
            either.Build(EmporixQuery.ProductSearch));
    }

    [Fact]
    public void A_compound_query_is_refused_for_a_service_that_cannot_run_it()
    {
        CompoundMixinFilter either = MixinFilter.Raw("mixins.a.x:1").Or(MixinFilter.Raw("mixins.a.x:2"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => either.Build(EmporixQuery.CategorySearch));

        Assert.Contains("Category", error.Message, StringComparison.Ordinal);
        Assert.Contains("And", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_capability_value_either_allows_or_refuses_a_compound_query(bool allowed)
    {
        EmporixQuery[] targets = allowed
            ? [EmporixQuery.ProductSearch, EmporixQuery.AvailabilitySearch, EmporixQuery.QuoteSearch,
               EmporixQuery.ApprovalSearch, EmporixQuery.SchemaSearch, EmporixQuery.AuditLogSearch]
            : [EmporixQuery.CategorySearch, EmporixQuery.OrderList,
               EmporixQuery.VendorSearch, EmporixQuery.CustomerAdminSearch];

        CompoundMixinFilter either = MixinFilter.Raw("a:1").Or(MixinFilter.Raw("a:2"));

        foreach (EmporixQuery target in targets)
        {
            if (allowed)
            {
                Assert.StartsWith("compoundLogicalQuery:", either.Build(target), StringComparison.Ordinal);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => either.Build(target));
            }
        }
    }

    [Fact]
    public void A_compound_filter_is_not_a_plain_filter()
    {
        // The reason Or returns a separate type rather than a subclass: an
        // inherited argumentless Build would let the capability gate be skipped
        // silently. If this fails, someone made the types related and the gate
        // is now optional.
        Assert.False(typeof(MixinFilter).IsAssignableFrom(typeof(CompoundMixinFilter)));
        Assert.False(typeof(CompoundMixinFilter).IsAssignableFrom(typeof(MixinFilter)));
    }

    [Fact]
    public void Anding_onto_a_compound_query_stays_compound()
    {
        string built = MixinFilter.Raw("a:1")
            .Or(MixinFilter.Raw("a:2"))
            .And(MixinFilter.Raw("published:true"))
            .Build(EmporixQuery.ProductSearch);

        Assert.Contains("compoundLogicalQuery:", built, StringComparison.Ordinal);
        Assert.Contains("published:true", built, StringComparison.Ordinal);
    }

    private static MixinDescriptor<TestDeliveryMixin> Delivery => new()
    {
        Key = "deliveryOptions",
        Entity = "PRODUCT",
        Url = "https://cdn.emporix.io/deliveryOptionsMixIn.v6.json",
        Version = 6,
        TypeInfo = TestMixinContext.Default.TestDeliveryMixin,
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Packaging"] = "packaging",
            ["Weight"] = "weight",
            ["Note"] = "note",
        },
    };

    [Fact]
    public void A_clause_targets_the_namespaced_attribute_path()
    {
        string q = MixinQuery.For(Delivery)
            .Where(d => d.Packaging, Condition.EqualTo("Paper"))
            .Build()
            .Build();

        Assert.Equal("mixins.deliveryOptions.packaging:Paper", q);
    }

    [Fact]
    public void Several_clauses_are_anded_by_a_space()
    {
        string q = MixinQuery.For(Delivery)
            .Where(d => d.Packaging, Condition.EqualTo("Paper"))
            .Where(d => d.Weight, Condition.Between(1.0, 5.0))
            .Build()
            .Build();

        Assert.Equal(
            "mixins.deliveryOptions.packaging:Paper mixins.deliveryOptions.weight:(>=1 AND <=5)",
            q);
    }

    [Fact]
    public void A_localized_clause_carries_the_language_segment()
    {
        string q = MixinQuery.For(Delivery)
            .WhereLocalized(d => d.Note, "en", Condition.EqualTo("Sale"))
            .Build()
            .Build();

        Assert.Equal("mixins.deliveryOptions.note.en:Sale", q);
    }

    [Fact]
    public void Presence_works_on_an_attribute_of_any_type()
    {
        string q = MixinQuery.For(Delivery)
            .Where(d => d.Note, Condition.Present())
            .Build()
            .Build();

        Assert.Equal("mixins.deliveryOptions.note:exists", q);
    }

    [Fact]
    public void An_expression_that_is_not_a_property_selector_is_refused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => MixinQuery.For(Delivery).Where(d => "constant", Condition.EqualTo("x")));

        Assert.Contains("property", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_property_that_is_not_an_attribute_of_this_mixin_is_refused()
    {
        MixinDescriptor<TestDeliveryMixin> incomplete = new()
        {
            Key = "deliveryOptions",
            Entity = "PRODUCT",
            Url = "https://cdn.emporix.io/deliveryOptionsMixIn.v6.json",
            Version = 6,
            TypeInfo = TestMixinContext.Default.TestDeliveryMixin,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["Packaging"] = "packaging" },
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => MixinQuery.For(incomplete).Where(d => d.Weight, Condition.AtLeast(1)));

        Assert.Contains("Weight", error.Message, StringComparison.Ordinal);
        Assert.Contains("deliveryOptions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Building_without_a_condition_is_refused()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => MixinQuery.For(Delivery).Build());

        Assert.Contains("deliveryOptions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_language_is_refused()
    {
        Assert.Throws<ArgumentException>(
            () => MixinQuery.For(Delivery).WhereLocalized(d => d.Note, "  ", Condition.EqualTo("Sale")));
    }
}
