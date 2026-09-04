using System.Text.Json;
using System.Text.Json.Serialization;
using Viu.Emporix.QuoteModels;

namespace Viu.Emporix;

/// <summary>
/// Writes a quote request as whichever of the two shapes it is.
/// </summary>
/// <remarks>
/// <para>
/// The same arrangement as the product write converters, minus their
/// complications: two branches instead of five, no array of mixed types, and
/// two unrelated classes rather than a hierarchy where a derived value can be
/// passed where its base is expected.
/// </para>
/// <para>
/// Dispatch is still on exact runtime type. The interface is public, so a
/// consumer can implement it and arrive here with something the specification
/// does not permit; comparing exactly turns that into a message naming the two
/// rather than a body Emporix rejects for reasons the caller cannot see.
/// </para>
/// <para>
/// Internal, unlike <c>NullOnUnknownEnumConverter</c>: that one is named in a
/// <c>JsonConverterAttribute</c>, which the source generator insists be public.
/// A converter reached through a context's <c>Converters</c> list has no such
/// requirement, so it stays off the public surface — as the product write
/// converters do.
/// </para>
/// </remarks>
internal sealed class EmporixQuoteCreationConverter : JsonConverter<IEmporixQuoteCreation>
{
    private const string Permitted = "QuoteCreateRequest, QuoteCreateFromCartRequest";

    /// <summary>Refused: these are request bodies.</summary>
    /// <param name="reader">Unused.</param>
    /// <param name="typeToConvert">Unused.</param>
    /// <param name="options">Unused.</param>
    public override IEmporixQuoteCreation? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException(
            "A quote creation type is a request body; the endpoint answers with a QuoteIdResponse. "
            + "Read a quote through QuoteService.GetAsync instead.");

    /// <summary>Writes the value through its own type's contract.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The quote request.</param>
    /// <param name="options">Unused; the contract comes from the context.</param>
    public override void Write(
        Utf8JsonWriter writer,
        IEmporixQuoteCreation value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        Type type = value.GetType();

        if (type == typeof(QuoteCreateRequest))
        {
            JsonSerializer.Serialize(
                writer, (QuoteCreateRequest)value, QuoteJsonContext.Default.QuoteCreateRequest);
            return;
        }

        if (type == typeof(QuoteCreateFromCartRequest))
        {
            JsonSerializer.Serialize(
                writer, (QuoteCreateFromCartRequest)value, QuoteJsonContext.Default.QuoteCreateFromCartRequest);
            return;
        }

        throw new NotSupportedException(
            $"{type.Name} cannot be written as a quote creation. The specification permits exactly: "
            + $"{Permitted}. Implementing IEmporixQuoteCreation outside the SDK does not make a shape "
            + "the endpoint accepts.");
    }
}
