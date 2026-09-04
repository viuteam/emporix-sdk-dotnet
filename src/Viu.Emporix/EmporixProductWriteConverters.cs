using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix;

/// <summary>
/// The one place a write converter decides that a value is exactly some type.
/// </summary>
/// <remarks>
/// <para>
/// Shared by all three converters so that the exactness check exists once
/// rather than fifteen times. That check is the safety property here: the
/// generated write hierarchy is not disjoint — a bulk update derives from its
/// plain update, and four response types derive from their creation type — so
/// a <c>value is BasicProductUpdate</c> pattern match would accept a derived
/// value and serialize it through the base contract, dropping whatever the
/// derived type adds with no error at all.
/// </para>
/// <para>
/// One branch out of fifteen written as a pattern match would reintroduce that,
/// and no test of the other fourteen would notice.
/// </para>
/// </remarks>
internal static class ProductWriteDispatch
{
    /// <summary>
    /// Serializes <paramref name="value"/> when it is exactly
    /// <typeparamref name="T"/>, and reports whether it did.
    /// </summary>
    public static bool TryWrite<T>(
        Utf8JsonWriter writer,
        object value,
        Type type,
        JsonTypeInfo<T> typeInfo)
    {
        if (type != typeof(T))
        {
            return false;
        }

        JsonSerializer.Serialize(writer, (T)value, typeInfo);
        return true;
    }

    /// <summary>
    /// The exception for a type no branch matched, saying why it compiled.
    /// </summary>
    /// <remarks>
    /// The «derives from» sentence is the whole point of this message. Someone
    /// who passes a product they just fetched into a create call needs to be
    /// told that the response type inherits the creation type, or the refusal
    /// reads as a bug in the SDK.
    /// </remarks>
    public static NotSupportedException Unsupported(Type type, string body, string permitted)
        => new(
            $"{type.Name} cannot be written as a {body}. The specification permits exactly: {permitted}. "
            + "A type outside that list can reach here because the generated hierarchy is not disjoint — "
            + $"{type.Name} derives from one of them, which is why the call compiled. Serializing it "
            + "through its base type would silently drop the fields it adds, so it is refused instead.");
}

/// <summary>
/// Writes a product as whichever of the five creation shapes it is.
/// </summary>
internal sealed class EmporixProductCreationConverter : JsonConverter<IEmporixProductCreation>
{
    private const string Permitted =
        "BasicProductCreation, BundleProductCreation, ParentVariantProductCreation, "
        + "VariantProductCreation, DynamicVariantProductCreation";

    public override IEmporixProductCreation? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException(
            "A creation type is a request body; no Emporix endpoint returns one. "
            + "Read a product through ProductService or its AnyType group instead.");

    public override void Write(
        Utf8JsonWriter writer,
        IEmporixProductCreation value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        Type type = value.GetType();

        if (ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BasicProductCreation)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BundleProductCreation)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.ParentVariantProductCreation)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.VariantProductCreation)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.DynamicVariantProductCreation))
        {
            return;
        }

        throw ProductWriteDispatch.Unsupported(type, "product creation", Permitted);
    }
}

/// <summary>
/// Writes a product as whichever of the five update shapes it is.
/// </summary>
/// <remarks>
/// For <c>PUT</c> only. <c>PATCH</c> declares a single flat
/// <c>productPartialUpdate</c> and needs no converter.
/// </remarks>
internal sealed class EmporixProductUpdateConverter : JsonConverter<IEmporixProductUpdate>
{
    private const string Permitted =
        "BasicProductUpdate, BundleProductUpdate, ParentVariantProductUpdate, "
        + "VariantProductUpdate, DynamicVariantProductUpdate";

    public override IEmporixProductUpdate? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException(
            "An update type is a request body; no Emporix endpoint returns one. "
            + "Read a product through ProductService or its AnyType group instead.");

    public override void Write(
        Utf8JsonWriter writer,
        IEmporixProductUpdate value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        Type type = value.GetType();

        if (ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BasicProductUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BundleProductUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.ParentVariantProductUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.VariantProductUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.DynamicVariantProductUpdate))
        {
            return;
        }

        throw ProductWriteDispatch.Unsupported(type, "product update", Permitted);
    }
}

/// <summary>
/// Writes a product as whichever of the five bulk-update shapes it is.
/// </summary>
/// <remarks>
/// This is what lets one bulk call carry a mix of shapes, which the
/// specification's array of <c>oneOf</c> permits and a per-type method could
/// not offer.
/// </remarks>
internal sealed class EmporixProductBulkUpdateConverter : JsonConverter<IEmporixProductBulkUpdate>
{
    private const string Permitted =
        "BasicProductBulkUpdate, BundleProductBulkUpdate, ParentVariantProductBulkUpdate, "
        + "VariantProductBulkUpdate, DynamicVariantProductBulkUpdate";

    public override IEmporixProductBulkUpdate? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException(
            "A bulk update type is a request body; the bulk endpoint answers with BulkResponse entries.");

    public override void Write(
        Utf8JsonWriter writer,
        IEmporixProductBulkUpdate value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        Type type = value.GetType();

        if (ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BasicProductBulkUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BundleProductBulkUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.ParentVariantProductBulkUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.VariantProductBulkUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.DynamicVariantProductBulkUpdate))
        {
            return;
        }

        throw ProductWriteDispatch.Unsupported(type, "product bulk update", Permitted);
    }
}
