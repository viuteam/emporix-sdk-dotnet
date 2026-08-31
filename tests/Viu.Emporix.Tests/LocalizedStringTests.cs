using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

/// <summary>
/// The same Emporix field arrives as a text or as a map of translations,
/// depending on whether the request asked for a language.
/// </summary>
/// <remarks>
/// This shipped broken: the generator resolved the union to its first branch and
/// typed the property <c>string</c>, so reading products from a real tenant threw
/// unless <c>Accept-Language</c> happened to be set. No unit test could see it —
/// every stub returned the shape the model expected.
/// </remarks>
public class LocalizedStringTests
{
    [Fact]
    public void A_plain_text_is_read()
    {
        LocalizedString? value = JsonSerializer.Deserialize("\"Kaffee\"", TestJsonContext.Default.LocalizedString);

        Assert.Equal("Kaffee", value?.ToString());
        Assert.False(value?.IsTranslated);
    }

    [Fact]
    public void Translations_are_read()
    {
        LocalizedString? value = JsonSerializer.Deserialize(
            """{"de":"Kaffee","en":"Coffee"}""",
            TestJsonContext.Default.LocalizedString);

        Assert.True(value?.IsTranslated);
        Assert.Equal("Kaffee", value?.Get("de"));
        Assert.Equal("Coffee", value?.Get("en"));
        Assert.Equal(["de", "en"], value?.Languages);
    }

    [Fact]
    public void A_language_tag_is_matched_regardless_of_case()
    {
        // Emporix has been seen sending both «de» and «DE».
        LocalizedString? value = JsonSerializer.Deserialize("""{"DE":"Kaffee"}""", TestJsonContext.Default.LocalizedString);

        Assert.Equal("Kaffee", value?.Get("de"));
    }

    [Fact]
    public void A_single_text_answers_for_every_language()
    {
        // Emporix already translated, so there is nothing left to choose between.
        LocalizedString value = new("Kaffee");

        Assert.Equal("Kaffee", value.Get("de"));
        Assert.Equal("Kaffee", value.Get("fr"));
    }

    [Fact]
    public void An_absent_language_is_null_rather_than_a_wrong_one()
    {
        LocalizedString? value = JsonSerializer.Deserialize("""{"de":"Kaffee"}""", TestJsonContext.Default.LocalizedString);

        Assert.Null(value?.Get("fr"));
        Assert.Equal("Kaffee", value?.GetOrAny("fr"));
    }

    [Fact]
    public void A_null_translation_is_absent_rather_than_empty()
    {
        LocalizedString? value = JsonSerializer.Deserialize(
            """{"de":"Kaffee","fr":null}""",
            TestJsonContext.Default.LocalizedString);

        Assert.Null(value?.Get("fr"));
        Assert.DoesNotContain("fr", value?.Languages ?? []);
    }

    [Fact]
    public void Each_shape_is_written_back_as_it_was_read()
    {
        // Writing translations as a single text would drop every language but
        // one, and Emporix would store that loss.
        Assert.Equal(
            "\"Kaffee\"",
            JsonSerializer.Serialize(new LocalizedString("Kaffee"), TestJsonContext.Default.LocalizedString));

        Assert.Equal(
            """{"de":"Kaffee"}""",
            JsonSerializer.Serialize(
                new LocalizedString(new Dictionary<string, string> { ["de"] = "Kaffee" }),
                TestJsonContext.Default.LocalizedString));
    }

    [Fact]
    public void A_shape_the_specification_does_not_describe_is_rejected()
    {
        // Silently accepting a number would hide a real change upstream.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("42", TestJsonContext.Default.LocalizedString));
    }

    [Fact]
    public async Task A_product_parses_in_both_shapes()
    {
        // The end-to-end case: the same endpoint, the two shapes it answers with.
        foreach (string body in new[]
        {
            """[{"id":"p1","name":{"de":"Kaffee"}}]""",
            """[{"id":"p1","name":"Kaffee"}]""",
        })
        {
            StubHttpMessageHandler handler = new(HttpStatusCode.OK, body);
            IOptions<EmporixOptions> options =
                Microsoft.Extensions.Options.Options.Create(new EmporixOptions { Tenant = "acme" });

            ProductService products = new(
                new EmporixHttpClient(new HttpClient(handler), options),
                options,
                NullLogger<ProductService>.Instance);

            PaginatedItems<ProductModels.BasicProductWithId> page =
                await products.ListAsync(auth: AuthContext.Anonymous());

            Assert.Equal("Kaffee", Assert.Single(page.Items).Name?.ToString());
        }
    }
}
