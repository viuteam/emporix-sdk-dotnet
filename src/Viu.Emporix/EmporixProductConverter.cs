using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix;

/// <summary>
/// Reads a product response into the shape its <c>productType</c> names.
/// </summary>
/// <remarks>
/// <para>
/// A converter rather than logic inside the facade, for one reason: a mixed
/// list then costs nothing. <c>PaginatedItems&lt;IEmporixProduct&gt;</c> and
/// <c>List&lt;IEmporixProduct&gt;</c> work without a second code path.
/// </para>
/// <para>
/// <c>[JsonPolymorphic]</c> would be the idiomatic route and is not available
/// here: <c>productType</c> is both the natural discriminator and a declared
/// field on all five types, which System.Text.Json refuses. Using it would mean
/// hiding the field with <c>[JsonIgnore]</c> and inventing a base class for the
/// dynamic variant shape.
/// </para>
/// </remarks>
internal sealed class EmporixProductConverter : JsonConverter<IEmporixProduct>
{
    public override IEmporixProduct? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string? productType = PeekProductType(reader);

        return productType switch
        {
            "BUNDLE" => Take(ref reader, ProductJsonContext.Default.BundleProductWithId),
            "PARENT_VARIANT" => Take(ref reader, ProductJsonContext.Default.ParentVariantProductWithId),
            "VARIANT" => Take(ref reader, ProductJsonContext.Default.VariantProductWithId),
            "DYNAMIC_VARIANT" => Take(ref reader, ProductJsonContext.Default.DynamicVariantProductWithId),

            // BASIC, an unknown value, and no value at all. Emporix has extended
            // the list before, and the specification does not require the field
            // on a variant — so this has to be a fallback rather than an error.
            // The basic shape carries every shared field plus extension data, so
            // nothing reachable is lost.
            _ => Take(ref reader, ProductJsonContext.Default.BasicProductWithId),
        };

        static IEmporixProduct? Take<T>(ref Utf8JsonReader reader, JsonTypeInfo<T> typeInfo)
            where T : IEmporixProduct
            => JsonSerializer.Deserialize(ref reader, typeInfo);
    }

    /// <summary>
    /// Finds <c>productType</c> without consuming anything.
    /// </summary>
    /// <remarks>
    /// <see cref="Utf8JsonReader"/> is a struct, so the copy scans ahead while
    /// the original stays where the real deserialization needs it.
    /// </remarks>
    private static string? PeekProductType(Utf8JsonReader peek)
    {
        // The reader arrives positioned on the value's StartObject, which the
        // loop below never observes — so the object we are already inside counts
        // as depth 1 from the start. Initialising this to 0 is silent: the guard
        // never matches, every product resolves to the fallback, and nothing
        // errors. That is how the first version of this method behaved.
        int depth = 1;

        while (peek.Read())
        {
            switch (peek.TokenType)
            {
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    depth++;
                    break;

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (--depth == 0)
                    {
                        return null;
                    }

                    break;

                // Depth 1 only: a dynamic variant's «variants» map carries
                // entries with their own productType, and an inner value must
                // not decide the outer shape.
                case JsonTokenType.PropertyName
                    when depth == 1 && peek.ValueTextEquals("productType"):
                    return peek.Read() && peek.TokenType == JsonTokenType.String
                        ? peek.GetString()
                        : null;

                default:
                    break;
            }
        }

        return null;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IEmporixProduct value,
        JsonSerializerOptions options)
        => throw new NotSupportedException(
            "A product is written through its concrete update type — BasicProductUpdate and its siblings. "
            + "The update schemas carry different fields from the response schemas, so a product that was "
            + "read cannot be sent back unchanged.");
}
