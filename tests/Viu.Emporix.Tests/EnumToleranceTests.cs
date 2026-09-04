using System.Text.Json;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix.Tests;

/// <summary>
/// Reading an enum value the vendored specification does not list.
/// </summary>
/// <remarks>
/// The failure this addresses is not that one field becomes unreadable — it is
/// that the whole response does. A product, an order, a page of sixty: all of
/// it threw because one field carried a value the specification had not caught
/// up with.
///
/// The converter is a pure function, so these tests carry real weight. Which
/// values Emporix actually sends is a separate question, and one no unit test
/// can answer.
/// </remarks>
public class EnumToleranceTests
{
    [Fact]
    public void A_known_value_still_parses()
    {
        BasicProductWithId? product = JsonSerializer.Deserialize(
            """{"id":"b1","code":"plain","productType":"BUNDLE"}""",
            ProductJsonContext.Default.BasicProductWithId);

        Assert.Equal(ProductType.BUNDLE, product?.ProductType);
    }

    [Fact]
    public void An_unknown_value_becomes_null_and_the_rest_of_the_object_survives()
    {
        // The point of the whole change. Before it, this threw and took the id
        // and the code with it.
        BasicProductWithId? product = JsonSerializer.Deserialize(
            """{"id":"b1","code":"plain","productType":"SOMETHING_NEW"}""",
            ProductJsonContext.Default.BasicProductWithId);

        Assert.Null(product?.ProductType);
        Assert.Equal("b1", product?.Id);
        Assert.Equal("plain", product?.Code);
    }

    [Fact]
    public void Null_from_an_unknown_value_is_not_written_back()
    {
        // WhenWritingNull on every context is what makes this safe: a value the
        // SDK could not read is omitted rather than sent back as null, which on
        // a PATCH would clear the field.
        BasicProductWithId? product = JsonSerializer.Deserialize(
            """{"id":"b1","code":"plain","productType":"SOMETHING_NEW"}""",
            ProductJsonContext.Default.BasicProductWithId);

        string json = JsonSerializer.Serialize(
            product!, ProductJsonContext.Default.BasicProductWithId);

        Assert.DoesNotContain("productType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Case_insensitivity_is_preserved()
    {
        // Not a choice — a constraint. JsonStringEnumConverter matches without
        // regard to case, and 180 generated enum members differ from their wire
        // value in case alone: the specification writes «string» where NSwag had
        // to emit «String» to get a legal identifier. A converter that tightened
        // this would break every one of them.
        BasicProductWithId? product = JsonSerializer.Deserialize(
            """{"id":"b1","productType":"bundle"}""",
            ProductJsonContext.Default.BasicProductWithId);

        Assert.Equal(ProductType.BUNDLE, product?.ProductType);
    }

    [Fact]
    public void A_numeric_string_does_not_smuggle_in_an_undefined_value()
    {
        // Enum.TryParse accepts «4» and hands back (ProductType)4 whether or not
        // any member has that value, so the parse alone is not enough — an
        // out-of-range number would arrive looking like a real product type.
        BasicProductWithId? four = JsonSerializer.Deserialize(
            """{"id":"b1","productType":"4"}""",
            ProductJsonContext.Default.BasicProductWithId);

        // 4 happens to be DYNAMIC_VARIANT, so it is defined and survives.
        Assert.Equal(ProductType.DYNAMIC_VARIANT, four?.ProductType);

        BasicProductWithId? out_of_range = JsonSerializer.Deserialize(
            """{"id":"b1","productType":"99"}""",
            ProductJsonContext.Default.BasicProductWithId);

        Assert.Null(out_of_range?.ProductType);
    }

    [Fact]
    public void A_required_enum_still_throws()
    {
        // The 75 non-nullable enum properties keep NSwag's strict converter.
        // The specification marks these required, so an unrecognised value is a
        // broken contract rather than a field the caller can shrug off — and
        // there is no null to put there anyway.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"id":"d1","code":"dyn","productType":"SOMETHING_NEW"}""",
            ProductJsonContext.Default.DynamicVariantProductWithId));
    }

    [Fact]
    public void An_absent_value_is_still_absent()
    {
        // Null means «absent» and «present but unrecognised» alike. That is the
        // price of approach A, and it is acceptable because both leave the
        // caller in the same position: no usable value, nothing sent back.
        BasicProductWithId? product = JsonSerializer.Deserialize(
            """{"id":"b1","code":"plain"}""",
            ProductJsonContext.Default.BasicProductWithId);

        Assert.Null(product?.ProductType);
    }
}
