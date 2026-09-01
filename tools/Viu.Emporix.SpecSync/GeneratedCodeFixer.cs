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
        List<string> renamedBases = [];
        string result = source;

        foreach (Match match in matches)
        {
            string name = match.Groups["name"].Value;
            string baseType = match.Groups["baseType"].Value;

            // NSwag names a schema it cannot title «Anonymous», «Anonymous2» and
            // so on. Where such a type is the base of a named alias, dissolving
            // the alias throws the only meaningful name away and puts
            // «Anonymous2» in a public signature. Renaming the base instead
            // keeps the name the specification gave it.
            if (AnonymousType().IsMatch(baseType))
            {
                result = new Regex($@"\b{Regex.Escape(baseType)}\b").Replace(result, name);

                // The alias now derives from itself. Removing it has to take the
                // attributes and documentation above it too — leaving those
                // behind orphans a [GeneratedCode] attribute onto whatever class
                // comes next, which is a duplicate-attribute error pointing at
                // an innocent type.
                result = new Regex(
                    $@"(?:^[ \t]*(?:\[[^\]]*\]|///[^\n]*)\r?\n)*"
                    + $@"^[ \t]*public partial class {Regex.Escape(name)} : {Regex.Escape(name)}\r?\n"
                    + @"[ \t]*\{\r?\n(?:[ \t]*\r?\n)*[ \t]*\}\r?\n(?:[ \t]*\r?\n)*",
                    RegexOptions.Multiline).Replace(result, string.Empty, 1);

                renamedBases.Add($"{baseType} → {name}");
                continue;
            }

            aliases[name] = baseType;
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

        return (result, [.. renamedBases, .. aliases.Select(pair => $"{pair.Key} → {pair.Value}")]);
    }

    /// <summary>
    /// Puts the string-enum converter on the enum types themselves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NSwag attaches <c>JsonStringEnumConverter</c> to each property whose type
    /// is an enum, but not to a property that is a <em>collection</em> of one —
    /// it leaves a <c>TODO(system.text.json)</c> there instead. Such a property
    /// then reads as a numeric enum, and the API sends names: deserialising
    /// <c>["customer"]</c> into <c>ICollection&lt;RequiredScopes&gt;</c> threw,
    /// and took the whole agent list with it.
    /// </para>
    /// <para>
    /// Annotating the enum declaration fixes every use at once — scalar,
    /// collection, dictionary value, nested — and keeps working when the next
    /// specification adds another. Found by a live call against tenant viu; six
    /// properties across two services were affected, of which one was reachable
    /// by a read.
    /// </para>
    /// </remarks>
    public static (string Source, IReadOnlyList<string> Annotated) AnnotateEnums(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<string> annotated = [];

        string result = EnumDeclaration().Replace(source, match =>
        {
            string name = match.Groups["name"].Value;

            // Idempotent: a second run must not stack attributes.
            if (match.Groups["attributes"].Value.Contains("JsonStringEnumConverter", StringComparison.Ordinal))
            {
                return match.Value;
            }

            annotated.Add(name);

            return match.Groups["attributes"].Value
                + "    [System.Text.Json.Serialization.JsonConverter("
                + $"typeof(System.Text.Json.Serialization.JsonStringEnumConverter<{name}>))]"
                + Environment.NewLine
                + match.Groups["declaration"].Value;
        });

        // The per-property TODO is now answered by the type annotation, and a
        // TODO that no longer describes anything is worse than none.
        result = result.Replace(
            "        // TODO(system.text.json): Add ItemConverterType with enum converter when supported"
            + Environment.NewLine,
            string.Empty,
            StringComparison.Ordinal);

        return (result, annotated);
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
    /// union to <em>whichever branch the specification lists first</em>, so the
    /// other shape fails to parse. Both directions have been seen against a live
    /// tenant: products came back as a map where the type said <c>string</c>,
    /// and a tax class came back as a string where the type said
    /// <c>IDictionary&lt;string, string&gt;</c>.
    /// </para>
    /// <para>
    /// Which is why both are matched here. Handling only <c>string</c> is what
    /// let <c>taxClass.name</c> ship broken: the specification was read
    /// correctly, the property was on the list, and the replacement quietly
    /// found nothing to replace. A property on the list that cannot be found is
    /// now reported rather than skipped.
    /// </para>
    /// <para>
    /// <see cref="T:Viu.Emporix.LocalizedString"/> reads both shapes, so the
    /// properties are pointed at it here. Which classes and properties are
    /// affected is read out of the specification during generation — a name
    /// like «Name» or «Description» is not evidence of anything.
    /// </para>
    /// </remarks>
    public static (string Source, IReadOnlyList<string> Retyped, IReadOnlyList<string> Missed)
        RetypeLocalizedProperties(
            string source,
            IReadOnlyCollection<string> localized)
        => RetypeProperties(source, localized, "Viu.Emporix.LocalizedString?", @"(?:string\??|System\.Collections\.Generic\.IDictionary<string,\s*string>\??)");

    /// <summary>
    /// Retypes the properties whose schema is a union of several object types.
    /// </summary>
    /// <remarks>
    /// The current type is whichever branch the specification listed first, so
    /// the pattern accepts any single identifier — there is nothing more
    /// specific to match on, and the property name is already anchored to its
    /// class.
    /// </remarks>
    public static (string Source, IReadOnlyList<string> Retyped, IReadOnlyList<string> Missed)
        RetypeUnionProperties(
            string source,
            IReadOnlyCollection<string> unions)
        => RetypeProperties(source, unions, "System.Text.Json.JsonElement?", @"[\w\.]+\??");

    private static (string Source, IReadOnlyList<string> Retyped, IReadOnlyList<string> Missed)
        RetypeProperties(
            string source,
            IReadOnlyCollection<string> localized,
            string replacement,
            string currentType)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(localized);

        List<string> retyped = [];
        List<string> missed = [];
        string result = source;

        foreach (string entry in localized)
        {
            string[] parts = entry.Split('.');
            if (parts.Length < 2)
            {
                continue;
            }

            // A path of three or more parts walks through the generated code
            // rather than guessing at it. «QuoteResponseItem.Zone.Name» means:
            // find QuoteResponseItem.Zone, read the type NSwag gave it — which
            // is «Zone2», a name no one could have predicted — and retype
            // Zone2.Name. Following the declarations is the only way to know
            // what the anonymous nested class ended up being called.
            string? className = parts[0];

            for (int i = 1; i < parts.Length - 1 && className is not null; i++)
            {
                className = DeclaredTypeOf(result, className, parts[i]);
            }

            if (className is null)
            {
                missed.Add(entry);
                continue;
            }

            string propertyName = parts[^1];

            // Anchored on the class so a property name shared by several classes
            // is only touched where the specification says it is localized.
            Regex declaration = new(
                $@"(?<class>public partial class {Regex.Escape(className)}\b(?:[^{{]*)\{{)(?<body>.*?)(?<property>public\s+){currentType}(?<tail>\s+{Regex.Escape(propertyName)}\s*\{{)",
                RegexOptions.Singleline);

            Match match = declaration.Match(result);
            if (!match.Success)
            {
                // The specification says this property is localized and the
                // generated code has no such property, or has it under another
                // type. Either way something moved, and silence here is how a
                // broken read ships.
                missed.Add(entry);
                continue;
            }

            result = declaration.Replace(
                result,
                m => m.Groups["class"].Value
                    + m.Groups["body"].Value
                    + m.Groups["property"].Value
                    + replacement
                    + m.Groups["tail"].Value,
                1);

            retyped.Add(entry);
        }

        return (result, retyped, missed);
    }

    /// <summary>
    /// Reads the type a generated property was given, stripped of nullability
    /// and of any collection wrapper.
    /// </summary>
    /// <remarks>
    /// The wrapper is stripped because a localized field inside an array's items
    /// lives on the item class: <c>ICollection&lt;Elements&gt;</c> means the
    /// next step of the path is <c>Elements</c>.
    /// </remarks>
    private static string? DeclaredTypeOf(string source, string className, string propertyName)
    {
        Match match = new Regex(
            $@"public partial class {Regex.Escape(className)}\b(?:[^{{]*)\{{"
            + $@".*?public\s+(?<type>[\w\.<>, ]+?)\??\s+{Regex.Escape(propertyName)}\s*\{{",
            RegexOptions.Singleline).Match(source);

        if (!match.Success)
        {
            return null;
        }

        string type = match.Groups["type"].Value.Trim();

        Match element = CollectionElement().Match(type);
        if (element.Success)
        {
            type = element.Groups[1].Value.Trim();
        }

        // A plain string or a map is the end of the road, not a class to walk
        // into: the path is wrong, or the generated shape moved.
        return type.Contains('.', StringComparison.Ordinal) || type == "string" ? null : type;
    }

    /// <summary>
    /// Renames generated types that differ only in letter case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shipping specification defines both <c>MetaData</c> and
    /// <c>Metadata</c> — different shapes, same name to anything
    /// case-insensitive. The JSON source generator derives one file per type
    /// from the type name, so the second collides with the first and the
    /// generator aborts with «hintName must be unique». It does not fail
    /// gracefully: every other serialization context in the assembly stops
    /// being generated too, and the resulting errors point everywhere except
    /// here.
    /// </para>
    /// <para>
    /// The later declaration is suffixed so both survive. Renaming rather than
    /// merging is deliberate: the two carry different fields, and a caller
    /// reading a <c>version</c> off the one that only has timestamps would get
    /// a silent null.
    /// </para>
    /// </remarks>
    public static (string Source, IReadOnlyList<string> Renamed) ResolveCaseInsensitiveCollisions(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<string> declared = [.. ClassDeclaration().Matches(source).Select(m => m.Groups[1].Value)];
        Dictionary<string, string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> renamed = [];
        string result = source;

        foreach (string name in declared)
        {
            if (!seen.TryGetValue(name, out string? existing))
            {
                seen[name] = name;
                continue;
            }

            // An exactly repeated name is not a collision: NSwag declares
            // ApiException twice, once generic and once not, and both belong.
            // Only a difference in case breaks the generator.
            if (string.Equals(existing, name, StringComparison.Ordinal))
            {
                continue;
            }

            // Suffixed rather than numbered: a reader seeing «MetadataCased»
            // can tell it was renamed, and by what rule.
            string replacement = name + "Cased";

            result = new Regex($@"\b{Regex.Escape(name)}\b").Replace(result, replacement);
            renamed.Add($"{name} → {replacement}");
            seen[replacement] = replacement;
        }

        return (result, renamed);
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

    [GeneratedRegex(@"^    public partial class (\w+)", RegexOptions.Multiline)]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex(@"^Anonymous\d*$")]
    private static partial Regex AnonymousType();

    /// <summary>A field whose type is the alias being dissolved.</summary>
    private static Regex PropertyDeclaration(string alias)
        => new($@"public {Regex.Escape(alias)}(\??) (\w+ \{{ get)", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>The alias as the sole type argument, for example in a collection.</summary>
    private static Regex GenericArgument(string alias)
        => new($@"<{Regex.Escape(alias)}>", RegexOptions.None, TimeSpan.FromSeconds(5));

    [GeneratedRegex(
        @"(?<attributes>(?:^[ \t]*(?:\[[^\]]*\]|///[^\n]*)\r?\n)*)" +
        @"(?<declaration>^[ \t]*public enum (?<name>\w+)\r?$)",
        RegexOptions.Multiline)]
    private static partial Regex EnumDeclaration();

    [GeneratedRegex(@"^(?:System\.Collections\.Generic\.)?I?(?:Collection|List|Enumerable)<(.+)>$")]
    private static partial Regex CollectionElement();
}
