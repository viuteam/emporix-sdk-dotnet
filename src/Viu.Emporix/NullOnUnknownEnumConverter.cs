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
/// <b>Case-insensitive, and that is a constraint rather than a choice.</b>
/// <c>JsonStringEnumConverter</c> matches without regard to case, and 180
/// generated members differ from their wire value in case alone — the
/// specifications write <c>string</c> where NSwag had to emit <c>String</c> to
/// get a legal identifier. Tightening this would break every one of them.
/// </para>
/// <para>
/// Writing is unchanged from the strict converter: the member's name, which is
/// what System.Text.Json has always sent. It ignores the
/// <c>EnumMember</c> attributes NSwag emits, so those 180 differences are
/// written in the member's casing today and stay that way here — this converter
/// deliberately does not fix that, because doing so would change what the SDK
/// puts on the wire.
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
    /// <summary>
    /// Reads the value, or <see langword="null"/> when it is not one this
    /// enum declares.
    /// </summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="typeToConvert">Unused; the type is the parameter.</param>
    /// <param name="options">Unused; nothing here depends on them.</param>
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType is not JsonTokenType.String)
        {
            return null;
        }

        // Enum.TryParse answers true for «99» and hands back the raw number
        // whether or not a member carries it, so the parse alone is not enough:
        // an out-of-range value would arrive looking like a real one. It also
        // accepts comma-separated lists for flag enums, which none of these
        // are. IsDefined closes both.
        return Enum.TryParse(reader.GetString(), ignoreCase: true, out T value)
            && Enum.IsDefined(value)
            ? value
            : null;
    }

    /// <summary>Writes the member's name, as the strict converter does.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value; a null is written as a JSON null.</param>
    /// <param name="options">Unused; nothing here depends on them.</param>
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

        writer.WriteStringValue(value.Value.ToString());
    }
}
