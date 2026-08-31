using System.Text.RegularExpressions;

namespace Viu.Emporix.SpecSync;

/// <summary>
/// Cleans up two quirks of the generated code that do not compile.
/// </summary>
/// <remarks>
/// <para>
/// Emporix describes some fields as an <c>allOf</c> of exactly one type plus a
/// description — an alias in TypeScript, but a class of its own in C#. NSwag
/// turns that into an empty class deriving from its single constituent:
/// <c>public partial class Name : string { }</c>. You cannot derive from
/// <c>string</c>, and even where you could the class would hold nothing.
/// </para>
/// <para>
/// Repairing happens in the pipeline, not by hand in the generated file: the
/// next run would overwrite a manual correction.
/// </para>
/// </remarks>
internal static partial class GeneratedCodeFixer
{
    /// <summary>
    /// Dissolves empty derived classes into their base type and reports what was
    /// dissolved.
    /// </summary>
    public static (string Source, IReadOnlyList<string> Resolved) ResolveEmptyAliasClasses(string source)
    {
        MatchCollection matches = EmptyAliasClass().Matches(source);

        if (matches.Count == 0)
        {
            return (source, []);
        }

        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        string result = source;

        foreach (Match match in matches)
        {
            aliases[match.Groups["name"].Value] = match.Groups["baseType"].Value;
            result = result.Replace(match.Value, string.Empty, StringComparison.Ordinal);
        }

        // Only now rewrite the usages, and only at type positions: a field is
        // often named exactly like its type (»public Name? Name«), so replacing
        // across the whole file would hit the field name too.
        foreach ((string alias, string baseType) in aliases)
        {
            result = PropertyDeclaration(alias).Replace(result, $"public {baseType}$1 $2");
            result = GenericArgument(alias).Replace(result, $"<{baseType}>");

            // Carry the initialiser along: »= new Name()« would otherwise become
            // »= new string()«, which does not exist.
            result = result.Replace(
                $"new {alias}()",
                KnownTypes.Contains(baseType) ? "default!" : $"new {baseType}()",
                StringComparison.Ordinal);
        }

        // A final tidy-up: the primitive types have no parameterless
        // constructor. »new string()« is never valid C#, however it came about.
        foreach (string primitive in KnownTypes)
        {
            result = result.Replace(
                $"= new {primitive}()",
                primitive == "string" ? "= string.Empty" : "= default!",
                StringComparison.Ordinal);
        }

        return (result, [.. aliases.Select(pair => $"{pair.Key} → {pair.Value}")]);
    }

    /// <summary>
    /// Replaces references to types that were never generated with raw JSON.
    /// </summary>
    /// <remarks>
    /// Emporix describes some fields as a <c>oneOf</c> without a discriminator —
    /// «either just the id or the full object». NSwag cannot form a class from
    /// that but still references one on the field: what remains is a dangling
    /// reference.
    /// <para>
    /// Such fields become <see cref="System.Text.Json.JsonElement"/>. That is the
    /// honest mapping — the shape is only known at runtime — and stays readable
    /// without reflection (ADR-0004). Mapping a union properly remains hand work
    /// in the respective facade; see ADR-0001.
    /// </para>
    /// </remarks>
    public static (string Source, IReadOnlyList<string> Replaced) ResolveDanglingTypeReferences(string source)
    {
        SortedSet<string> replaced = new(StringComparer.Ordinal);
        Dictionary<string, bool> declaredCache = new(StringComparer.Ordinal);

        string result = TypedProperty().Replace(source, match =>
        {
            string typeName = match.Groups["type"].Value;

            if (KnownTypes.Contains(typeName) || IsTypeParameter(typeName) || IsDeclared(typeName))
            {
                return match.Value;
            }

            replaced.Add(typeName);
            return $"public System.Text.Json.JsonElement{match.Groups["nullable"].Value} "
                + match.Groups["rest"].Value;
        });

        // The same for collections: »ICollection<Conditions>« does not match the
        // rule for plain fields.
        result = GenericTypeArgument().Replace(result, match =>
        {
            string typeName = match.Groups["type"].Value;

            if (KnownTypes.Contains(typeName) || IsTypeParameter(typeName) || IsDeclared(typeName))
            {
                return match.Value;
            }

            replaced.Add(typeName);
            return match.Groups["prefix"].Value + "System.Text.Json.JsonElement>";
        });

        return (result, [.. replaced]);

        // Search per type name rather than collecting every declaration at once:
        // this version reads plainly and is obviously correct, and the cache
        // takes the cost out of it.
        bool IsDeclared(string typeName)
        {
            if (declaredCache.TryGetValue(typeName, out bool cached))
            {
                return cached;
            }

            bool declared = Regex.IsMatch(
                source,
                $@"\b(?:class|enum|interface|record|struct)\s+{Regex.Escape(typeName)}\b",
                RegexOptions.None,
                TimeSpan.FromSeconds(5));

            declaredCache[typeName] = declared;
            return declared;
        }
    }

