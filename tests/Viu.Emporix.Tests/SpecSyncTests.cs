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

    // ---------- Localized and union properties ----------
    //
    // Every case below is one a live call found. Unit tests could not have:
    // the specification and the generated code agreed with each other, and both
    // disagreed with the API.

    [Fact]
    public void A_union_spelled_out_on_the_property_is_recognised()
    {
        // The tax service does this. Only the $ref form was handled at first,
        // so taxClass.name shipped typed as a map and reading a tax
        // configuration threw on a tenant that stores a plain string.
        const string yaml = """
            components:
              schemas:
                taxClass:
                  type: object
                  properties:
                    name:
                      description: |-
                        A long description that pushes the union well past any
                        fixed look-ahead window.

                        Another paragraph, for good measure.
                      oneOf:
                        - type: object
                          additionalProperties:
                            type: string
                        - type: string
            """;

        Assert.Contains("TaxClass.Name", LocalizedProperties.Read(yaml));
    }

    [Fact]
    public void A_union_nested_deeper_is_not_attributed_to_the_property_above_it()
    {
        // A oneOf three levels down inside an array's items belongs to the
        // property it is on, not to the outermost one. Attributing it upwards
        // flagged «AggregateFee.Elements» as localized text.
        const string yaml = """
            components:
              schemas:
                aggregateFee:
                  type: object
                  properties:
                    elements:
                      type: array
                      items:
                        type: object
                        properties:
                          name:
                            oneOf:
                              - type: string
                              - type: object
                                additionalProperties:
                                  type: string
            """;

        IReadOnlyCollection<string> found = LocalizedProperties.Read(yaml);

        Assert.Contains("AggregateFee.Elements.Name", found);
        Assert.DoesNotContain("AggregateFee.Elements", found);
    }

    [Fact]
    public void A_path_operation_is_not_mistaken_for_a_schema()
    {
        // «post:» sits at a schema's indentation, and its requestBody has a
        // «content» property whose media type holds a schema — which is how
        // «Post.Content» reached the list of localized properties.
        const string yaml = """
            paths:
              /thing:
                post:
                  requestBody:
                    content:
                      application/json:
                        schema:
                          oneOf:
                            - type: string
                            - type: object
                              additionalProperties:
                                type: string
            """;

        Assert.DoesNotContain("Post.Content", LocalizedProperties.Read(yaml));
    }

    [Fact]
    public void A_union_of_named_object_types_is_read_separately()
    {
        // Not a localized value: three provider shapes with no discriminator.
        const string yaml = """
            components:
              schemas:
                agent:
                  type: object
                  properties:
                    llmConfig:
                      oneOf:
                        - $ref: '#/components/schemas/EmporixLlm'
                        - $ref: '#/components/schemas/ApiKeyLlm'
                        - $ref: '#/components/schemas/SelfHostedLlm'
            """;

        Assert.Contains("Agent.LlmConfig", LocalizedProperties.ReadUnions(yaml));
        Assert.Empty(LocalizedProperties.Read(yaml));
    }

    [Fact]
    public void A_localized_property_is_retyped_from_either_branch()
    {
        // NSwag picks whichever branch the specification listed first, and both
        // have been seen: products came back as a map typed as string, a tax
        // class as a string typed as a map.
        const string source = """
                public partial class Product
                {
                    public string? Name { get; set; }
                }

                public partial class TaxClass
                {
                    public System.Collections.Generic.IDictionary<string, string> Name { get; set; }
                }
            """;

        (string result, IReadOnlyList<string> retyped, IReadOnlyList<string> missed) =
            GeneratedCodeFixer.RetypeLocalizedProperties(source, ["Product.Name", "TaxClass.Name"]);

        Assert.Equal(2, retyped.Count);
        Assert.Empty(missed);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(
            result, @"Viu\.Emporix\.LocalizedString\? Name"));
    }

    [Fact]
    public void A_localized_property_that_cannot_be_found_is_reported()
    {
        // Previously a silent no-op, which is how taxClass.name shipped broken:
        // the specification was read correctly and the replacement found
        // nothing to replace.
        const string source = "public partial class Product { public int Age { get; set; } }";

        (_, IReadOnlyList<string> retyped, IReadOnlyList<string> missed) =
            GeneratedCodeFixer.RetypeLocalizedProperties(source, ["Product.Name"]);

        Assert.Empty(retyped);
        Assert.Equal(["Product.Name"], missed);
    }

    [Fact]
    public void A_nested_path_is_resolved_through_the_generated_code()
    {
        // «QuoteResponseItem.Zone.Name» has to become «Zone2.Name», and only the
        // generated code knows the nested class ended up called Zone2.
        const string source = """
                public partial class QuoteResponseItem
                {
                    public Zone2? Zone { get; set; }
                }

                public partial class Zone2
                {
                    public string? Id { get; set; }
                    public string? Name { get; set; }
                }
            """;

        (string result, IReadOnlyList<string> retyped, _) =
            GeneratedCodeFixer.RetypeLocalizedProperties(source, ["QuoteResponseItem.Zone.Name"]);

        Assert.Equal(["QuoteResponseItem.Zone.Name"], retyped);
        Assert.Contains("Viu.Emporix.LocalizedString? Name", result, StringComparison.Ordinal);
        Assert.Contains("string? Id", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_collection_wrapper_is_stepped_through_on_the_way_down()
    {
        // «AggregateFee.Elements.Name»: Elements is ICollection<Elements>, and
        // the localized field is on the item class.
        const string source = """
                public partial class AggregateFee
                {
                    public System.Collections.Generic.ICollection<Elements>? Elements { get; set; }
                }

                public partial class Elements
                {
                    public string? Name { get; set; }
                }
            """;

        (string result, IReadOnlyList<string> retyped, _) =
            GeneratedCodeFixer.RetypeLocalizedProperties(source, ["AggregateFee.Elements.Name"]);

        Assert.Single(retyped);
        Assert.Contains("Viu.Emporix.LocalizedString? Name", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_union_property_becomes_raw_json()
    {
        const string source = """
                public partial class AgentResponse
                {
                    public EmporixLlm? LlmConfig { get; set; }
                }
            """;

        (string result, IReadOnlyList<string> retyped, _) =
            GeneratedCodeFixer.RetypeUnionProperties(source, ["AgentResponse.LlmConfig"]);

        Assert.Single(retyped);
        Assert.Contains("System.Text.Json.JsonElement? LlmConfig", result, StringComparison.Ordinal);
    }

    // ---------- Enum serialization ----------

    [Fact]
    public void Enums_are_annotated_for_string_serialization()
    {
        // NSwag annotates a property whose type is an enum but not one that is a
        // collection of enums — it leaves a TODO. Deserialising ["customer"]
        // into ICollection<RequiredScopes> threw and took the whole agent list
        // with it.
        const string source = """
            namespace X
            {
                [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.7.1.0")]
                public enum RequiredScopes
                {
                    Anonymous = 0,
                }
            }
            """;

        (string result, IReadOnlyList<string> annotated) = GeneratedCodeFixer.AnnotateEnums(source);

        Assert.Equal(["RequiredScopes"], annotated);
        Assert.Contains(
            "JsonStringEnumConverter<RequiredScopes>",
            result,
            StringComparison.Ordinal);

        // The attribute has to land above the declaration, not above the
        // [GeneratedCode] attribute that documents where the type came from.
        Assert.True(
            result.IndexOf("GeneratedCode", StringComparison.Ordinal)
            < result.IndexOf("JsonStringEnumConverter", StringComparison.Ordinal));
    }

    [Fact]
    public void Annotating_enums_twice_changes_nothing_the_second_time()
    {
        // Every repair in this pipeline has to be repeatable: the sync runs on a
        // schedule and stacked attributes would not compile.
        const string source = "    public enum Colour\n    {\n        Red = 0,\n    }\n";

        (string once, _) = GeneratedCodeFixer.AnnotateEnums(source);
        (string twice, IReadOnlyList<string> again) = GeneratedCodeFixer.AnnotateEnums(once);

        Assert.Equal(once, twice);
        Assert.Empty(again);
    }

    [Fact]
    public void The_stale_element_converter_note_is_removed()
    {
        const string source =
            "        // TODO(system.text.json): Add ItemConverterType with enum converter when supported\n"
            + "        public System.Collections.Generic.ICollection<X>? Y { get; set; }\n";

        (string result, _) = GeneratedCodeFixer.AnnotateEnums(source);

        Assert.DoesNotContain("TODO(system.text.json)", result, StringComparison.Ordinal);
        Assert.Contains("ICollection<X>? Y", result, StringComparison.Ordinal);
    }
}
