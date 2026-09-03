using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Viu.Emporix.Mixins;
using Viu.Emporix.MixinSync;

namespace Viu.Emporix.Tests;

/// <summary>
/// Everything the generator emits has to compile.
/// </summary>
/// <remarks>
/// <para>
/// The structural counterpart to <see cref="SpecPathTests"/>. Not one defect in
/// this SDK has been found by a unit test asserting an expectation, so this
/// asserts a property instead: feed the generator schemas shaped like the ones
/// that break it, and compile the result. Two collisions are known and each has
/// a fixture; the reason to compile rather than to assert on the text is the
/// third one nobody has thought of yet.
/// </para>
/// <para>
/// <b>What this does not cover.</b> The generated registry references
/// <c>&lt;Mixin&gt;Context.Default.&lt;Type&gt;</c>, a member the
/// <c>System.Text.Json</c> source generator produces — and that generator does
/// not run inside this compilation, so referencing it here would fail with
/// <c>CS0117</c> for a reason that has nothing to do with the generator under
/// test. The same holds for each mixin's context file, which is why the
/// generator writes it separately. Both are therefore excluded, and what proves
/// they compile is
/// building a consumer project against a real generated tree, which is a step in
/// the release checklist rather than a test.
/// </para>
/// </remarks>
public class MixinGeneratorCompilationTests
{
    [Fact]
    public void Two_mixins_with_a_same_named_nested_object_compile()
    {
        // Without one namespace per mixin, two schemas each declaring a note
        // object emit two «partial class Note» into one namespace. Identical
        // members then give CS0102; differing ones are worse — the halves merge
        // silently into a type carrying both mixins' fields, and it compiles.
        AssertCompiles(
        [
            Mixin("delivery", 6, """
                {"type":"object","additionalProperties":false,"properties":{
                  "note":{"type":"object","additionalProperties":false,"properties":{"en":{"type":"string"}}}}}
                """),
            Mixin("warranty", 2, """
                {"type":"object","additionalProperties":false,"properties":{
                  "note":{"type":"object","additionalProperties":false,"properties":{"en":{"type":"string"}}}}}
                """),
        ]);
    }

    [Fact]
    public void Every_attribute_shape_emporix_supports_compiles()
    {
        AssertCompiles(
        [
            Mixin("everything", 1, """
                {"type":"object","additionalProperties":false,"properties":{
                  "text":{"type":"string"},
                  "count":{"type":"integer"},
                  "weight":{"type":"number"},
                  "active":{"type":"boolean"},
                  "packaging":{"type":"string","enum":["Paper","Plastic","None"]},
                  "tags":{"type":"array","items":{"type":"string"}},
                  "at":{"type":"string","format":"date-time"},
                  "note":{"type":"object","additionalProperties":false,"properties":{"en":{"type":"string"},"de":{"type":"string"}}}}}
                """),
        ]);
    }

    [Fact]
    public void A_schema_id_that_cannot_start_a_csharp_name_compiles()
    {
        AssertCompiles(
        [
            Mixin("68e27d7a68ce91215abc0f23", 1,
                """{"type":"object","additionalProperties":false,"properties":{"a":{"type":"string"}}}"""),
        ]);
    }

    [Fact]
    public void An_attribute_named_like_its_own_type_compiles()
    {
        // NJsonSchema emits a nested class per object property, so an attribute
        // whose name matches the mixin's own type name is a plausible clash.
        AssertCompiles(
        [
            Mixin("delivery", 4, """
                {"type":"object","additionalProperties":false,"properties":{
                  "deliveryMixinV4":{"type":"object","additionalProperties":false,"properties":{"a":{"type":"string"}}}}}
                """),
        ]);
    }

    [Fact]
    public void The_compilation_check_is_sharp_enough_to_see_a_collision()
    {
        // Guards the guard. If the reference set were incomplete or the
        // diagnostics filtered too loosely, every test above would pass
        // vacuously — so this feeds the compiler the very clash that one
        // namespace per mixin prevents, and requires it to be reported.
        //
        // Both halves declare the same member on purpose. NJsonSchema emits
        // «partial» classes, so halves with differing members merge without a
        // diagnostic; only a repeated member is an error. That is the sharper
        // reason for the namespace split: the failure mode is silence.
        Diagnostic[] fatal = Compile(
        [
            """
            namespace Acme.Mixins
            {
                public partial class Note { public string? En { get; set; } }
            }
            """,
            """
            namespace Acme.Mixins
            {
                public partial class Note { public string? En { get; set; } }
            }
            """,
        ]);

        Assert.Contains(fatal, d => string.Equals(d.Id, "CS0102", StringComparison.Ordinal));
    }

    private static RawMixin Mixin(string key, int version, string schema) => new()
    {
        Key = key,
        Entity = "PRODUCT",
        Version = version,
        Url = $"https://cdn.emporix.io/{key}.v{version}.json",
        Schema = schema,
    };

    private static void AssertCompiles(IEnumerable<RawMixin> mixins)
    {
        IReadOnlyDictionary<string, string> files = Generator.Generate(mixins, "Acme.Mixins");

        IEnumerable<string> sources = files
            .Where(file => !file.Key.EndsWith(".Context.g.cs", StringComparison.Ordinal)
                && !string.Equals(file.Key, "Registry.g.cs", StringComparison.Ordinal))
            .Select(file => file.Value);

        Diagnostic[] fatal = Compile(sources);

        Assert.True(
            fatal.Length == 0,
            $"The generated code does not compile:{Environment.NewLine}"
            + string.Join(Environment.NewLine, fatal.Select(d => $"  {d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")));
    }

    private static Diagnostic[] Compile(IEnumerable<string> sources)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratedMixins",
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            ReferenceAssemblies(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();

        return [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];
    }

    private static IEnumerable<MetadataReference> ReferenceAssemblies()
    {
        // Every assembly the running runtime offers, rather than a hand-kept
        // list. A generated enum reaches for System.Runtime.Serialization, a
        // date for System.Runtime, an array for System.Collections — and the
        // next schema construct would reach for something else again. Keeping a
        // list current is exactly the kind of maintenance this test exists to
        // avoid. Assembly.Location would be the obvious route to the directory
        // but trips IL3000, which is an error here.
        foreach (string path in Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            MetadataReference? reference = null;

            try
            {
                reference = MetadataReference.CreateFromFile(path);
            }
            catch (BadImageFormatException)
            {
                // Native assets sit in the same directory; skip them.
            }

            if (reference is not null)
            {
                yield return reference;
            }
        }

        // The SDK assembly sits next to the test assembly in the output.
        yield return MetadataReference.CreateFromFile(
            Path.Combine(AppContext.BaseDirectory, "Viu.Emporix.dll"));
    }
}