    /// <summary>
    /// Whether the name is a generic type parameter by .NET convention
    /// (<c>T</c>, <c>TResult</c>, <c>TKey</c>).
    /// </summary>
    /// <remarks>
    /// A type parameter is declared nowhere as a type and would therefore look
    /// like a dangling reference. Replacing it would destroy the class that
    /// introduces it.
    /// </remarks>
    /// <summary>
    /// Retypes the properties a specification declares as localized values.
    /// </summary>
    /// <param name="source">The generated file.</param>
    /// <param name="localized">
    /// The properties to retype, as <c>ClassName.PropertyName</c>, taken from
    /// the specification rather than guessed at from the names.
    /// </param>
    /// <remarks>
    /// <para>
    /// Emporix declares a localized field as <c>oneOf: [string, object]</c> —
    /// the same field arrives as <c>"Kaffee"</c> when the request asked for one
    /// language and as <c>{"de":"Kaffee"}</c> when it did not. NSwag resolves the
    /// union to its first branch and types the property <c>string</c>, so the
    /// untranslated shape fails to parse: reading products from a real tenant
    /// throws unless <c>Accept-Language</c> happens to be set.
    /// </para>
    /// <para>
    /// <see cref="T:Viu.Emporix.LocalizedString"/> reads both shapes, so the
    /// properties are pointed at it here. Which classes and properties are
    /// affected is read out of the specification during generation — a name
    /// like «Name» or «Description» is not evidence of anything.
    /// </para>
    /// </remarks>
    public static (string Source, IReadOnlyList<string> Retyped) RetypeLocalizedProperties(
        string source,
        IReadOnlyCollection<string> localized)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(localized);

        List<string> retyped = [];
        string result = source;

        foreach (string entry in localized)
        {
            int separator = entry.LastIndexOf('.');
            if (separator <= 0)
            {
                continue;
            }

            string className = entry[..separator];
            string propertyName = entry[(separator + 1)..];

            // Anchored on the class so a property name shared by several classes
            // is only touched where the specification says it is localized.
            Regex declaration = new(
                $@"(?<class>public partial class {Regex.Escape(className)}\b(?:[^{{]*)\{{)(?<body>.*?)(?<property>public\s+)string\??(?<tail>\s+{Regex.Escape(propertyName)}\s*\{{)",
                RegexOptions.Singleline);

            Match match = declaration.Match(result);
            if (!match.Success)
            {
                continue;
            }

            result = declaration.Replace(
                result,
                m => m.Groups["class"].Value
                    + m.Groups["body"].Value
                    + m.Groups["property"].Value
                    + "Viu.Emporix.LocalizedString?"
                    + m.Groups["tail"].Value,
                1);

            retyped.Add(entry);
        }

        return (result, retyped);
    }

    private static bool IsTypeParameter(string name)
        => name.Length >= 1
            && name[0] == 'T'
            && (name.Length == 1 || char.IsUpper(name[1]));

    /// <summary>
    /// Types that are always known and never come from a specification.
    /// </summary>
    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        "string", "int", "long", "short", "byte", "bool", "double", "float", "decimal",
        "object", "char", "uint", "ulong", "ushort", "sbyte",
    };

    /// <summary>A single unqualified type argument, for example in a collection.</summary>
    [GeneratedRegex(@"(?<prefix>(?:I?Collection|IReadOnlyList|IList|IEnumerable)<)(?<type>[A-Za-z_]\w*)>")]
    private static partial Regex GenericTypeArgument();

    /// <summary>A field whose type is a plain, unqualified name.</summary>
    [GeneratedRegex(@"public (?<type>[A-Za-z_]\w*)(?<nullable>\??) (?<rest>\w+ \{ get)")]
    private static partial Regex TypedProperty();

    /// <summary>
    /// A class with no members of its own that merely derives from another type —
    /// together with the marker NSwag puts in front of it.
    /// </summary>
    [GeneratedRegex(
        @"[ \t]*\[System\.CodeDom\.Compiler\.GeneratedCode\([^\]]*\)\]\r?\n"
        + @"[ \t]*public partial class (?<name>\w+) : (?<baseType>[\w.]+)\r?\n"
        + @"[ \t]*\{\r?\n(?:[ \t]*\r?\n)*[ \t]*\}\r?\n",
        RegexOptions.Multiline)]
    private static partial Regex EmptyAliasClass();

    /// <summary>A field whose type is the alias being dissolved.</summary>
    private static Regex PropertyDeclaration(string alias)
        => new($@"public {Regex.Escape(alias)}(\??) (\w+ \{{ get)", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>The alias as the sole type argument, for example in a collection.</summary>
    private static Regex GenericArgument(string alias)
        => new($@"<{Regex.Escape(alias)}>", RegexOptions.None, TimeSpan.FromSeconds(5));
}
