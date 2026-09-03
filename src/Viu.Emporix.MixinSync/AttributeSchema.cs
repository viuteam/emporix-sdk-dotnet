using System.Text.Json;
using System.Text.Json.Nodes;
using Viu.Emporix.SchemaModels;

namespace Viu.Emporix.MixinSync;

/// <summary>
/// Converts the Schema Service's own attribute model into JSON Schema.
/// </summary>
/// <remarks>
/// The fallback path. A schema's <c>metadata.url</c> is authoritative and is
/// fetched first; this runs only when that fetch fails. More type-safe than the
/// Node SDK's equivalent, which compares type strings — here the generated
/// <see cref="SchemaAttributeType"/> enum makes an unhandled value visible
/// rather than a silent fall-through.
/// </remarks>
public static class AttributeSchema
{
    /// <summary>Builds a JSON Schema object from a schema's attributes.</summary>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The schema, as indented JSON text.</returns>
    public static string FromAttributes(IEnumerable<SchemaAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return Object(attributes).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Object(IEnumerable<SchemaAttribute> attributes)
    {
        JsonObject properties = [];
        JsonArray required = [];

        foreach (SchemaAttribute attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key))
            {
                continue;
            }

            properties[attribute.Key] = Property(attribute);

            if (attribute.Metadata?.Required == true)
            {
                required.Add(attribute.Key);
            }
        }

        JsonObject schema = new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private static JsonObject Property(SchemaAttribute attribute)
    {
        // A localized attribute stores one value per language, so its shape is a
        // map of language tags rather than the scalar the type names. This is
        // what makes MixinQuery.WhereLocalized's path valid for it.
        if (attribute.Metadata?.Localized == true)
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = Scalar(attribute.Type, attribute.Values),
            };
        }

        return attribute.Type switch
        {
            SchemaAttributeType.OBJECT => Object(attribute.Attributes ?? []),
            SchemaAttributeType.ARRAY => ArrayOf(attribute),
            _ => Scalar(attribute.Type, attribute.Values),
        };
    }

    private static JsonObject ArrayOf(SchemaAttribute attribute)
    {
        // ArrayType carries its own SchemaAttributeType and, for an enum array,
        // its own values — so an array of enums maps correctly rather than
        // degrading to an array of strings.
        JsonObject items = attribute.ArrayType is { } element
            ? Scalar(element.Type, element.Values)
            : new JsonObject { ["type"] = "string" };

        return new JsonObject { ["type"] = "array", ["items"] = items };
    }

    private static JsonObject Scalar(
        SchemaAttributeType type,
        ICollection<SchemaAttributeValue>? values) => type switch
        {
            SchemaAttributeType.TEXT => new JsonObject { ["type"] = "string" },
            SchemaAttributeType.NUMBER => new JsonObject { ["type"] = "number" },
            SchemaAttributeType.DECIMAL => new JsonObject { ["type"] = "number" },
            SchemaAttributeType.BOOLEAN => new JsonObject { ["type"] = "boolean" },
            SchemaAttributeType.DATE => new JsonObject { ["type"] = "string", ["format"] = "date" },
            SchemaAttributeType.TIME => new JsonObject { ["type"] = "string", ["format"] = "time" },
            SchemaAttributeType.DATE_TIME => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
            SchemaAttributeType.ENUM => Enumeration(values),

            // A reference is an id, and there is nothing here to resolve it
            // against. The specification says its values list is populated for
            // REFERENCE too, but the value itself travels as a string.
            SchemaAttributeType.REFERENCE => new JsonObject { ["type"] = "string" },

            // OBJECT and ARRAY are handled before this point; a new upstream type
            // degrades to «any» so it still generates rather than stopping the run.
            _ => new JsonObject(),
        };

    private static JsonObject Enumeration(ICollection<SchemaAttributeValue>? values)
    {
        JsonArray allowed = [];

        foreach (SchemaAttributeValue value in values ?? [])
        {
            allowed.Add(value.Value);
        }

        // An enum with no values would generate an unusable type, so it stays a
        // plain string instead.
        return allowed.Count == 0
            ? new JsonObject { ["type"] = "string" }
            : new JsonObject { ["type"] = "string", ["enum"] = allowed };
    }
}
