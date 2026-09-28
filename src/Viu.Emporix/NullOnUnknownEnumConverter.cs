using System.Text.Json;
using System.Text.Json.Serialization;

namespace Viu.Emporix;

/// <summary>
/// Reads an enum value the vendored specification does not list as
/// <see langword="null"/> rather than throwing.
/// </summary>
/// <typeparam name="T">The generated enum type.</typeparam>
/// <remarks>
/// <para>
/// The failure this exists for is not that one field becomes unreadable. It is
/// that the whole response does: a product, an order, a page of sixty, all lost
/// because one field carried a value Emporix had added and the vendored
/// specification had not caught up with. Losing one optional field is a far
/// smaller price.
/// </para>
/// <para>
/// Attached by <c>GeneratedCodeFixer</c> to nullable enum properties only. The
/// non-nullable ones keep NSwag's strict converter: the specification marks
/// those required, so an unrecognised value there is a broken contract rather
/// than a field a caller can shrug off — and there is no null to put in it.
/// </para>
/// <para>
/// <b>Both directions go through the strict converter</b>, so a nullable
/// property puts the same value on the wire as a required one: the value the
/// specification declares, which <c>GeneratedCodeFixer</c> records as
/// <c>JsonStringEnumMemberName</c> wherever it differs from the member's name.
/// This converter used to write the member's name itself. That matched the
/// strict converter until the fixer taught the strict one the declared values,
/// and from then on a nullable property sent <c>Kg</c> where the specification
/// declares <c>kg</c> — and read <c>/status</c> as unknown, because no member
/// is called that.
/// </para>
/// <para>
/// <b>Case-insensitive, and that is a constraint rather than a choice.</b>
/// <c>JsonStringEnumConverter</c> matches without regard to case, and 180
/// generated members differ from their wire value in case alone — the
/// specifications write <c>string</c> where NSwag had to emit <c>String</c> to
/// get a legal identifier. Tightening this would break every one of them.
/// </para>
/// <para>
/// <b>Public because it has to be.</b> System.Text.Json's source generator
/// rejects an <c>internal</c> converter named in a <c>JsonConverterAttribute</c>
/// with <c>SYSLIB1220</c>, saying it is «not a converter type or does not
/// contain an accessible parameterless constructor» — which is true of neither
/// but is what the check reports. Nothing outside the SDK needs to name this
/// type.
/// </para>
/// </remarks>
public sealed class NullOnUnknownEnumConverter<T> : JsonConverter<T?>
    where T : struct, Enum
{
    private JsonConverter<T>? _strict;

    /// <summary>
    /// Reads the value, or <see langword="null"/> when it is not one this
    /// enum declares.
    /// </summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="typeToConvert">Unused; the type is the parameter.</param>
    /// <param name="options">Passed on to the strict converter.</param>
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            return null;
        }

        T value;

        try
        {
            value = Strict(options).Read(ref reader, typeof(T), options);
        }
        catch (JsonException)
        {
            return null;
        }

        // The strict converter reads «99» as the raw number whether or not a
        // member carries it, so an out-of-range value would arrive looking like
        // a real one. IsDefined closes that.
        return Enum.IsDefined(value) ? value : null;
    }

    /// <summary>Writes the value the specification declares, as the strict converter does.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value; a null is written as a JSON null.</param>
    /// <param name="options">Passed on to the strict converter.</param>
    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Reached only when a context does not ignore nulls. Every context in
        // this SDK sets DefaultIgnoreCondition to WhenWritingNull, so a value
        // that could not be read is omitted rather than sent back as null —
        // which on a PATCH would clear the field.
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        Strict(options).Write(writer, value.Value, options);
    }

    // The generic JsonStringEnumConverter is the trim-safe one: it reads the
    // JsonStringEnumMemberName attributes without the reflection this SDK
    // does not allow itself.
    private JsonConverter<T> Strict(JsonSerializerOptions options)
        => _strict ??= (JsonConverter<T>)new JsonStringEnumConverter<T>().CreateConverter(typeof(T), options);
}
