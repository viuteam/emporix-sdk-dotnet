using System.Text.RegularExpressions;

namespace Viu.Emporix.SpecSync;

/// <summary>
/// Finds the properties a specification declares as localized values.
/// </summary>
/// <remarks>
/// <para>
/// Emporix models a localized field as <c>oneOf: [string, object]</c> — the same
/// field arrives as plain text when the request asked for one language and as a
/// map of translations when it did not. A generator has to pick one, and the one
/// it picks is wrong half the time.
/// </para>
/// <para>
/// The union appears in two forms, and both have to be recognised. Most
/// specifications name it and reference it (<c>$ref: …/localizedValue</c>);
/// others spell it out inline on the property. Only the first form was handled
/// at first, which is how <c>taxClass.name</c> shipped typed as a map — the live
/// API answers it with a string, and reading a tax configuration threw.
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
        => Scan(yaml, unions: false);

    /// <summary>
    /// Returns the properties whose schema is a union of several object types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NSwag resolves such a union to its first branch, which types the property
    /// as one of the alternatives and makes every other one unreadable. The AI
    /// service's <c>llmConfig</c> is the case that showed up: an agent using
    /// <c>provider: openai</c> could not be read at all, because the generated
    /// type only admits <c>emporix_openai</c>. The specification's own examples
    /// use the value its generated type rejects.
    /// </para>
    /// <para>
    /// Four properties across all 44 specifications are like this, and all four
    /// are provider configurations with no discriminator a generator could use.
    /// They are retyped to <c>JsonElement</c>: reading the branch the caller
    /// actually has is their decision, and losing the other branches' fields
    /// silently is not an option.
    /// </para>
    /// </remarks>
    public static IReadOnlyCollection<string> ReadUnions(string yaml)
        => Scan(yaml, unions: true);

    private static List<string> Scan(string yaml, bool unions)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        string[] lines = yaml.Split('\n');
        // No early return when this is empty: a specification can carry the
        // union inline on the property and name no schema at all, which is what
        // the tax service does. Bailing out here is what let taxClass.name ship
        // typed as a map.
        HashSet<string> localizing = FindLocalizingSchemas(lines);

        List<string> properties = [];

        // The path to the property currently being read, innermost last:
        // [«QuoteResponseItem», «zone», «name»]. A localized field is often two
        // levels down — inside an inline object or an array's items — and
        // attributing it to the outermost property is what reported
        // «QuoteResponseItem.Zone» while the property that actually needed
        // retyping was the «name» inside it.
        List<(int Indent, string Name)> path = [];
        HashSet<int> propertyIndents = [];

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            Match schemaName = SchemaName().Match(line);
            if (schemaName.Success)
            {
                path = [(4, schemaName.Groups[1].Value)];
                propertyIndents = [];
                continue;
            }

            if (path.Count == 0)
            {
                continue;
            }

            int indent = Indent(line);
            string trimmed = line.TrimStart();

            // Everything directly under a «properties:» mapping is a property
            // name. Everything else at that depth — «type:», «items:»,
            // «allOf:» — is structure.
            if (trimmed.StartsWith("properties:", StringComparison.Ordinal))
            {
                propertyIndents.Add(indent + 2);
                continue;
            }

            Match name = AnyName().Match(line);
            if (name.Success && propertyIndents.Contains(indent))
            {
                while (path.Count > 1 && path[^1].Indent >= indent)
                {
                    path.RemoveAt(path.Count - 1);
                }

                path.Add((indent, name.Groups[1].Value));

                if (!unions && IsInlineUnion(lines, i))
                {
                    properties.Add(Join(path));
                }
                else if (unions && IsObjectUnion(lines, i))
                {
                    properties.Add(Join(path));
                }

                continue;
            }

            Match reference = SchemaReference().Match(line);
            if (!unions
                && reference.Success
                && path.Count > 1
                && indent > path[^1].Indent
                && localizing.Contains(reference.Groups[1].Value))
            {
                properties.Add(Join(path));
            }
        }

        return properties;
    }

    /// <summary>
    /// Renders the path in the spelling the generated code uses.
    /// </summary>
    private static string Join(List<(int Indent, string Name)> path)
        => string.Join('.', path.Select(part => Pascal(part.Name)));

    /// <summary>
    /// Decides whether the property starting at <paramref name="start"/> spells
    /// the union out inline.
    /// </summary>
    /// <remarks>
    /// Reads the property's own block — every following line indented deeper
    /// than the property name — rather than a fixed number of lines. A window
    /// would work until a specification puts a five-paragraph description
    /// between the property and its <c>oneOf</c>, which is exactly what the tax
    /// service does.
    /// </remarks>
    private static bool IsInlineUnion(string[] lines, int start)
    {
        int indent = Indent(lines[start]);
        int union = -1;

        // The union has to be the property's own, at its immediate child
        // indentation. Scanning the whole block instead matches a oneOf nested
        // three levels down inside an array's items — which flagged
        // «AggregateFee.Elements» and «QuoteResponseItem.Zone» as localized text
        // when neither is text at all.
        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            int depth = Indent(line);

            if (depth <= indent)
            {
                break;
            }

            if (depth == indent + 2 && line.TrimStart().StartsWith("oneOf:", StringComparison.Ordinal))
            {
                union = i;
                break;
            }
        }

        if (union < 0)
        {
            return false;
        }

        bool text = false;
        bool map = false;

        for (int i = union + 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            if (Indent(line) <= indent + 2)
            {
                break;
            }

            string trimmed = line.TrimStart();

            text |= trimmed is "- type: string" or "type: string";
            map |= trimmed.StartsWith("additionalProperties:", StringComparison.Ordinal);
        }

        // Both branches, or it is a union of two unrelated shapes.
        return text && map;
    }

    /// <summary>
    /// Decides whether the property at <paramref name="start"/> is a union of
    /// two or more named object types.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than <see cref="IsInlineUnion"/>: only branches
    /// that are <c>$ref</c>s count. A union of a string and a map is a localized
    /// value and belongs to the other rule; a union with one branch is not a
    /// union.
    /// </remarks>
    private static bool IsObjectUnion(string[] lines, int start)
    {
        int indent = Indent(lines[start]);
        int union = -1;

        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            int depth = Indent(line);

            if (depth <= indent)
            {
                return false;
            }

            if (depth == indent + 2)
            {
                if (!line.TrimStart().StartsWith("oneOf:", StringComparison.Ordinal))
                {
                    return false;
                }

                union = i;
                break;
            }
        }

        if (union < 0)
        {
            return false;
        }

        int refs = 0;

        for (int i = union + 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            if (Indent(line) <= indent + 2)
            {
                break;
            }

            if (SchemaReference().IsMatch(line))
            {
                refs++;
            }
        }

        return refs >= 2;
    }

    private static int Indent(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ')
        {
            i++;
        }

        return i;
    }

    /// <summary>
    /// Collects the schemas that are a union of a text and a map of texts.
    /// </summary>
    /// <remarks>
    /// Recognised by shape rather than by name: the specifications call this
    /// <c>localizedValue</c> today, but nothing guarantees the next one will.
    /// The same check serves a property that spells the union out inline, which
    /// is the only difference between the two forms Emporix uses.
    /// </remarks>
    private static HashSet<string> FindLocalizingSchemas(string[] lines)
    {
        HashSet<string> found = [];

        for (int i = 0; i < lines.Length; i++)
        {
            Match schemaName = SchemaName().Match(lines[i].TrimEnd('\r'));

            if (schemaName.Success && IsInlineUnion(lines, i))
            {
                found.Add(schemaName.Groups[1].Value);
            }
        }

        return found;
    }

    /// <summary>Turns a specification name into the spelling NSwag generates.</summary>
    private static string Pascal(string name)
        => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    // `(?!…)` excludes the HTTP verbs: a path operation sits at the same
    // indentation as a schema name, and its requestBody carries a `content`
    // property — which is how «Post.Content» reached the localized list.
    [GeneratedRegex(@"^    (?!get:|put:|post:|patch:|delete:|head:|options:|trace:)(\w+):\s*$")]
    private static partial Regex SchemaName();

    [GeneratedRegex(@"^\s*(\w+):\s*$")]
    private static partial Regex AnyName();

    [GeneratedRegex(@"\$ref:\s*'#/components/schemas/(\w+)'")]
    private static partial Regex SchemaReference();
}
