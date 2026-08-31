using System.Text.RegularExpressions;

namespace Viu.Emporix.SpecSync;

/// <summary>
/// Finds the properties a specification declares as localized values.
/// </summary>
/// <remarks>
/// <para>
/// Emporix models a localized field as a reference to a schema whose type is
/// <c>oneOf: [string, object]</c> — the same field arrives as a plain text when
/// the request asked for one language and as a map of translations when it did
/// not. A generator has to pick one, and the one it picks is wrong half the
/// time.
/// </para>
/// <para>
/// This reads which properties are affected out of the specification, so the
/// post-processing retypes exactly those. Matching on names like «name» or
/// «description» would be guesswork: plenty of fields carry those names and are
/// ordinary strings.
/// </para>
/// </remarks>
internal static partial class LocalizedProperties
{
    /// <summary>
    /// Returns the localized properties as <c>ClassName.PropertyName</c>, in the
    /// spelling the generated code uses.
    /// </summary>
    public static IReadOnlyCollection<string> Read(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        string[] lines = yaml.Split('\n');
        HashSet<string> localizing = FindLocalizingSchemas(lines);

        if (localizing.Count == 0)
        {
            return [];
        }

        List<string> properties = [];
        string? schema = null;
        string? property = null;

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');

            Match schemaName = SchemaName().Match(line);
            if (schemaName.Success)
            {
                schema = schemaName.Groups[1].Value;
                property = null;
                continue;
            }

            Match propertyName = PropertyName().Match(line);
            if (propertyName.Success)
            {
                property = propertyName.Groups[1].Value;
                continue;
            }

            Match reference = SchemaReference().Match(line);
            if (reference.Success
                && schema is not null
                && property is not null
                && localizing.Contains(reference.Groups[1].Value))
            {
                properties.Add($"{Pascal(schema)}.{Pascal(property)}");
            }
        }

        return properties;
    }

    /// <summary>
    /// Collects the schemas that are a union of a text and a map of texts.
    /// </summary>
    /// <remarks>
    /// Recognised by shape rather than by name: the specifications call this
    /// <c>localizedValue</c> today, but nothing guarantees the next one will.
    /// </remarks>
    private static HashSet<string> FindLocalizingSchemas(string[] lines)
    {
        HashSet<string> found = [];

        for (int i = 0; i < lines.Length; i++)
        {
            Match schemaName = SchemaName().Match(lines[i].TrimEnd('\r'));
            if (!schemaName.Success)
            {
                continue;
            }

            // The union is short; looking past a dozen lines would start
            // catching the next schema's body.
            string window = string.Join('\n', lines.Skip(i + 1).Take(12));

            if (window.Contains("oneOf:", StringComparison.Ordinal)
                && window.Contains("type: string", StringComparison.Ordinal)
                && window.Contains("type: object", StringComparison.Ordinal)
                && window.Contains("additionalProperties:", StringComparison.Ordinal))
            {
                found.Add(schemaName.Groups[1].Value);
            }
        }

        return found;
    }

    /// <summary>Turns a specification name into the spelling NSwag generates.</summary>
    private static string Pascal(string name)
        => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    [GeneratedRegex(@"^    (\w+):\s*$")]
    private static partial Regex SchemaName();

    [GeneratedRegex(@"^        (\w+):\s*$")]
    private static partial Regex PropertyName();

    [GeneratedRegex(@"\$ref:\s*'#/components/schemas/(\w+)'")]
    private static partial Regex SchemaReference();
}
