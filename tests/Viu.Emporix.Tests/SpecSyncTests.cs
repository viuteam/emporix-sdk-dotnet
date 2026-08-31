using Viu.Emporix.SpecSync;

namespace Viu.Emporix.Tests;

/// <summary>
/// Checks the generation pipeline. The repairs are the delicate part: one that
/// reaches too far damages hundreds of types at once.
/// </summary>
public class SpecSyncTests
{
    // ---------- Specification repairs ----------

    [Fact]
    public void Every_service_specific_patch_names_a_known_service()
    {
        // An entry for a service that does not exist would never run and would
        // quietly do nothing.
        HashSet<string> known = [.. SpecCatalog.All.Select(spec => spec.Name)];

        Assert.All(SpecPatches.ByService.Keys, name => Assert.Contains(name, known));
    }

    [Fact]
    public void Whitespace_only_lines_become_truly_empty()
    {
        // The trigger: a line of nothing but spaces right after »|«.
        const string yaml = "description: |\n        \n        Ein Text.\n";

        PatchOutcome outcome = SpecPatches.Apply("anything", yaml);

        Assert.Equal("description: |\n\n        Ein Text.\n", outcome.Yaml);
    }

    [Fact]
    public void Regex_escapes_in_patterns_are_left_alone()
    {
        // The dangerous case: \d and \s appear in search patterns and must not be
        // treated like the YAML escapes \_ and \L.
        const string yaml = "pattern: \"^[\\\\d]+$\"\nother: \"a\\\\sb\"\n";

        PatchOutcome outcome = SpecPatches.Apply("anything", yaml);

        Assert.Contains(@"\d", outcome.Yaml, StringComparison.Ordinal);
        Assert.Contains(@"\s", outcome.Yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Yaml_only_escapes_are_replaced()
    {
        const string yaml = "description: \"a\\_b\\L\"\n";

        PatchOutcome outcome = SpecPatches.Apply("anything", yaml);

        Assert.Equal("description: \"a b\"\n", outcome.Yaml);
    }

    [Fact]
    public void A_patch_that_changes_nothing_is_reported_as_stale()
    {
        // The report is the only sign that Emporix fixed a defect and the repair
        // can go.
        PatchOutcome outcome = SpecPatches.Apply("approval-service", "harmless content\n");

        Assert.NotEmpty(outcome.Stale);
        Assert.Empty(outcome.Applied);
    }

    [Fact]
    public void Patches_are_repeatable()
    {
        const string yaml = "description: |\n    \n    Text mit a\\_b\n";

        PatchOutcome once = SpecPatches.Apply("anything", yaml);
        PatchOutcome twice = SpecPatches.Apply("anything", once.Yaml);

        Assert.Equal(once.Yaml, twice.Yaml);
    }

    // ---------- Specification state ----------

    [Fact]
    public void The_digest_reacts_to_any_change()
    {
        Assert.NotEqual(SyncManifest.Hash("a"), SyncManifest.Hash("b"));
        Assert.Equal(SyncManifest.Hash("a"), SyncManifest.Hash("a"));
    }

    [Theory]
    [InlineData("info:\n  version: 1.2.3\n", "1.2.3")]
    [InlineData("info:\n  version: '4.5'\n", "4.5")]
    [InlineData("info:\n  version:\n", "")]
    [InlineData("kein info-Block\n", "")]
    public void The_version_is_read_from_the_info_block(string yaml, string expected)
    {
        Assert.Equal(expected, SyncManifest.ReadSpecVersion(yaml));
    }

    [Fact]
    public void Only_changed_digests_are_reported()
    {
        SyncManifest before = Manifest(("a", "1"), ("b", "2"));
        SyncManifest after = Manifest(("a", "1"), ("b", "GEÄNDERT"), ("c", "3"));

        Assert.Equal(["b", "c"], SyncManifest.Diff(before, after));
    }

    [Fact]
    public void A_first_run_reports_nothing_as_changed()
    {
        Assert.Empty(SyncManifest.Diff(null, Manifest(("a", "1"))));
    }

    // ---------- Post-processing the generated code ----------

    [Fact]
    public void An_empty_class_deriving_from_string_is_dissolved()
    {
        string source = Wrap("""
                [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "x")]
                public partial class Name : string
                {

                }

                public partial class Catalog
                {
                    public Name? Name { get; set; } = default!;
                }
            """);

        (string result, IReadOnlyList<string> resolved) =
            GeneratedCodeFixer.ResolveEmptyAliasClasses(source);

        Assert.Contains("Name → string", resolved);
        Assert.DoesNotContain("class Name : string", result, StringComparison.Ordinal);
        Assert.Contains("public string? Name { get; set; }", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_class_with_members_is_never_dissolved()
    {
        // The expensive mistake would be taking a class with content for a mere
        // alias.
        string source = Wrap("""
                [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "x")]
                public partial class Price : BasePrice
                {
                    public string? Currency { get; set; } = default!;
                }
            """);

        (string result, IReadOnlyList<string> resolved) =
            GeneratedCodeFixer.ResolveEmptyAliasClasses(source);

        Assert.Empty(resolved);
        Assert.Contains("class Price : BasePrice", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_type_is_never_replaced_by_raw_json()
    {
        // This is exactly where an earlier version went wrong: it replaced
        // hundreds of valid types because it failed to find their declaration.
        string source = Wrap("""
                public partial class CalculatedPrice
                {
                    public string? Currency { get; set; } = default!;
                }

                public partial class Cart
                {
                    public CalculatedPrice? CalculatedPrice { get; set; } = default!;
                }
            """);

        (string result, IReadOnlyList<string> replaced) =
            GeneratedCodeFixer.ResolveDanglingTypeReferences(source);

        Assert.Empty(replaced);
        Assert.Contains("public CalculatedPrice? CalculatedPrice", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_to_a_missing_type_becomes_raw_json()
    {
        string source = Wrap("""
                public partial class Config
                {
                    public Token? Token { get; set; } = default!;
                }
            """);

        (string result, IReadOnlyList<string> replaced) =
            GeneratedCodeFixer.ResolveDanglingTypeReferences(source);

        Assert.Equal(["Token"], replaced);
        Assert.Contains("System.Text.Json.JsonElement? Token", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_generic_type_parameter_is_never_replaced()
    {
        string source = Wrap("""
                public partial class Wrapper<TResult>
                {
                    public TResult? Result { get; set; } = default!;
                }
            """);

        (string result, IReadOnlyList<string> replaced) =
            GeneratedCodeFixer.ResolveDanglingTypeReferences(source);

        Assert.Empty(replaced);
        Assert.Contains("public TResult? Result", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_type_inside_a_collection_becomes_raw_json()
    {
        string source = Wrap("""
                public partial class Agent
                {
                    public System.Collections.Generic.ICollection<Conditions> Conditions { get; set; } = default!;
                }
            """);

        (string result, IReadOnlyList<string> replaced) =
            GeneratedCodeFixer.ResolveDanglingTypeReferences(source);

        Assert.Equal(["Conditions"], replaced);
        Assert.Contains("ICollection<System.Text.Json.JsonElement>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_type_inside_a_collection_is_left_alone()
    {
        string source = Wrap("""
                public partial class Item
                {
                    public string? Code { get; set; } = default!;
                }

                public partial class Cart
                {
                    public System.Collections.Generic.ICollection<Item> Items { get; set; } = default!;
                }
            """);

        (string _, IReadOnlyList<string> replaced) =
            GeneratedCodeFixer.ResolveDanglingTypeReferences(source);

        Assert.Empty(replaced);
    }

    private static string Wrap(string body) => $"namespace X;\n\n{body}\n";

    private static SyncManifest Manifest(params (string Name, string Digest)[] services)
        => new()
        {
            GeneratedAt = DateTimeOffset.UnixEpoch,
            Services = new SortedDictionary<string, SpecManifestEntry>(
                services.ToDictionary(
                    service => service.Name,
                    service => new SpecManifestEntry
                    {
                        Url = "https://example.test",
                        SpecVersion = string.Empty,
                        FetchedAt = DateTimeOffset.UnixEpoch,
                        Sha256 = service.Digest,
                    },
                    StringComparer.Ordinal),
                StringComparer.Ordinal),
        };
}
