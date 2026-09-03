# Typed Emporix Mixins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give consumers typed read/write access to Emporix mixins, a type-safe `q` filter over mixin attributes, and a CLI that generates the types from a tenant's Schema Service and detects schema drift.

**Architecture:** Two artefacts on one version line. `Viu.Emporix` gains a `Mixins` namespace whose descriptor carries a `JsonTypeInfo<T>` supplied by the consumer's own serializer context — that is what keeps the whole path reflection-free and AOT-safe. `Viu.Emporix.MixinSync` is a `dotnet tool` that reads the Schema Service, generates one namespace and one serializer context per mixin, and maintains a lockfile so a raised schema version surfaces as a pull request.

**Tech Stack:** .NET 10, C# 14, `System.Text.Json` source generation, `System.Linq.Expressions` for property selectors, xunit, `NJsonSchema.CodeGeneration.CSharp` 11.6.1 as a library, `Microsoft.CodeAnalysis.CSharp` for the structural test.

**Spec:** [`docs/superpowers/specs/2026-09-03-mixin-codegen-design.md`](../specs/2026-09-03-mixin-codegen-design.md)

## Global Constraints

- **Never invent endpoints, fields or scopes.** Verify against `specs/`, the Node SDK at `../emporix-sdk`, or the Emporix documentation MCP connector. This is the rule that matters most in this repository.
- `TreatWarningsAsErrors` is on for every project. A warning fails the build.
- `IsAotCompatible=true` for `Viu.Emporix` and the tests. **Only** `Viu.Emporix.MixinSync` opts out, in its own csproj, the way `tools/Viu.Emporix.SpecSync` does.
- **No reflection** in `Viu.Emporix`. No `.Compile()` on an expression tree, no runtime type resolution. ADR-0004.
- **One `JsonSerializerContext` per mixin, without exception.** A shared context collides on same-named nested types with `SYSLIB1031`, which is an error here.
- Zero runtime dependencies for `Viu.Emporix`. New package references go only into `Viu.Emporix.MixinSync`.
- Package versions live in `Directory.Packages.props` — `ManagePackageVersionsCentrally` is on, so a `PackageReference` carries no `Version`.
- After any public API change in `Viu.Emporix`, run `./scripts/update-public-api.sh` or the build fails on `RS0016`.
- Two public overloads that both have optional parameters trigger `RS0026`. Rename one rather than suppress it.
- Code, comments, documentation and commit messages in **English**. Comments explain why, not what.
- **No nested parentheses in a commit body.** Release Please silently drops a commit it cannot parse. A code fence does not protect them.
- Commit subjects: `feat` for new API, `fix` for defects, `test`/`chore`/`ci` for the rest. Only `feat` and `fix` reach the changelog.
- Work on the existing branch `docs/mixin-codegen-design`, which is open as PR #9.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/Viu.Emporix/Mixins/MixinDescriptor.cs` | The descriptor: key, entity, url, version, `JsonTypeInfo<T>`, attribute-name table |
| `src/Viu.Emporix/Mixins/MixinReader.cs` | Typed read off a mixin container; version parsing from the schema URL |
| `src/Viu.Emporix/Mixins/MixinWriter.cs` | Collects values and schema URLs for assignment onto an entity |
| `src/Viu.Emporix/Mixins/MixinConditions.cs` | The four condition categories and the `Is` factory |
| `src/Viu.Emporix/Mixins/MixinFilter.cs` | Plain and compound filters, `EmporixQuery` capability values, the gate |
| `src/Viu.Emporix/Mixins/MixinQuery.cs` | The builder: selector to attribute path, clause assembly |
| `src/Viu.Emporix.MixinSync/Viu.Emporix.MixinSync.csproj` | Tool packaging, AOT opt-out, NJsonSchema reference |
| `src/Viu.Emporix.MixinSync/Program.cs` | `pull` / `generate` / `check` dispatch, config loading |
| `src/Viu.Emporix.MixinSync/MixinConfig.cs` | `emporix-mixins.json` shape and its serializer context |
| `src/Viu.Emporix.MixinSync/RawMixin.cs` | The normalized mixin plus the snapshot file shape |
| `src/Viu.Emporix.MixinSync/SchemaSource.cs` | Schema Service to `RawMixin[]`, with the URL fetch |
| `src/Viu.Emporix.MixinSync/AttributeSchema.cs` | `attributes[]` to JSON Schema, the fallback path |
| `src/Viu.Emporix.MixinSync/Generator.cs` | NJsonSchema to types, contexts, registry; collision detection |
| `src/Viu.Emporix.MixinSync/Lockfile.cs` | Lockfile shape, build and diff |
| `src/Viu.Emporix/SchemaService.cs` | **Modified**: the schema listing sends no paging parameters today, see Task 9 |
| `tests/Viu.Emporix.Tests/MixinRuntimeTests.cs` | Reader, writer, version parsing |
| `tests/Viu.Emporix.Tests/MixinQueryTests.cs` | Clause rendering, the capability gate, the whitespace guard |
| `tests/Viu.Emporix.Tests/MixinSyncTests.cs` | Lockfile diff, attribute conversion, collision detection |
| `tests/Viu.Emporix.Tests/MixinGeneratorCompilationTests.cs` | The structural test: generated code must compile |

Files that change together live together, so the filter's three files stay separate from the runtime's three: a reviewer can reject the filter design without touching the reader.

**Build order is not negotiable.** Phase 2 reads `Attributes` off the descriptor from Phase 1, and Phase 3 generates code against both. Phases 1 and 2 together are already shippable — a consumer can write descriptors by hand — so stopping after Phase 2 leaves working software.

---

## Phase 1 — Runtime

### Task 1: Descriptor and reader

**Files:**
- Create: `src/Viu.Emporix/Mixins/MixinDescriptor.cs`
- Create: `src/Viu.Emporix/Mixins/MixinReader.cs`
- Test: `tests/Viu.Emporix.Tests/MixinRuntimeTests.cs`
- Modify: `tests/Viu.Emporix.Tests/TestJson.cs`
- Modify: `src/Viu.Emporix/PublicAPI.Unshipped.txt` via script

**Interfaces:**
- Consumes: nothing.
- Produces: `MixinDescriptor<T>` with required init properties `Key` `string`, `Entity` `string`, `Url` `string`, `Version` `int`, `TypeInfo` `JsonTypeInfo<T>`, `Attributes` `IReadOnlyDictionary<string, string>`. `MixinReader.Read<T>(object? mixins, MixinDescriptor<T> descriptor)` returning `T?`. `MixinReader.SavedVersion(IDictionary<string, string>? metadataMixins, string key)` and `MixinReader.SavedVersion(object? metadataMixins, string key)`, both returning `int?`.

- [ ] **Step 1: Add the test mixin type and register it**

Append to `tests/Viu.Emporix.Tests/TestJson.cs`, and add the three `JsonSerializable` lines to the existing `TestJsonContext`:

```csharp
/// <summary>A stand-in for a generated mixin type.</summary>
/// <remarks>
/// Shaped like what the generator emits: explicit <c>JsonPropertyName</c> on
/// every attribute, all optional, one nested object for a localized field.
/// </remarks>
internal sealed class TestDeliveryMixin
{
    [JsonPropertyName("packaging")]
    public string? Packaging { get; set; }

    [JsonPropertyName("weight")]
    public double? Weight { get; set; }

    [JsonPropertyName("note")]
    public TestLocalizedNote? Note { get; set; }
}

/// <summary>The nested object of a localized mixin attribute.</summary>
internal sealed class TestLocalizedNote
{
    [JsonPropertyName("en")]
    public string? En { get; set; }
}
```

Add to `TestJsonContext`, and add `using System.Text.Json;` to the file if it is not there — the tests read raw `JsonElement`, so it must be registered:

```csharp
[JsonSerializable(typeof(TestDeliveryMixin))]
[JsonSerializable(typeof(TestLocalizedNote))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Viu.Emporix.Tests/MixinRuntimeTests.cs`:

```csharp
using System.Text.Json;
using Viu.Emporix.Mixins;

namespace Viu.Emporix.Tests;

/// <summary>
/// Typed access to a tenant's mixins.
/// </summary>
/// <remarks>
/// The descriptor carries a <c>JsonTypeInfo</c> because a generated serializer
/// context has no resolver for an arbitrary runtime type: assigning a POCO to an
/// <c>object?</c> mixin property throws <c>NotSupportedException</c>, and it does
/// so with reflection enabled too. Going through the consumer's own type
/// information is the only path that works, not merely the AOT-safe one.
/// </remarks>
public class MixinRuntimeTests
{
    private static MixinDescriptor<TestDeliveryMixin> Delivery => new()
    {
        Key = "deliveryOptions",
        Entity = "PRODUCT",
        Url = "https://cdn.emporix.io/deliveryOptionsMixIn.v6.json",
        Version = 6,
        TypeInfo = TestJsonContext.Default.TestDeliveryMixin,
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Packaging"] = "packaging",
            ["Weight"] = "weight",
            ["Note"] = "note",
        },
    };

    [Fact]
    public void A_mixin_is_read_from_the_container()
    {
        JsonElement mixins = JsonSerializer.Deserialize(
            """{"deliveryOptions":{"packaging":"Paper","weight":2.5}}""",
            TestJsonContext.Default.JsonElement);

        TestDeliveryMixin? value = MixinReader.Read(mixins, Delivery);

        Assert.Equal("Paper", value?.Packaging);
        Assert.Equal(2.5, value?.Weight);
    }

    [Fact]
    public void An_absent_mixin_reads_as_null_rather_than_throwing()
    {
        JsonElement mixins = JsonSerializer.Deserialize(
            """{"somethingElse":{}}""", TestJsonContext.Default.JsonElement);

        Assert.Null(MixinReader.Read(mixins, Delivery));
    }

    [Fact]
    public void A_container_that_is_not_an_object_reads_as_null()
    {
        // An entity that carries no mixins at all leaves the property null, and
        // some services send an empty string instead of an object.
        Assert.Null(MixinReader.Read(null, Delivery));
        Assert.Null(MixinReader.Read(
            JsonSerializer.Deserialize("\"\"", TestJsonContext.Default.JsonElement), Delivery));
    }

    [Fact]
    public void The_saved_version_is_parsed_from_the_schema_url()
    {
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["deliveryOptions"] = "https://cdn.emporix.io/deliveryOptionsMixIn.v6.json",
        };

        Assert.Equal(6, MixinReader.SavedVersion(metadata, "deliveryOptions"));
    }

    [Fact]
    public void A_url_without_a_version_marker_yields_no_version()
    {
        Dictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["deliveryOptions"] = "https://cdn.emporix.io/deliveryOptions.json",
        };

        Assert.Null(MixinReader.SavedVersion(metadata, "deliveryOptions"));
        Assert.Null(MixinReader.SavedVersion(metadata, "absentKey"));
        Assert.Null(MixinReader.SavedVersion((IDictionary<string, string>?)null, "deliveryOptions"));
    }

    [Fact]
    public void The_saved_version_is_also_read_from_an_object_typed_metadata_property()
    {
        // Fourteen specifications type metadata.mixins as IDictionary<string,
        // string>; the other hundred-odd type the very same concept as object,
        // which deserializes to a JsonElement. Both must work.
        JsonElement metadata = JsonSerializer.Deserialize(
            """{"deliveryOptions":"https://cdn.emporix.io/deliveryOptionsMixIn.v7.json"}""",
            TestJsonContext.Default.JsonElement);

        Assert.Equal(7, MixinReader.SavedVersion(metadata, "deliveryOptions"));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinRuntimeTests"`
Expected: compile failure — `MixinDescriptor` and `MixinReader` do not exist.

- [ ] **Step 4: Write the descriptor**

Create `src/Viu.Emporix/Mixins/MixinDescriptor.cs`:

```csharp
using System.Text.Json.Serialization.Metadata;

namespace Viu.Emporix.Mixins;

/// <summary>
/// One of a tenant's mixins: where it hangs, which schema describes it, and how
/// to serialize it.
/// </summary>
/// <remarks>
/// <para>
/// Produced by <c>Viu.Emporix.MixinSync</c> into the consumer's repository, one
/// per mixin. Writing one by hand is supported and is the way to use a mixin
/// without adopting the generator.
/// </para>
/// <para>
/// <see cref="TypeInfo"/> is the reason this type exists rather than a plain
/// string key. A generated <c>JsonSerializerContext</c> resolves no arbitrary
/// runtime type, so serializing a mixin value requires the consumer's own type
/// information — the SDK never resolves it. That keeps the path reflection-free,
/// as ADR-0004 requires.
/// </para>
/// </remarks>
/// <typeparam name="T">The generated type for this mixin's schema.</typeparam>
public sealed class MixinDescriptor<T>
{
    /// <summary>The key the value sits under in <c>entity.mixins</c>.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// The entity type the schema is assigned to, for example <c>PRODUCT</c>.
    /// </summary>
    /// <remarks>
    /// Informational: it makes the generated registry readable and lets an error
    /// name where a mixin belongs. It deliberately does not decide whether a
    /// query may use <c>compoundLogicalQuery</c> — that capability belongs to the
    /// service being called, not to the entity. A schema assigned to several
    /// entity types yields one descriptor each.
    /// </remarks>
    public required string Entity { get; init; }

    /// <summary>
    /// The hosted schema URL, written to <c>entity.metadata.mixins[key]</c>.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>The schema version Emporix assigned.</summary>
    public required int Version { get; init; }

    /// <summary>Serialization metadata from the consumer's own context.</summary>
    public required JsonTypeInfo<T> TypeInfo { get; init; }

    /// <summary>
    /// CLR property name to JSON attribute name.
    /// </summary>
    /// <remarks>
    /// Lets the query builder turn a property selector into an attribute path
    /// without reading any metadata reflectively. The generator parses this out
    /// of the code it emitted rather than recomputing the names, because the
    /// conversion and the emitted result can diverge.
    /// </remarks>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
}
```

- [ ] **Step 5: Write the reader**

Create `src/Viu.Emporix/Mixins/MixinReader.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace Viu.Emporix.Mixins;

/// <summary>
/// Reads typed mixin values off an entity.
/// </summary>
/// <remarks>
/// Takes the mixin container rather than the entity. C# is nominally typed, the
/// generated entity classes share no interface, and the same concept is modelled
/// two ways across the specifications — so handing in <c>product.Mixins</c> is
/// what works everywhere without changing generated code.
/// </remarks>
public static class MixinReader
{
    /// <summary>Reads one mixin, or <c>null</c> when it is absent.</summary>
    /// <param name="mixins">The entity's <c>mixins</c> property.</param>
    /// <param name="descriptor">Which mixin to read.</param>
    /// <typeparam name="T">The mixin's generated type.</typeparam>
    public static T? Read<T>(object? mixins, MixinDescriptor<T> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // Deserializing an object-typed property yields a JsonElement; anything
        // else means the entity carries no mixin object to read from.
        if (mixins is not JsonElement { ValueKind: JsonValueKind.Object } container)
        {
            return default;
        }

        return container.TryGetProperty(descriptor.Key, out JsonElement value)
            ? value.Deserialize(descriptor.TypeInfo)
            : default;
    }

    /// <summary>
    /// The schema version an entity was saved with, parsed from its metadata.
    /// </summary>
    /// <param name="metadataMixins">The entity's <c>metadata.mixins</c> map.</param>
    /// <param name="key">The mixin key.</param>
    /// <returns>The version, or <c>null</c> when absent or unparseable.</returns>
    /// <remarks>
    /// Compare against <see cref="MixinDescriptor{T}.Version"/> to detect that a
    /// tenant's schema moved on while the loaded type did not.
    /// </remarks>
    public static int? SavedVersion(IDictionary<string, string>? metadataMixins, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return metadataMixins is not null && metadataMixins.TryGetValue(key, out string? url)
            ? VersionFromUrl(url)
            : null;
    }

    /// <summary>
    /// The schema version, for the specifications that type the same map as
    /// <c>object</c> rather than as a dictionary.
    /// </summary>
    /// <param name="metadataMixins">The entity's <c>metadata.mixins</c> property.</param>
    /// <param name="key">The mixin key.</param>
    public static int? SavedVersion(object? metadataMixins, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (metadataMixins is not JsonElement { ValueKind: JsonValueKind.Object } container
            || !container.TryGetProperty(key, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return VersionFromUrl(value.GetString());
    }

    // Emporix puts the version in the file name: «…deliveryOptionsMixIn.v6.json».
    private static int? VersionFromUrl(string? url)
    {
        if (url is null)
        {
            return null;
        }

        int marker = url.LastIndexOf(".v", StringComparison.Ordinal);

        if (marker < 0)
        {
            return null;
        }

        ReadOnlySpan<char> tail = url.AsSpan(marker + 2);
        int end = tail.IndexOf('.');

        return int.TryParse(
            end < 0 ? tail : tail[..end],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int version)
            ? version
            : null;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MixinRuntimeTests"`
Expected: 6 passed.

- [ ] **Step 7: Record the new public API**

Run: `./scripts/update-public-api.sh`
Then: `dotnet build` — expected: 0 warnings, 0 errors. `RS0016` here means the script did not run.

- [ ] **Step 8: Commit**

```bash
git add src/Viu.Emporix/Mixins tests/Viu.Emporix.Tests src/Viu.Emporix/PublicAPI.Unshipped.txt
git commit -m "feat: read typed mixin values off an entity

A descriptor carrying a JsonTypeInfo from the consumer's own serializer
context, plus a reader that takes the mixin container rather than the entity.
The generated entity classes share no interface and model metadata.mixins two
different ways, so the container is the only handle that works everywhere.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 2: Writer

**Files:**
- Create: `src/Viu.Emporix/Mixins/MixinWriter.cs`
- Modify: `tests/Viu.Emporix.Tests/MixinRuntimeTests.cs`
- Modify: `src/Viu.Emporix/JsonContexts.cs`

**Interfaces:**
- Consumes: `MixinDescriptor<T>` from Task 1.
- Produces: `MixinWriter.Create()` returning `MixinWriter`; instance method `Set<T>(MixinDescriptor<T> descriptor, T value)` returning `MixinWriter` for chaining; properties `Values` of type `JsonElement` and `SchemaUrls` of type `IDictionary<string, string>`.

- [ ] **Step 1: Add the serializer context for the writer**

Append to `src/Viu.Emporix/JsonContexts.cs`. It needs its own context, like every other one in that file:

```csharp
/// <summary>
/// Serialization for the mixin writer.
/// </summary>
/// <remarks>
/// Its own context, as every service has: the writer assembles a
/// <c>Dictionary&lt;string, JsonElement&gt;</c> and needs type information for
/// it. Reusing a service context would tie the mixin runtime to one service.
/// </remarks>
[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>))]
internal sealed partial class MixinJsonContext : JsonSerializerContext;
```

- [ ] **Step 2: Write the failing tests**

Append to `MixinRuntimeTests.cs`:

```csharp
    [Fact]
    public void The_writer_produces_the_value_and_the_schema_url_separately()
    {
        MixinWriter writer = MixinWriter.Create()
            .Set(Delivery, new TestDeliveryMixin { Packaging = "Paper", Weight = 2.5 });

        Assert.Equal(
            """{"deliveryOptions":{"packaging":"Paper","weight":2.5}}""",
            writer.Values.GetRawText());
        Assert.Equal(
            "https://cdn.emporix.io/deliveryOptionsMixIn.v6.json",
            writer.SchemaUrls["deliveryOptions"]);
    }

    [Fact]
    public void The_writer_omits_null_attributes()
    {
        // A schema declaring additionalProperties:false has no use for an
        // explicit null, and it is payload the tenant did not ask for.
        MixinWriter writer = MixinWriter.Create()
            .Set(Delivery, new TestDeliveryMixin { Packaging = "Paper" });

        Assert.DoesNotContain("null", writer.Values.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_writer_carries_several_mixins_at_once()
    {
        MixinDescriptor<TestLocalizedNote> other = new()
        {
            Key = "banner",
            Entity = "PRODUCT",
            Url = "https://cdn.emporix.io/bannerMixIn.v2.json",
            Version = 2,
            TypeInfo = TestJsonContext.Default.TestLocalizedNote,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["En"] = "en" },
        };

        MixinWriter writer = MixinWriter.Create()
            .Set(Delivery, new TestDeliveryMixin { Packaging = "Paper" })
            .Set(other, new TestLocalizedNote { En = "Sale" });

        Assert.Equal(2, writer.SchemaUrls.Count);
        Assert.True(writer.Values.TryGetProperty("banner", out _));
    }

    [Fact]
    public void A_round_trip_through_the_writer_reads_back_typed()
    {
        MixinWriter writer = MixinWriter.Create()
            .Set(Delivery, new TestDeliveryMixin { Packaging = "Plastic", Weight = 1.25 });

        TestDeliveryMixin? read = MixinReader.Read(writer.Values, Delivery);

        Assert.Equal("Plastic", read?.Packaging);
        Assert.Equal(1.25, read?.Weight);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinRuntimeTests"`
Expected: compile failure — `MixinWriter` does not exist.

- [ ] **Step 4: Write the writer**

Note the `JsonSerializerOptions` with `DefaultIgnoreCondition`: without it the writer emits `"note":null`, which the second test rejects.

Create `src/Viu.Emporix/Mixins/MixinWriter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Viu.Emporix.Mixins;

/// <summary>
/// Assembles mixin values and their schema URLs for writing onto an entity.
/// </summary>
/// <remarks>
/// <para>
/// Emporix wants both halves: the value under <c>mixins[key]</c> and the schema
/// URL under <c>metadata.mixins[key]</c>. Without the second the mixin is stored
/// unvalidated. The Node SDK calls this «the part consumers get wrong».
/// </para>
/// <para>
/// The two halves are returned separately and the caller assigns both, because
/// no interface spans the entity types that carry mixins:
/// </para>
/// <code>
/// var w = MixinWriter.Create().Set(Mixins.Delivery, value);
/// product.Mixins = w.Values;
/// product.Metadata.Mixins = w.SchemaUrls;
/// </code>
/// </remarks>
public sealed class MixinWriter
{
    // A null attribute is not written: a schema declaring
    // additionalProperties:false has no use for it, and it is payload nobody
    // asked for. Found by running a round-trip, not by reading the code.
    private static readonly JsonSerializerOptions ValueOptions =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _schemaUrls = new(StringComparer.Ordinal);

    private MixinWriter()
    {
    }

    /// <summary>Starts a new writer.</summary>
    public static MixinWriter Create() => new();

    /// <summary>Sets one mixin's value.</summary>
    /// <param name="descriptor">Which mixin to set.</param>
    /// <param name="value">The value.</param>
    /// <typeparam name="T">The mixin's generated type.</typeparam>
    /// <returns>The same writer, for chaining.</returns>
    public MixinWriter Set<T>(MixinDescriptor<T> descriptor, T value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // The descriptor's own JsonTypeInfo would carry the generated context's
        // options, which do not skip nulls. Re-resolving against local options
        // keeps the null handling in one place.
        JsonTypeInfo<T> typeInfo = (JsonTypeInfo<T>)ValueOptions
            .GetTypeInfo(typeof(T));

        _values[descriptor.Key] = JsonSerializer.SerializeToElement(value, typeInfo);
        _schemaUrls[descriptor.Key] = descriptor.Url;

        return this;
    }

    /// <summary>The value for the entity's <c>mixins</c> property.</summary>
    public JsonElement Values => JsonSerializer.SerializeToElement(
        _values, MixinJsonContext.Default.DictionaryStringJsonElement);

    /// <summary>
    /// The value for the entity's <c>metadata.mixins</c> property.
    /// </summary>
    public IDictionary<string, string> SchemaUrls => _schemaUrls;
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~MixinRuntimeTests"`
Expected: 10 passed.

If Step 4's `GetTypeInfo` call fails at runtime with `InvalidOperationException` because `ValueOptions` has no resolver, replace the body of `Set` with a direct serialize plus re-parse, which is the fallback that needs no resolver:

```csharp
        string json = JsonSerializer.Serialize(value, descriptor.TypeInfo);
        using JsonDocument document = JsonDocument.Parse(json);
        _values[descriptor.Key] = document.RootElement.Clone();
```

That variant keeps nulls, so the second test then needs the generated context to set `DefaultIgnoreCondition` instead — which is where Phase 3's generator puts it anyway. Prefer whichever passes; record which one in a comment.

- [ ] **Step 6: Record the public API and build**

Run: `./scripts/update-public-api.sh && dotnet build`
Expected: 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/Viu.Emporix tests/Viu.Emporix.Tests
git commit -m "feat: assemble mixin values and schema urls for writing

Emporix stores a mixin unvalidated unless metadata.mixins carries the schema
url alongside the value. The writer produces both halves and the caller
assigns each, since no interface spans the entity types that carry mixins.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
## Phase 2 — The q filter

### Task 3: Conditions

**Files:**
- Create: `src/Viu.Emporix/Mixins/MixinConditions.cs`
- Test: `tests/Viu.Emporix.Tests/MixinQueryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: four `readonly struct` types `TextCondition`, `NumberCondition`, `BoolCondition`, `AnyCondition`, each with an `internal string Render { get; }`. Static class `Is` with `EqualTo(string)`, `OneOf(params string[])`, `Matching(string)` returning `TextCondition`; `EqualTo(double)`, `AtLeast(double)`, `AtMost(double)`, `Between(double, double)` returning `NumberCondition`; `True()`, `False()` returning `BoolCondition`; `Present()`, `Missing()` returning `AnyCondition`.

**Why categories rather than `MixinCondition<TValue>`:** a generic condition paired with an `Expression<Func<T, TValue?>>` selector cannot be inferred — with `d.Weight` of type `double?`, C# cannot decide whether `TValue` is `double` under `Nullable<>` or is itself nullable, and `CS0411` results. Inference runs before constraint checking, so `where TValue : notnull` does not rescue it. This was found by compiling, not by reasoning.

- [ ] **Step 1: Write the failing tests**

Create `tests/Viu.Emporix.Tests/MixinQueryTests.cs`:

```csharp
using Viu.Emporix.Mixins;

namespace Viu.Emporix.Tests;

/// <summary>
/// Building an Emporix <c>q</c> filter over mixin attributes.
/// </summary>
/// <remarks>
/// The grammar is taken from the Node SDK, which runs it against real tenants.
/// Five forms in it are unverified against a tenant and are recorded as such in
/// the design spec — the range syntax, the localized path, exists and missing
/// semantics, the escaping, and whether metadata must be resent on PATCH. These
/// tests pin what the SDK emits, not that Emporix accepts it. Only the smoke
/// test can establish the latter.
/// </remarks>
public class MixinQueryTests
{
    [Fact]
    public void Text_conditions_render()
    {
        Assert.Equal("Paper", Is.EqualTo("Paper").Render);
        Assert.Equal("(S,M,L)", Is.OneOf("S", "M", "L").Render);
        Assert.Equal("~^Pa", Is.Matching("^Pa").Render);
    }

    [Fact]
    public void Number_conditions_render_with_an_invariant_decimal_point()
    {
        // A Swiss or German culture would render 2,5 and the query would break.
        Assert.Equal("2.5", Is.EqualTo(2.5).Render);
        Assert.Equal(">=10", Is.AtLeast(10).Render);
        Assert.Equal("<=20", Is.AtMost(20).Render);
        Assert.Equal("(>=1.5 AND <=4.5)", Is.Between(1.5, 4.5).Render);
    }

    [Fact]
    public void Presence_and_boolean_conditions_render()
    {
        Assert.Equal("true", Is.True().Render);
        Assert.Equal("false", Is.False().Render);
        Assert.Equal("exists", Is.Present().Render);
        Assert.Equal("missing", Is.Missing().Render);
    }

    [Fact]
    public void A_text_value_carrying_whitespace_is_refused()
    {
        // The q DSL separates clauses with spaces and the Node SDK records its
        // escaping as unverified. Refusing beats mangling.
        ArgumentException error = Assert.Throws<ArgumentException>(() => Is.EqualTo("Two Words"));
        Assert.Contains("whitespace", error.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => Is.OneOf("fine", "not fine"));
    }

    [Fact]
    public void An_empty_text_value_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Is.EqualTo(""));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinQueryTests"`
Expected: compile failure — `Is` does not exist.

- [ ] **Step 3: Write the conditions**

Create `src/Viu.Emporix/Mixins/MixinConditions.cs`:

```csharp
using System.Globalization;

namespace Viu.Emporix.Mixins;

/// <summary>A condition on a text attribute.</summary>
public readonly struct TextCondition
{
    internal TextCondition(string render) => Render = render;

    internal string Render { get; }
}

/// <summary>A condition on a numeric attribute.</summary>
public readonly struct NumberCondition
{
    internal NumberCondition(string render) => Render = render;

    internal string Render { get; }
}

/// <summary>A condition on a boolean attribute.</summary>
public readonly struct BoolCondition
{
    internal BoolCondition(string render) => Render = render;

    internal string Render { get; }
}

/// <summary>A condition on an attribute of any type.</summary>
public readonly struct AnyCondition
{
    internal AnyCondition(string render) => Render = render;

    internal string Render { get; }
}

/// <summary>
/// The conditions a mixin attribute can be filtered by.
/// </summary>
/// <remarks>
/// <para>
/// Categorised by value kind rather than generic over it. A generic condition
/// paired with a nullable property selector cannot be inferred: with a
/// <c>double?</c> attribute the compiler cannot tell whether the type argument
/// is <c>double</c> under <c>Nullable</c> or is itself nullable, and inference
/// runs before constraints, so no constraint fixes it.
/// </para>
/// <para>
/// The categories are what gates the operators: <see cref="AtLeast"/> returns a
/// <see cref="NumberCondition"/>, which fits no text selector, so misapplying it
/// is a compile error rather than a query the backend rejects.
/// </para>
/// </remarks>
public static class Is
{
    /// <summary>Matches a text attribute exactly.</summary>
    /// <param name="value">The value. Must not contain whitespace.</param>
    public static TextCondition EqualTo(string value) => new(Text(value));

    /// <summary>Matches any of several text values.</summary>
    /// <param name="values">The values. None may contain whitespace.</param>
    public static TextCondition OneOf(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length == 0)
        {
            throw new ArgumentException("Pass at least one value.", nameof(values));
        }

        return new($"({string.Join(',', values.Select(Text))})");
    }

    /// <summary>Matches a text attribute against a regular expression.</summary>
    /// <param name="regex">The expression, as Emporix evaluates it.</param>
    public static TextCondition Matching(string regex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regex);
        return new($"~{regex}");
    }

    /// <summary>Matches a numeric attribute exactly.</summary>
    /// <param name="value">The value.</param>
    public static NumberCondition EqualTo(double value) => new(Number(value));

    /// <summary>Matches a numeric attribute at or above a bound.</summary>
    /// <param name="value">The lower bound, inclusive.</param>
    public static NumberCondition AtLeast(double value) => new($">={Number(value)}");

    /// <summary>Matches a numeric attribute at or below a bound.</summary>
    /// <param name="value">The upper bound, inclusive.</param>
    public static NumberCondition AtMost(double value) => new($"<={Number(value)}");

    /// <summary>Matches a numeric attribute within a range.</summary>
    /// <param name="low">The lower bound, inclusive.</param>
    /// <param name="high">The upper bound, inclusive.</param>
    public static NumberCondition Between(double low, double high)
        => low > high
            ? throw new ArgumentException($"The lower bound {low} exceeds the upper bound {high}.", nameof(low))
            : new NumberCondition($"(>={Number(low)} AND <={Number(high)})");

    /// <summary>Matches a boolean attribute that is true.</summary>
    public static BoolCondition True() => new("true");

    /// <summary>Matches a boolean attribute that is false.</summary>
    public static BoolCondition False() => new("false");

    /// <summary>Matches an attribute that is present.</summary>
    public static AnyCondition Present() => new("exists");

    /// <summary>Matches an attribute that is absent.</summary>
    public static AnyCondition Missing() => new("missing");

    private static string Number(double value)
        => value.ToString(CultureInfo.InvariantCulture);

    // The q DSL separates clauses with spaces and the Node SDK records the safe
    // escaping as unverified upstream. A value carrying whitespace is refused
    // rather than mangled; MixinFilter.Raw is the way past this.
    private static string Text(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        return value.AsSpan().ContainsAny(' ', '\t', '\n', '\r')
            ? throw new ArgumentException(
                $"The value \"{value}\" contains whitespace, which the q syntax uses as an AND separator. Use MixinFilter.Raw for it.",
                nameof(value))
            : value;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~MixinQueryTests"`
Expected: 5 passed.

- [ ] **Step 5: Record the public API and commit**

```bash
./scripts/update-public-api.sh && dotnet build
git add src/Viu.Emporix tests/Viu.Emporix.Tests
git commit -m "feat: add mixin filter conditions

Categorised by value kind rather than generic over it: a generic condition
paired with a nullable property selector cannot be inferred, because the
compiler cannot tell a nullable value type from a nullable type argument and
inference runs before constraints. The categories are also what gates the
operators, so AtLeast on a text attribute fails at compile time.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 4: Filters and the capability gate

**Files:**
- Create: `src/Viu.Emporix/Mixins/MixinFilter.cs`
- Modify: `tests/Viu.Emporix.Tests/MixinQueryTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `MixinFilter` with `Build()` returning `string`, `And(MixinFilter)` returning `MixinFilter`, `Or(MixinFilter)` returning `CompoundMixinFilter`, static `Raw(string)` returning `MixinFilter`, and an `internal string Fragment { get; }`. `CompoundMixinFilter` with `Build(EmporixQuery)` returning `string`, `And(MixinFilter)` and `Or(MixinFilter)` returning `CompoundMixinFilter`. `EmporixQuery` with static properties `ProductSearch`, `AvailabilitySearch`, `QuoteSearch`, `ApprovalSearch`, `SchemaSearch`, `AuditLogSearch`, `CategorySearch`, `OrderList`, `VendorSearch`, `CustomerAdminSearch`. `MixinFilter` also gets an `internal static MixinFilter FromClauses(string fragment)`.

**The capability is per service, not per entity.** Verified against the Emporix documentation at `api-references/standard-practices/q-param` and against the Node SDK's per-method flags: `compoundLogicalQuery` is accepted by Approval, Audit Logs, Availability, Product, Quote and Schema, and by no other service. Add no value without a source.

- [ ] **Step 1: Write the failing tests**

Append to `MixinQueryTests.cs`:

```csharp
    [Fact]
    public void Plain_filters_join_with_a_space_which_every_q_endpoint_understands()
    {
        MixinFilter joined = MixinFilter.Raw("mixins.a.x:1").And(MixinFilter.Raw("mixins.a.y:2"));

        Assert.Equal("mixins.a.x:1 mixins.a.y:2", joined.Build());
    }

    [Fact]
    public void Or_produces_a_compound_query()
    {
        CompoundMixinFilter either = MixinFilter.Raw("mixins.a.x:1").Or(MixinFilter.Raw("mixins.a.x:2"));

        Assert.Equal(
            "compoundLogicalQuery:((mixins.a.x:1) OR (mixins.a.x:2))",
            either.Build(EmporixQuery.ProductSearch));
    }

    [Fact]
    public void A_compound_query_is_refused_for_a_service_that_cannot_run_it()
    {
        CompoundMixinFilter either = MixinFilter.Raw("mixins.a.x:1").Or(MixinFilter.Raw("mixins.a.x:2"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => either.Build(EmporixQuery.CategorySearch));

        Assert.Contains("Category", error.Message, StringComparison.Ordinal);
        Assert.Contains("And", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_capability_value_either_allows_or_refuses_a_compound_query(bool allowed)
    {
        EmporixQuery[] targets = allowed
            ? [EmporixQuery.ProductSearch, EmporixQuery.AvailabilitySearch, EmporixQuery.QuoteSearch,
               EmporixQuery.ApprovalSearch, EmporixQuery.SchemaSearch, EmporixQuery.AuditLogSearch]
            : [EmporixQuery.CategorySearch, EmporixQuery.OrderList,
               EmporixQuery.VendorSearch, EmporixQuery.CustomerAdminSearch];

        CompoundMixinFilter either = MixinFilter.Raw("a:1").Or(MixinFilter.Raw("a:2"));

        foreach (EmporixQuery target in targets)
        {
            if (allowed)
            {
                Assert.StartsWith("compoundLogicalQuery:", either.Build(target), StringComparison.Ordinal);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => either.Build(target));
            }
        }
    }

    [Fact]
    public void A_compound_filter_has_no_argumentless_build()
    {
        // The reason Or returns a separate type rather than a subclass: an
        // inherited Build() would let the capability gate be skipped silently.
        // If this test fails, someone made CompoundMixinFilter derive from
        // MixinFilter and the gate is now optional.
        Assert.Null(typeof(CompoundMixinFilter).GetMethod("Build", Type.EmptyTypes));
        Assert.False(typeof(MixinFilter).IsAssignableFrom(typeof(CompoundMixinFilter)));
    }

    [Fact]
    public void Anding_onto_a_compound_query_stays_compound()
    {
        string built = MixinFilter.Raw("a:1")
            .Or(MixinFilter.Raw("a:2"))
            .And(MixinFilter.Raw("published:true"))
            .Build(EmporixQuery.ProductSearch);

        Assert.Contains("compoundLogicalQuery:", built, StringComparison.Ordinal);
        Assert.Contains("published:true", built, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinQueryTests"`
Expected: compile failure — `MixinFilter` does not exist.

- [ ] **Step 3: Write the filters**

Create `src/Viu.Emporix/Mixins/MixinFilter.cs`:

```csharp
namespace Viu.Emporix.Mixins;

/// <summary>
/// A built <c>q</c> fragment that any q-capable endpoint accepts.
/// </summary>
/// <remarks>
/// Pass <see cref="Build()"/> to any service method taking a <c>q</c> filter.
/// </remarks>
public sealed class MixinFilter
{
    private MixinFilter(string fragment) => Fragment = fragment;

    internal string Fragment { get; }

    internal static MixinFilter FromClauses(string fragment) => new(fragment);

    /// <summary>The fragment, for a service method's <c>q</c> parameter.</summary>
    public string Build() => Fragment;

    /// <summary>
    /// Combines with another filter using AND.
    /// </summary>
    /// <param name="other">The filter to combine with.</param>
    /// <remarks>
    /// A space is the q syntax's AND, and every q endpoint understands it, so
    /// this needs no capability.
    /// </remarks>
    public MixinFilter And(MixinFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new($"{Fragment} {other.Fragment}");
    }

    /// <summary>
    /// Combines with another filter using OR.
    /// </summary>
    /// <param name="other">The filter to combine with.</param>
    /// <returns>
    /// A compound filter, whose <see cref="CompoundMixinFilter.Build"/> requires
    /// naming the target service.
    /// </returns>
    /// <remarks>
    /// OR needs the <c>compoundLogicalQuery</c> operator, which only some
    /// services accept, so the result is a different type: there is no
    /// argumentless <c>Build</c> on it, and the capability cannot be forgotten.
    /// </remarks>
    public CompoundMixinFilter Or(MixinFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return CompoundMixinFilter.FromFragment(
            $"compoundLogicalQuery:(({Fragment}) OR ({other.Fragment}))");
    }

    /// <summary>
    /// Wraps a fragment written by hand.
    /// </summary>
    /// <param name="fragment">The q fragment, escaped by the caller.</param>
    /// <remarks>
    /// The way past the whitespace guard, and the way to combine a mixin filter
    /// with a non-mixin clause such as <c>published:true</c>.
    /// </remarks>
    public static MixinFilter Raw(string fragment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment);
        return new(fragment);
    }
}

/// <summary>
/// A service endpoint, and whether it can run a compound query.
/// </summary>
/// <remarks>
/// <para>
/// <c>compoundLogicalQuery</c> is a per-service capability, not a per-entity
/// one: the Emporix documentation scopes the operator to Approval, Audit Logs,
/// Availability, Product, Quote and Schema, and the Node SDK carries the same
/// flag per method. Knowing which entity a mixin hangs on says nothing about
/// which endpoint is being called.
/// </para>
/// <para>
/// Add no value here without a source for it.
/// </para>
/// </remarks>
public sealed class EmporixQuery
{
    private EmporixQuery(string service, bool compound)
    {
        Service = service;
        Compound = compound;
    }

    internal string Service { get; }

    internal bool Compound { get; }

    /// <summary>Product search. Accepts compound queries.</summary>
    public static EmporixQuery ProductSearch { get; } = new("Product", true);

    /// <summary>Availability search. Accepts compound queries.</summary>
    public static EmporixQuery AvailabilitySearch { get; } = new("Availability", true);

    /// <summary>Quote search. Accepts compound queries.</summary>
    public static EmporixQuery QuoteSearch { get; } = new("Quote", true);

    /// <summary>Approval search. Accepts compound queries.</summary>
    public static EmporixQuery ApprovalSearch { get; } = new("Approval", true);

    /// <summary>Schema and custom entity search. Accepts compound queries.</summary>
    public static EmporixQuery SchemaSearch { get; } = new("Schema", true);

    /// <summary>Audit log search. Accepts compound queries.</summary>
    public static EmporixQuery AuditLogSearch { get; } = new("AuditLog", true);

    /// <summary>Category search. Rejects compound queries.</summary>
    public static EmporixQuery CategorySearch { get; } = new("Category", false);

    /// <summary>Order listing. Rejects compound queries.</summary>
    public static EmporixQuery OrderList { get; } = new("Order", false);

    /// <summary>Vendor search. Rejects compound queries.</summary>
    public static EmporixQuery VendorSearch { get; } = new("Vendor", false);

    /// <summary>Customer search, seller side. Rejects compound queries.</summary>
    public static EmporixQuery CustomerAdminSearch { get; } = new("CustomerAdmin", false);
}

/// <summary>
/// A <c>q</c> fragment using <c>compoundLogicalQuery</c>, which only some
/// services accept.
/// </summary>
/// <remarks>
/// Deliberately not derived from <see cref="MixinFilter"/>: inheriting its
/// argumentless <c>Build</c> would let the capability gate be skipped without a
/// diagnostic.
/// </remarks>
public sealed class CompoundMixinFilter
{
    private CompoundMixinFilter(string fragment) => Fragment = fragment;

    internal string Fragment { get; }

    internal static CompoundMixinFilter FromFragment(string fragment) => new(fragment);

    /// <summary>
    /// The fragment, if the target service can run it.
    /// </summary>
    /// <param name="target">The endpoint this filter is going to.</param>
    /// <exception cref="InvalidOperationException">
    /// The service does not accept <c>compoundLogicalQuery</c>.
    /// </exception>
    public string Build(EmporixQuery target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.Compound
            ? Fragment
            : throw new InvalidOperationException(
                $"The {target.Service} service does not accept compoundLogicalQuery. Combine the conditions with And instead of Or.");
    }

    /// <summary>Combines with another filter using AND.</summary>
    /// <param name="other">The filter to combine with.</param>
    public CompoundMixinFilter And(MixinFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new($"compoundLogicalQuery:(({Fragment}) AND ({other.Fragment}))");
    }

    /// <summary>Combines with another filter using OR.</summary>
    /// <param name="other">The filter to combine with.</param>
    public CompoundMixinFilter Or(MixinFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new($"compoundLogicalQuery:(({Fragment}) OR ({other.Fragment}))");
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~MixinQueryTests"`
Expected: 12 passed — the `Theory` counts as two.

- [ ] **Step 5: Record the public API and commit**

```bash
./scripts/update-public-api.sh && dotnet build
git add src/Viu.Emporix tests/Viu.Emporix.Tests
git commit -m "feat: gate compound mixin queries by target service

compoundLogicalQuery is a per-service capability, confirmed against the
Emporix documentation and the Node SDK's per-method flags. Or returns a
separate type whose Build requires naming the endpoint, so a query the
backend cannot run fails to compile rather than returning a 400.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 5: The builder

**Files:**
- Create: `src/Viu.Emporix/Mixins/MixinQuery.cs`
- Modify: `tests/Viu.Emporix.Tests/MixinQueryTests.cs`

**Interfaces:**
- Consumes: `MixinDescriptor<T>` and its `Attributes` table from Task 1; the four condition structs from Task 3; `MixinFilter.FromClauses` from Task 4.
- Produces: static `MixinQuery.For<T>(MixinDescriptor<T>)` returning `MixinQueryBuilder<T>`. On the builder: `Where(Expression<Func<T, string?>>, TextCondition)`, `Where(Expression<Func<T, double?>>, NumberCondition)`, `Where(Expression<Func<T, int?>>, NumberCondition)`, `Where(Expression<Func<T, bool?>>, BoolCondition)`, `Where<TAttr>(Expression<Func<T, TAttr?>>, AnyCondition)`, `WhereEnum<TEnum>(Expression<Func<T, TEnum?>>, TEnum) where TEnum : struct, Enum`, `WhereLocalized<TAttr>(Expression<Func<T, TAttr?>>, string, TextCondition)` — all returning `MixinQueryBuilder<T>` — and `Build()` returning `MixinFilter`.

- [ ] **Step 1: Write the failing tests**

Append to `MixinQueryTests.cs`. The descriptor repeats Task 1's on purpose — a task may be read on its own:

```csharp
    private static MixinDescriptor<TestDeliveryMixin> Delivery => new()
    {
        Key = "deliveryOptions",
        Entity = "PRODUCT",
        Url = "https://cdn.emporix.io/deliveryOptionsMixIn.v6.json",
        Version = 6,
        TypeInfo = TestJsonContext.Default.TestDeliveryMixin,
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Packaging"] = "packaging",
            ["Weight"] = "weight",
            ["Note"] = "note",
        },
    };

    [Fact]
    public void A_clause_targets_the_namespaced_attribute_path()
    {
        string q = MixinQuery.For(Delivery)
            .Where(d => d.Packaging, Is.EqualTo("Paper"))
            .Build()
            .Build();

        Assert.Equal("mixins.deliveryOptions.packaging:Paper", q);
    }

    [Fact]
    public void Several_clauses_are_anded_by_a_space()
    {
        string q = MixinQuery.For(Delivery)
            .Where(d => d.Packaging, Is.EqualTo("Paper"))
            .Where(d => d.Weight, Is.Between(1.0, 5.0))
            .Build()
            .Build();

        Assert.Equal(
            "mixins.deliveryOptions.packaging:Paper mixins.deliveryOptions.weight:(>=1 AND <=5)",
            q);
    }

    [Fact]
    public void A_localized_clause_carries_the_language_segment()
    {
        string q = MixinQuery.For(Delivery)
            .WhereLocalized(d => d.Note, "en", Is.EqualTo("Sale"))
            .Build()
            .Build();

        Assert.Equal("mixins.deliveryOptions.note.en:Sale", q);
    }

    [Fact]
    public void Presence_works_on_an_attribute_of_any_type()
    {
        string q = MixinQuery.For(Delivery)
            .Where(d => d.Note, Is.Present())
            .Build()
            .Build();

        Assert.Equal("mixins.deliveryOptions.note:exists", q);
    }

    [Fact]
    public void An_expression_that_is_not_a_property_selector_is_refused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => MixinQuery.For(Delivery).Where(d => "constant", Is.EqualTo("x")));

        Assert.Contains("property", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_property_that_is_not_an_attribute_of_this_mixin_is_refused()
    {
        MixinDescriptor<TestDeliveryMixin> incomplete = new()
        {
            Key = "deliveryOptions",
            Entity = "PRODUCT",
            Url = "https://cdn.emporix.io/deliveryOptionsMixIn.v6.json",
            Version = 6,
            TypeInfo = TestJsonContext.Default.TestDeliveryMixin,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["Packaging"] = "packaging" },
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => MixinQuery.For(incomplete).Where(d => d.Weight, Is.AtLeast(1)));

        Assert.Contains("Weight", error.Message, StringComparison.Ordinal);
        Assert.Contains("deliveryOptions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Building_without_a_condition_is_refused()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => MixinQuery.For(Delivery).Build());

        Assert.Contains("deliveryOptions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_language_is_refused()
    {
        Assert.Throws<ArgumentException>(
            () => MixinQuery.For(Delivery).WhereLocalized(d => d.Note, "  ", Is.EqualTo("Sale")));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinQueryTests"`
Expected: compile failure — `MixinQuery` does not exist.

- [ ] **Step 3: Write the builder**

Create `src/Viu.Emporix/Mixins/MixinQuery.cs`:

```csharp
using System.Linq.Expressions;

namespace Viu.Emporix.Mixins;

/// <summary>
/// Builds an Emporix <c>q</c> filter over a mixin's attributes.
/// </summary>
/// <example>
/// <code>
/// string q = MixinQuery.For(Mixins.Delivery)
///     .Where(d =&gt; d.Packaging, Is.EqualTo("Paper"))
///     .Where(d =&gt; d.Weight, Is.AtLeast(2))
///     .Build()
///     .Build();
///
/// await client.Products.SearchAsync(q);
/// </code>
/// </example>
public static class MixinQuery
{
    /// <summary>Starts a filter for one mixin.</summary>
    /// <param name="descriptor">The mixin to filter on.</param>
    /// <typeparam name="T">The mixin's generated type.</typeparam>
    public static MixinQueryBuilder<T> For<T>(MixinDescriptor<T> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new MixinQueryBuilder<T>(descriptor);
    }
}

/// <summary>
/// Collects conditions on one mixin's attributes.
/// </summary>
/// <typeparam name="T">The mixin's generated type.</typeparam>
/// <remarks>
/// The <c>Where</c> overloads resolve from the selector's return type, so the
/// condition's category decides which operators an attribute accepts. Selecting
/// a property is the only supported expression: nothing is evaluated, only the
/// member's name is read, which is why no reflection or expression compilation
/// is involved and the whole builder stays AOT-safe.
/// </remarks>
public sealed class MixinQueryBuilder<T>
{
    private readonly MixinDescriptor<T> _descriptor;
    private readonly List<string> _clauses = [];

    internal MixinQueryBuilder(MixinDescriptor<T> descriptor) => _descriptor = descriptor;

    /// <summary>Adds a condition on a text attribute.</summary>
    /// <param name="selector">The attribute, for example <c>d =&gt; d.Packaging</c>.</param>
    /// <param name="condition">The condition.</param>
    public MixinQueryBuilder<T> Where(Expression<Func<T, string?>> selector, TextCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds a condition on a decimal attribute.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="condition">The condition.</param>
    public MixinQueryBuilder<T> Where(Expression<Func<T, double?>> selector, NumberCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds a condition on an integer attribute.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="condition">The condition.</param>
    public MixinQueryBuilder<T> Where(Expression<Func<T, int?>> selector, NumberCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds a condition on a boolean attribute.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="condition">The condition.</param>
    public MixinQueryBuilder<T> Where(Expression<Func<T, bool?>> selector, BoolCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds a presence condition, which any attribute type accepts.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="condition">The condition.</param>
    /// <typeparam name="TAttr">The attribute's type, unconstrained.</typeparam>
    public MixinQueryBuilder<T> Where<TAttr>(Expression<Func<T, TAttr?>> selector, AnyCondition condition)
        => Add(selector, condition.Render, language: null);

    /// <summary>Adds an equality condition on an enum attribute.</summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="value">The value to match.</param>
    /// <typeparam name="TEnum">The generated enum type.</typeparam>
    /// <remarks>
    /// The generator emits an enum for a schema declaring <c>enum</c>, and the
    /// wire form is the member name.
    /// </remarks>
    public MixinQueryBuilder<T> WhereEnum<TEnum>(Expression<Func<T, TEnum?>> selector, TEnum value)
        where TEnum : struct, Enum
        => Add(selector, value.ToString(), language: null);

    /// <summary>
    /// Adds a condition on one language of a localized attribute.
    /// </summary>
    /// <param name="selector">The attribute.</param>
    /// <param name="language">The language tag, for example <c>en</c>.</param>
    /// <param name="condition">The condition.</param>
    /// <typeparam name="TAttr">The attribute's type, unconstrained.</typeparam>
    /// <remarks>
    /// A separate name rather than an overload: a localized attribute is an
    /// object of language keys, so its selector type says nothing about the
    /// compared value, and as an overload it was ambiguous.
    /// </remarks>
    public MixinQueryBuilder<T> WhereLocalized<TAttr>(
        Expression<Func<T, TAttr?>> selector,
        string language,
        TextCondition condition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return Add(selector, condition.Render, language);
    }

    /// <summary>The filter.</summary>
    /// <exception cref="InvalidOperationException">No condition was added.</exception>
    public MixinFilter Build()
        => _clauses.Count == 0
            ? throw new InvalidOperationException(
                $"No condition was added for the mixin \"{_descriptor.Key}\".")
            : MixinFilter.FromClauses(string.Join(' ', _clauses));

    private MixinQueryBuilder<T> Add<TAttr>(
        Expression<Func<T, TAttr>> selector,
        string render,
        string? language)
    {
        string attribute = Attribute(selector);

        _clauses.Add(language is null
            ? $"mixins.{_descriptor.Key}.{attribute}:{render}"
            : $"mixins.{_descriptor.Key}.{attribute}.{language}:{render}");

        return this;
    }

    // The selector yields the CLR name; the descriptor's generated table maps it
    // to the JSON name. Nothing is read that is not already in the tree, so this
    // needs neither reflection nor expression compilation.
    private string Attribute<TAttr>(Expression<Func<T, TAttr>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (selector.Body is not MemberExpression member)
        {
            throw new ArgumentException(
                "Select a property of the mixin, for example d => d.Packaging.",
                nameof(selector));
        }

        return _descriptor.Attributes.TryGetValue(member.Member.Name, out string? json)
            ? json
            : throw new ArgumentException(
                $"{member.Member.Name} is not an attribute of the mixin \"{_descriptor.Key}\".",
                nameof(selector));
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~MixinQueryTests"`
Expected: 20 passed.

- [ ] **Step 5: Verify the AOT bar on the real package**

Run: `dotnet publish samples/Viu.Emporix.Sample --configuration Release`
Expected: 0 warnings. An `IL2026` or `IL3050` here means something in the builder resolves a type at runtime; the expression selector must only read `Member.Name`.

- [ ] **Step 6: Record the public API and commit**

```bash
./scripts/update-public-api.sh && dotnet build
git add src/Viu.Emporix tests/Viu.Emporix.Tests
git commit -m "feat: build type-safe q filters over mixin attributes

A property selector supplies the attribute name and the condition category
supplies the operators, so the compiler rejects an operator that does not fit
the attribute. Only the member name is read from the expression, never
evaluated, which keeps the builder free of reflection.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
## Phase 3 — The generator tool

### Task 6: Project, configuration and CLI skeleton

**Files:**
- Create: `src/Viu.Emporix.MixinSync/Viu.Emporix.MixinSync.csproj`
- Create: `src/Viu.Emporix.MixinSync/MixinConfig.cs`
- Create: `src/Viu.Emporix.MixinSync/RawMixin.cs`
- Create: `src/Viu.Emporix.MixinSync/Program.cs`
- Modify: `Directory.Packages.props`
- Modify: `Viu.Emporix.slnx` or `*.sln` — whichever the repository has
- Test: `tests/Viu.Emporix.Tests/MixinSyncTests.cs`
- Modify: `tests/Viu.Emporix.Tests/Viu.Emporix.Tests.csproj`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `MixinConfig` with `Tenant`, `Namespace`, `Out`, `LockFile` as `string` properties and `static MixinConfig Load(string path)`; `MixinConfig.Validate()` throwing on a missing value. `RawMixin` with `Key`, `Entity`, `Url` as `string`, `Version` as `int`, `Schema` as `string` holding the raw JSON. `MixinJson.Options` as the shared `JsonSerializerOptions`.

- [ ] **Step 1: Add the package version centrally**

In `Directory.Packages.props`, add a new `ItemGroup` after the tooling one:

```xml
  <!--
    Generator tooling for Viu.Emporix.MixinSync only. NJsonSchema pulls
    Newtonsoft.Json, which is why the tool is a separate package: the core
    package keeps its zero-dependency promise.
  -->
  <ItemGroup>
    <PackageVersion Include="NJsonSchema.CodeGeneration.CSharp" Version="11.6.1" />
  </ItemGroup>
```

- [ ] **Step 2: Create the project**

Create `src/Viu.Emporix.MixinSync/Viu.Emporix.MixinSync.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>Viu.Emporix.MixinSync</RootNamespace>
    <IsPackable>true</IsPackable>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>emporix-mixins</ToolCommandName>
    <PackageId>Viu.Emporix.MixinSync</PackageId>
    <Description>Generates typed C# for an Emporix tenant's mixins and detects schema drift.</Description>
    <PackageTags>emporix;mixins;json-schema;codegen</PackageTags>
  </PropertyGroup>

  <!--
    A developer-machine tool, so the trimming and AOT requirements from
    Directory.Build.props do not apply — NJsonSchema is not AOT-compatible.
    Same arrangement as tools/Viu.Emporix.SpecSync. Warnings-as-errors stays.
  -->
  <PropertyGroup>
    <IsAotCompatible>false</IsAotCompatible>
    <EnableTrimAnalyzer>false</EnableTrimAnalyzer>
    <EnableAotAnalyzer>false</EnableAotAnalyzer>
    <EnableSingleFileAnalyzer>false</EnableSingleFileAnalyzer>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NJsonSchema.CodeGeneration.CSharp" />
  </ItemGroup>

  <!-- For SchemaService and EmporixClient when pulling from a tenant. -->
  <ItemGroup>
    <ProjectReference Include="../Viu.Emporix/Viu.Emporix.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Viu.Emporix.Tests" />
  </ItemGroup>

</Project>
```

Add the project to the solution: `dotnet sln add src/Viu.Emporix.MixinSync`.
Add a project reference from the tests: in `tests/Viu.Emporix.Tests/Viu.Emporix.Tests.csproj`, alongside the existing `SpecSync` reference, add

```xml
    <ProjectReference Include="../../src/Viu.Emporix.MixinSync/Viu.Emporix.MixinSync.csproj" />
```

- [ ] **Step 3: Write the failing config tests**

Create `tests/Viu.Emporix.Tests/MixinSyncTests.cs`:

```csharp
using Viu.Emporix.MixinSync;

namespace Viu.Emporix.Tests;

/// <summary>
/// The mixin generator tool.
/// </summary>
/// <remarks>
/// Tests here cover the pure parts — configuration, the lockfile, the attribute
/// fallback, collision detection. Whether a tenant's Schema Service answers as
/// expected is not testable here and is verified by the smoke test instead.
/// </remarks>
public class MixinSyncTests
{
    [Fact]
    public void A_configuration_file_is_read()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emporix-mixins-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "tenant": "acme",
              "namespace": "Acme.Mixins",
              "out": "src/Acme.Shop/Mixins/Generated",
              "lockFile": "src/Acme.Shop/Mixins/mixins.lock.json"
            }
            """);

        try
        {
            MixinConfig config = MixinConfig.Load(path);

            Assert.Equal("acme", config.Tenant);
            Assert.Equal("Acme.Mixins", config.Namespace);
            Assert.Equal("src/Acme.Shop/Mixins/Generated", config.Out);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_configuration_missing_a_value_is_refused_by_name()
    {
        MixinConfig config = new() { Tenant = "acme", Namespace = "", Out = "out", LockFile = "lock.json" };

        ArgumentException error = Assert.Throws<ArgumentException>(config.Validate);

        Assert.Contains("namespace", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_configuration_file_names_the_path()
    {
        string path = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json");

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() => MixinConfig.Load(path));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: compile failure — `MixinConfig` does not exist.

- [ ] **Step 5: Write the configuration and the raw mixin**

Create `src/Viu.Emporix.MixinSync/MixinConfig.cs`:

```csharp
using System.Text.Json;

namespace Viu.Emporix.MixinSync;

/// <summary>Shared JSON handling for the tool's own files.</summary>
/// <remarks>
/// Reflection-based, like <c>SpecSync</c>: this is a developer-machine tool with
/// the AOT requirement lifted, and source generation here would buy nothing.
/// Indented and camel-cased because every file it writes is version-controlled
/// and read in reviews.
/// </remarks>
internal static class MixinJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>What <c>emporix-mixins.json</c> holds.</summary>
/// <remarks>
/// Credentials are deliberately absent: they come from
/// <c>EMPORIX_BACKEND_CLIENT_ID</c> and <c>EMPORIX_BACKEND_SECRET</c>, so this
/// file carries nothing secret and belongs in version control.
/// </remarks>
public sealed class MixinConfig
{
    /// <summary>The Emporix tenant.</summary>
    public string Tenant { get; set; } = string.Empty;

    /// <summary>The root namespace for the generated code.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Where the generated files go, relative to the config file.</summary>
    public string Out { get; set; } = string.Empty;

    /// <summary>Where the lockfile goes, relative to the config file.</summary>
    public string LockFile { get; set; } = string.Empty;

    /// <summary>Reads a configuration file.</summary>
    /// <param name="path">The file to read.</param>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    public static MixinConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No configuration at {path}. Create an emporix-mixins.json with tenant, namespace, out and lockFile.",
                path);
        }

        MixinConfig config = JsonSerializer.Deserialize<MixinConfig>(
            File.ReadAllText(path), MixinJson.Options)
            ?? throw new InvalidOperationException($"{path} is empty.");

        config.Validate();

        return config;
    }

    /// <summary>Checks that every value is set.</summary>
    /// <exception cref="ArgumentException">A value is missing.</exception>
    public void Validate()
    {
        Require(Tenant, "tenant");
        Require(Namespace, "namespace");
        Require(Out, "out");
        Require(LockFile, "lockFile");

        static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"The configuration value \"{name}\" is missing.", name);
            }
        }
    }
}
```

Create `src/Viu.Emporix.MixinSync/RawMixin.cs`:

```csharp
namespace Viu.Emporix.MixinSync;

/// <summary>
/// One mixin, normalized from the Schema Service.
/// </summary>
/// <remarks>
/// This is what the snapshot file holds, so <c>generate</c> can run without a
/// tenant and without the network. A schema assigned to several entity types
/// yields one of these per type.
/// </remarks>
public sealed class RawMixin
{
    /// <summary>The schema id, which is the key under <c>mixins</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>One entity type the schema is assigned to.</summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>The version Emporix assigned.</summary>
    public int Version { get; set; }

    /// <summary>The hosted schema URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The JSON Schema itself, as text.</summary>
    /// <remarks>
    /// Kept as text rather than parsed: it is hashed for the lockfile and handed
    /// to NJsonSchema, and both want the original bytes.
    /// </remarks>
    public string Schema { get; set; } = string.Empty;
}
```

- [ ] **Step 6: Write the CLI skeleton**

Create `src/Viu.Emporix.MixinSync/Program.cs`. The three commands are stubs that the next tasks fill in; the dispatch and the exit codes are what this task delivers:

```csharp
using Viu.Emporix.MixinSync;

// Generates typed C# for an Emporix tenant's mixins and detects schema drift.
//
//   emporix-mixins pull       read the Schema Service, write snapshot and lock
//   emporix-mixins generate   turn the snapshot into C#
//   emporix-mixins check      compare the tenant against the lock, for CI

string command = args.Length > 0 ? args[0] : "help";
string configPath = args.Length > 1
    ? args[1]
    : Path.Combine(Directory.GetCurrentDirectory(), "emporix-mixins.json");

try
{
    return command switch
    {
        "pull" => await PullAsync(MixinConfig.Load(configPath), configPath),
        "generate" => Generate(MixinConfig.Load(configPath), configPath),
        "check" => await CheckAsync(MixinConfig.Load(configPath), configPath),
        "help" or "--help" or "-h" => Usage(0),
        _ => Usage(2, $"Unknown command \"{command}\"."),
    };
}
catch (Exception error) when (error is FileNotFoundException or ArgumentException or InvalidOperationException)
{
    // The expected failures — a missing config, a missing value, a collision in
    // the generated names. A stack trace would bury the message that matters.
    Console.Error.WriteLine($"emporix-mixins: {error.Message}");
    return 1;
}

static int Usage(int code, string? problem = null)
{
    if (problem is not null)
    {
        Console.Error.WriteLine($"emporix-mixins: {problem}");
    }

    Console.WriteLine("""
        usage: emporix-mixins <pull|generate|check> [config]

          pull      Read the tenant's Schema Service, write the snapshot and the lockfile.
          generate  Turn the snapshot into C# types, contexts and a registry.
          check     Compare the tenant against the lockfile. Exits 1 on drift.

        The config defaults to ./emporix-mixins.json. Credentials come from
        EMPORIX_BACKEND_CLIENT_ID and EMPORIX_BACKEND_SECRET.
        """);

    return code;
}
```

Add the three placeholder methods at the end of `Program.cs` so it compiles. Each task below replaces one:

```csharp
static Task<int> PullAsync(MixinConfig config, string configPath)
    => throw new NotImplementedException("Task 10 implements pull.");

static int Generate(MixinConfig config, string configPath)
    => throw new NotImplementedException("Task 11 implements generate.");

static Task<int> CheckAsync(MixinConfig config, string configPath)
    => throw new NotImplementedException("Task 12 implements check.");
```

- [ ] **Step 7: Run the tests and the tool**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: 3 passed.

Run: `dotnet run --project src/Viu.Emporix.MixinSync -- help`
Expected: the usage text, exit 0.

- [ ] **Step 8: Commit**

```bash
git add src/Viu.Emporix.MixinSync Directory.Packages.props tests/Viu.Emporix.Tests *.sln* 
git commit -m "feat: add the mixin sync tool skeleton

A dotnet tool with pull, generate and check. It carries the AOT opt-out in
its own csproj the way SpecSync does, because NJsonSchema is not
AOT-compatible and pulls Newtonsoft — which is exactly why this is a separate
package rather than part of the core.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 7: Lockfile

**Files:**
- Create: `src/Viu.Emporix.MixinSync/Lockfile.cs`
- Modify: `tests/Viu.Emporix.Tests/MixinSyncTests.cs`

**Interfaces:**
- Consumes: `RawMixin` and `MixinJson.Options` from Task 6.
- Produces: `LockEntry` with `Entity`, `Url`, `Sha256` as `string` and `Version` as `int`; `Lockfile` with `GeneratedAt` as `DateTimeOffset` and `Mixins` as `SortedDictionary<string, LockEntry>`; `Lockfile.Build(IEnumerable<RawMixin>, DateTimeOffset)` returning `Lockfile`; `Lockfile.Diff(Lockfile?, Lockfile)` returning `IReadOnlyList<string>`; `Lockfile.Read(string)` returning `Lockfile?`; `Lockfile.Write(string, Lockfile)`.

- [ ] **Step 1: Write the failing tests**

Append to `MixinSyncTests.cs`:

```csharp
    private static RawMixin Delivery(int version = 6, string schema = """{"type":"object"}""") => new()
    {
        Key = "deliveryOptions",
        Entity = "PRODUCT",
        Version = version,
        Url = $"https://cdn.emporix.io/deliveryOptionsMixIn.v{version}.json",
        Schema = schema,
    };

    [Fact]
    public void A_lockfile_records_version_url_and_a_content_hash()
    {
        Lockfile lock1 = Lockfile.Build([Delivery()], DateTimeOffset.UnixEpoch);

        LockEntry entry = lock1.Mixins["deliveryOptions"];
        Assert.Equal(6, entry.Version);
        Assert.Equal("PRODUCT", entry.Entity);
        Assert.Equal(64, entry.Sha256.Length);
    }

    [Fact]
    public void A_raised_version_is_drift()
    {
        Lockfile before = Lockfile.Build([Delivery(6)], DateTimeOffset.UnixEpoch);
        Lockfile after = Lockfile.Build([Delivery(7)], DateTimeOffset.UnixEpoch);

        IReadOnlyList<string> drift = Lockfile.Diff(before, after);

        Assert.Single(drift);
        Assert.Contains("deliveryOptions", drift[0], StringComparison.Ordinal);
        Assert.Contains("6", drift[0], StringComparison.Ordinal);
        Assert.Contains("7", drift[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_schema_at_the_same_version_is_also_drift()
    {
        // Emporix can change a schema without raising the version, and then the
        // content hash is the only signal there is.
        Lockfile before = Lockfile.Build([Delivery(6, """{"type":"object"}""")], DateTimeOffset.UnixEpoch);
        Lockfile after = Lockfile.Build([Delivery(6, """{"type":"object","title":"x"}""")], DateTimeOffset.UnixEpoch);

        Assert.Single(Lockfile.Diff(before, after));
    }

    [Fact]
    public void An_added_or_removed_mixin_is_drift()
    {
        Lockfile one = Lockfile.Build([Delivery()], DateTimeOffset.UnixEpoch);
        Lockfile none = Lockfile.Build([], DateTimeOffset.UnixEpoch);

        Assert.Single(Lockfile.Diff(none, one));
        Assert.Single(Lockfile.Diff(one, none));
    }

    [Fact]
    public void An_identical_lockfile_is_not_drift()
    {
        Lockfile lock1 = Lockfile.Build([Delivery()], DateTimeOffset.UnixEpoch);
        Lockfile lock2 = Lockfile.Build([Delivery()], DateTimeOffset.UtcNow);

        // The timestamp differs on purpose: it must not count as drift, or every
        // check would fail.
        Assert.Empty(Lockfile.Diff(lock1, lock2));
    }

    [Fact]
    public void A_missing_lockfile_reports_every_mixin_as_drift()
    {
        Lockfile live = Lockfile.Build([Delivery()], DateTimeOffset.UnixEpoch);

        Assert.Single(Lockfile.Diff(null, live));
    }

    [Fact]
    public void A_lockfile_round_trips_through_disk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mixins-{Guid.NewGuid():N}.lock.json");

        try
        {
            Lockfile.Write(path, Lockfile.Build([Delivery()], DateTimeOffset.UnixEpoch));

            Assert.Empty(Lockfile.Diff(
                Lockfile.Read(path), Lockfile.Build([Delivery()], DateTimeOffset.UnixEpoch)));
        }
        finally
        {
            File.Delete(path);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: compile failure — `Lockfile` does not exist.

- [ ] **Step 3: Write the lockfile**

Create `src/Viu.Emporix.MixinSync/Lockfile.cs`:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Viu.Emporix.MixinSync;

/// <summary>What one mixin looked like when the lockfile was written.</summary>
public sealed class LockEntry
{
    /// <summary>The entity type the schema is assigned to.</summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>The version Emporix had assigned.</summary>
    public int Version { get; set; }

    /// <summary>The hosted schema URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The SHA-256 digest of the schema text.
    /// </summary>
    /// <remarks>
    /// The version alone is not enough: a schema can change without the version
    /// moving, and then this is the only thing that differs.
    /// </remarks>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// The state of a tenant's mixins, as of the last <c>pull</c>.
/// </summary>
/// <remarks>
/// Version-controlled next to the generated code, and the input to <c>check</c>.
/// Sorted so its diff is legible in a review, following the same reasoning as
/// <c>SpecSync</c>'s sync manifest.
/// </remarks>
public sealed class Lockfile
{
    /// <summary>When this state was produced.</summary>
    /// <remarks>Informational — <see cref="Diff"/> ignores it.</remarks>
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>The entries, keyed by mixin key.</summary>
    public SortedDictionary<string, LockEntry> Mixins { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Builds a lockfile from pulled mixins.</summary>
    /// <param name="mixins">The mixins.</param>
    /// <param name="generatedAt">The timestamp to record.</param>
    public static Lockfile Build(IEnumerable<RawMixin> mixins, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(mixins);

        Lockfile lockfile = new() { GeneratedAt = generatedAt };

        foreach (RawMixin mixin in mixins)
        {
            lockfile.Mixins[mixin.Key] = new LockEntry
            {
                Entity = mixin.Entity,
                Version = mixin.Version,
                Url = mixin.Url,
                Sha256 = Hash(mixin.Schema),
            };
        }

        return lockfile;
    }

    /// <summary>
    /// What differs between a recorded state and a live one.
    /// </summary>
    /// <param name="recorded">The lockfile on disk, or <c>null</c> when absent.</param>
    /// <param name="live">What the tenant reports now.</param>
    /// <returns>One line per difference; empty when in sync.</returns>
    public static IReadOnlyList<string> Diff(Lockfile? recorded, Lockfile live)
    {
        ArgumentNullException.ThrowIfNull(live);

        SortedDictionary<string, LockEntry> before =
            recorded?.Mixins ?? new SortedDictionary<string, LockEntry>(StringComparer.Ordinal);
        List<string> drift = [];

        foreach (string key in before.Keys.Union(live.Mixins.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool had = before.TryGetValue(key, out LockEntry? was);
            bool has = live.Mixins.TryGetValue(key, out LockEntry? now);

            if (!had)
            {
                drift.Add($"{key}: added at v{now!.Version}");
            }
            else if (!has)
            {
                drift.Add($"{key}: removed, was v{was!.Version}");
            }
            else if (was!.Version != now!.Version)
            {
                drift.Add(FormattableString.Invariant($"{key}: v{was.Version} to v{now.Version}"));
            }
            else if (!string.Equals(was.Url, now.Url, StringComparison.Ordinal))
            {
                drift.Add($"{key}: url changed at v{now.Version}");
            }
            else if (!string.Equals(was.Sha256, now.Sha256, StringComparison.Ordinal))
            {
                drift.Add($"{key}: schema changed without a version bump, still v{now.Version}");
            }
        }

        return drift;
    }

    /// <summary>Reads a lockfile, or <c>null</c> when there is none.</summary>
    /// <param name="path">The file.</param>
    public static Lockfile? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.Exists(path)
            ? JsonSerializer.Deserialize<Lockfile>(File.ReadAllText(path), MixinJson.Options)
            : null;
    }

    /// <summary>Writes a lockfile, creating its directory.</summary>
    /// <param name="path">The file.</param>
    /// <param name="lockfile">What to write.</param>
    public static void Write(string path, Lockfile lockfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(lockfile);

        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(lockfile, MixinJson.Options));
    }

    private static string Hash(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: 10 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Viu.Emporix.MixinSync tests/Viu.Emporix.Tests
git commit -m "feat: track mixin schema state in a lockfile

Version, url and a content hash per mixin. The hash matters because Emporix
can change a schema without raising its version, and then nothing else
differs. The timestamp is excluded from the comparison so a check does not
fail on every run.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 8: The attribute fallback

**Files:**
- Create: `src/Viu.Emporix.MixinSync/AttributeSchema.cs`
- Modify: `tests/Viu.Emporix.Tests/MixinSyncTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `AttributeSchema.FromAttributes(IEnumerable<SchemaAttribute>)` returning `string`, the JSON Schema text.

**Why:** when `metadata.url` cannot be fetched, the Schema Service's own `attributes[]` is the only description available. All eleven `SchemaAttributeType` values must be handled: `TEXT NUMBER DECIMAL BOOLEAN DATE TIME DATE_TIME ENUM ARRAY OBJECT REFERENCE`. Verified from `src/Viu.Emporix/Generated/Schema.cs`.

- [ ] **Step 1: Write the failing tests**

Append to `MixinSyncTests.cs`:

```csharp
    [Fact]
    public void Scalar_attributes_convert_to_json_schema_types()
    {
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute { Key = "title", Type = SchemaAttributeType.TEXT },
            new SchemaAttribute { Key = "weight", Type = SchemaAttributeType.DECIMAL },
            new SchemaAttribute { Key = "count", Type = SchemaAttributeType.NUMBER },
            new SchemaAttribute { Key = "active", Type = SchemaAttributeType.BOOLEAN },
        ]);

        Assert.Contains("""

            "title": {
              "type": "string"
            """.Trim(), schema.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("\"active\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"boolean\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Date_and_time_attributes_carry_a_format()
    {
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute { Key = "from", Type = SchemaAttributeType.DATE },
            new SchemaAttribute { Key = "at", Type = SchemaAttributeType.DATE_TIME },
            new SchemaAttribute { Key = "clock", Type = SchemaAttributeType.TIME },
        ]);

        Assert.Contains("\"date\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"date-time\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"time\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void An_enum_attribute_carries_its_values()
    {
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute
            {
                Key = "packaging",
                Type = SchemaAttributeType.ENUM,
                Values = [new SchemaAttributeValue { Value = "Paper" }, new SchemaAttributeValue { Value = "Plastic" }],
            },
        ]);

        Assert.Contains("\"enum\"", schema, StringComparison.Ordinal);
        Assert.Contains("Paper", schema, StringComparison.Ordinal);
        Assert.Contains("Plastic", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_object_attribute_recurses()
    {
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute
            {
                Key = "note",
                Type = SchemaAttributeType.OBJECT,
                Attributes = [new SchemaAttribute { Key = "en", Type = SchemaAttributeType.TEXT }],
            },
        ]);

        Assert.Contains("\"note\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"en\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void A_required_attribute_is_listed_as_required()
    {
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute
            {
                Key = "title",
                Type = SchemaAttributeType.TEXT,
                Metadata = new SchemaAttributeMetadata { Required = true },
            },
            new SchemaAttribute { Key = "subtitle", Type = SchemaAttributeType.TEXT },
        ]);

        Assert.Contains("\"required\"", schema, StringComparison.Ordinal);
        Assert.Contains("title", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_attribute_becomes_a_string()
    {
        // A reference is an id, and the tool has nothing to resolve it against.
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute { Key = "parent", Type = SchemaAttributeType.REFERENCE },
        ]);

        Assert.Contains("\"string\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_attribute_type_produces_something_parseable()
    {
        // The guard against a new enum value arriving unhandled: eleven values
        // exist today, all must convert into a schema NJsonSchema can read.
        foreach (SchemaAttributeType type in Enum.GetValues<SchemaAttributeType>())
        {
            string schema = AttributeSchema.FromAttributes([
                new SchemaAttribute { Key = "field", Type = type },
            ]);

            using JsonDocument parsed = JsonDocument.Parse(schema);
            Assert.True(parsed.RootElement.TryGetProperty("properties", out _), $"{type} produced no properties");
        }
    }
```

Add `using System.Text.Json;` and `using Viu.Emporix.SchemaModels;` to the file's usings.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: compile failure — `AttributeSchema` does not exist.

- [ ] **Step 3: Write the conversion**

Create `src/Viu.Emporix.MixinSync/AttributeSchema.cs`:

```csharp
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
/// <see cref="SchemaAttributeType"/> enum makes an unhandled value a compiler
/// concern rather than a silent fall-through.
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

    private static JsonNode Property(SchemaAttribute attribute) => attribute.Type switch
    {
        SchemaAttributeType.TEXT => new JsonObject { ["type"] = "string" },
        SchemaAttributeType.NUMBER => new JsonObject { ["type"] = "number" },
        SchemaAttributeType.DECIMAL => new JsonObject { ["type"] = "number" },
        SchemaAttributeType.BOOLEAN => new JsonObject { ["type"] = "boolean" },
        SchemaAttributeType.DATE => new JsonObject { ["type"] = "string", ["format"] = "date" },
        SchemaAttributeType.TIME => new JsonObject { ["type"] = "string", ["format"] = "time" },
        SchemaAttributeType.DATE_TIME => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
        SchemaAttributeType.ENUM => Enumeration(attribute),
        SchemaAttributeType.ARRAY => Array(attribute),
        SchemaAttributeType.OBJECT => Object(attribute.Attributes ?? []),

        // A reference is an id, and there is nothing here to resolve it against.
        SchemaAttributeType.REFERENCE => new JsonObject { ["type"] = "string" },

        // Deliberately permissive rather than throwing: a new attribute type
        // upstream should degrade to «any» and still generate, not stop the run.
        _ => new JsonObject(),
    };

    private static JsonNode Enumeration(SchemaAttribute attribute)
    {
        JsonArray values = [];

        foreach (SchemaAttributeValue value in attribute.Values ?? [])
        {
            values.Add(value.Value);
        }

        return new JsonObject { ["type"] = "string", ["enum"] = values };
    }

    private static JsonNode Array(SchemaAttribute attribute)
    {
        // arrayType names the element type; absent, a string array is the
        // Schema Service's own default.
        JsonNode items = attribute.ArrayType is { } element
            && Enum.TryParse(element.ToString(), out SchemaAttributeType parsed)
            ? Property(new SchemaAttribute { Key = attribute.Key, Type = parsed })
            : new JsonObject { ["type"] = "string" };

        return new JsonObject { ["type"] = "array", ["items"] = items };
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: 17 passed.

If `SchemaAttribute.ArrayType` turns out to be typed as something other than an enum or string, adjust `Array` to match what `src/Viu.Emporix/Generated/Schema.cs` actually declares — check it rather than guessing, and keep the string-array default.

- [ ] **Step 5: Commit**

```bash
git add src/Viu.Emporix.MixinSync tests/Viu.Emporix.Tests
git commit -m "feat: convert schema attributes to json schema as a fallback

Used only when a schema's hosted url cannot be fetched. All eleven attribute
types are handled, and an unknown twelfth degrades to an open object so a new
type upstream does not stop a generation run.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Task 9: Fix the schema listing, which drops every page but the first

**Files:**
- Modify: `src/Viu.Emporix/SchemaService.cs:57-68`
- Test: `tests/Viu.Emporix.Tests/MixinSyncTests.cs`

**This is a defect in the shipped package, found by reading the specification against the code.** `specs/schema.yml`, operation `GET-schema-retrieve-schemas`, declares `trait_paged_pageNumber`, `trait_paged_pageSize`, `trait_sort` and `trait_q_param`. `SchemaService.ListAsync` sends none of them and returns `IReadOnlyList<SchemaResponse>`, so a tenant with more schemas than the endpoint's default page size silently yields a partial list — and the generator would then emit too few mixins without saying so.

Every other paginated facade in this SDK follows one shape, for example `ApprovalService.ListAsync:70`:

```csharp
public async Task<PaginatedItems<T>> ListAsync(
    string? query = null, int pageNumber = 1, int pageSize = 60,
    AuthContext auth = default, CancellationToken cancellationToken = default)
```

**Interfaces:**
- Produces: `SchemaService.ListAsync(string? query = null, int pageNumber = 1, int pageSize = 60, AuthContext auth = default, CancellationToken cancellationToken = default)` returning `Task<PaginatedItems<SchemaResponse>>`.

**This changes the return type**, so it is a breaking change and the commit is `feat!`. Before 1.0 that is a minor bump. Do not add a second overload instead: two public overloads with optional parameters is `RS0026`, and CLAUDE.md says rename rather than suppress — but here there is nothing to rename, the existing signature is simply wrong.

- [ ] **Step 1: Write the failing test**

Append to `MixinSyncTests.cs`, using the repository's existing stub handler from `TestDoubles.cs`:

```csharp
    [Fact]
    public async Task The_schema_listing_sends_paging_parameters()
    {
        // The specification declares pageNumber and pageSize on
        // GET-schema-retrieve-schemas. Without them a tenant with many schemas
        // yields one page and the generator emits too few mixins.
        Uri? requested = null;
        StubHandler handler = new(request =>
        {
            requested = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });

        EmporixClient client = TestClient.Create(handler);

        await client.Schemas.ListAsync(pageNumber: 3, pageSize: 200);

        Assert.Contains("pageNumber=3", requested?.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=200", requested?.Query, StringComparison.Ordinal);
    }
```

Read `tests/Viu.Emporix.Tests/TestDoubles.cs` first and use whatever the stub and client-construction helpers there are actually called — the names above are placeholders for that file's real ones, and this is the one place in this plan where you must look before writing.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "The_schema_listing_sends_paging_parameters"`
Expected: compile failure — `ListAsync` has no `pageNumber` parameter.

- [ ] **Step 3: Fix the facade**

Replace the body of `ListAsync` in `src/Viu.Emporix/SchemaService.cs`:

```csharp
    /// <summary>Lists the schemas.</summary>
    /// <param name="query">An Emporix <c>q</c> filter, or nothing.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// This shipped returning a bare list and sending no paging parameters,
    /// which the specification declares. A tenant with more schemas than one
    /// page then reported a partial list with nothing to signal it.
    /// </remarks>
    public async Task<PaginatedItems<SchemaResponse>> ListAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
        ];

        if (query is { Length: > 0 })
        {
            parameters.Add(new KeyValuePair<string, string?>("q", query));
        }

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = parameters,
            },
            SchemaJsonContext.Default.ListSchemaResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }
```

Compare against `ApprovalService.ListAsync:70` and match its `SendPageAsync` call exactly — argument order and the header handling for `X-Total-Count` if that facade sets one.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: all pass. Other tests calling `Schemas.ListAsync` need updating for the new return type — `PaginatedItems<T>` exposes the items, so the fix is usually one property access.

- [ ] **Step 5: Record the public API and commit**

```bash
./scripts/update-public-api.sh && dotnet build
git add src/Viu.Emporix tests/Viu.Emporix.Tests
git commit -m "feat!: page the schema listing

GET-schema-retrieve-schemas declares pageNumber, pageSize, sort and q in
specs/schema.yml. The facade sent none of them and returned a bare list, so a
tenant with more schemas than one page reported a partial answer with nothing
to signal it. Now shaped like every other paginated facade here.

BREAKING CHANGE: SchemaService.ListAsync returns PaginatedItems rather than
IReadOnlyList.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 10: Pull from the Schema Service

**Files:**
- Create: `src/Viu.Emporix.MixinSync/SchemaSource.cs`
- Modify: `src/Viu.Emporix.MixinSync/Program.cs`
- Modify: `tests/Viu.Emporix.Tests/MixinSyncTests.cs`

**Interfaces:**
- Consumes: `RawMixin` from Task 6, `Lockfile` from Task 7, `AttributeSchema.FromAttributes` from Task 8, `SchemaService.ListAsync` from Task 9.
- Produces: `SchemaSource(EmporixClient client, HttpClient http)` with `Task<IReadOnlyList<RawMixin>> ListAsync(CancellationToken)`; `SchemaSource.ToRawMixins(IEnumerable<SchemaResponse>, Func<string, Task<string?>>)` as the testable core.

- [ ] **Step 1: Write the failing tests**

Append to `MixinSyncTests.cs`:

```csharp
    [Fact]
    public async Task A_schema_becomes_one_raw_mixin_per_entity_type()
    {
        // One schema assigned to PRODUCT and CATEGORY is two descriptors, as in
        // the Node SDK: the entity is part of a mixin's identity for the caller.
        SchemaResponse schema = new()
        {
            Id = "deliveryOptions",
            Types = [SchemaType.PRODUCT, SchemaType.CATEGORY],
            Metadata = new SchemaMetadata { Version = 6, Url = "https://cdn/d.v6.json" },
        };

        IReadOnlyList<RawMixin> mixins = await SchemaSource.ToRawMixins(
            [schema], _ => Task.FromResult<string?>("""{"type":"object"}"""));

        Assert.Equal(2, mixins.Count);
        Assert.Equal(["CATEGORY", "PRODUCT"], mixins.Select(m => m.Entity).Order().ToArray());
        Assert.All(mixins, m => Assert.Equal(6, m.Version));
    }

    [Fact]
    public async Task A_schema_without_an_id_version_or_url_is_skipped()
    {
        SchemaResponse[] unusable =
        [
            new() { Id = null, Types = [SchemaType.PRODUCT], Metadata = new SchemaMetadata { Version = 1, Url = "u" } },
            new() { Id = "a", Types = [SchemaType.PRODUCT], Metadata = new SchemaMetadata { Version = null, Url = "u" } },
            new() { Id = "b", Types = [SchemaType.PRODUCT], Metadata = new SchemaMetadata { Version = 1, Url = null } },
        ];

        Assert.Empty(await SchemaSource.ToRawMixins(unusable, _ => Task.FromResult<string?>("{}")));
    }

    [Fact]
    public async Task A_schema_whose_url_cannot_be_fetched_falls_back_to_its_attributes()
    {
        SchemaResponse schema = new()
        {
            Id = "deliveryOptions",
            Types = [SchemaType.PRODUCT],
            Metadata = new SchemaMetadata { Version = 6, Url = "https://cdn/gone.v6.json" },
            Attributes = [new SchemaAttribute { Key = "packaging", Type = SchemaAttributeType.TEXT }],
        };

        IReadOnlyList<RawMixin> mixins = await SchemaSource.ToRawMixins(
            [schema], _ => Task.FromResult<string?>(null));

        Assert.Single(mixins);
        Assert.Contains("packaging", mixins[0].Schema, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: compile failure — `SchemaSource` does not exist.

- [ ] **Step 3: Write the source**

Create `src/Viu.Emporix.MixinSync/SchemaSource.cs`:

```csharp
using Viu.Emporix;
using Viu.Emporix.SchemaModels;

namespace Viu.Emporix.MixinSync;

/// <summary>
/// Reads a tenant's mixins from its Schema Service.
/// </summary>
/// <remarks>
/// The hosted schema at <c>metadata.url</c> is authoritative and is fetched
/// first. Only when that fails does the Schema Service's own attribute model
/// stand in, which is a lossier description of the same thing.
/// </remarks>
public sealed class SchemaSource
{
    private const int PageSize = 100;

    private readonly EmporixClient _client;
    private readonly HttpClient _http;

    /// <summary>Reads from one tenant.</summary>
    /// <param name="client">A client configured for that tenant.</param>
    /// <param name="http">Used for the schema URLs, which need no Emporix token.</param>
    public SchemaSource(EmporixClient client, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(http);

        _client = client;
        _http = http;
    }

    /// <summary>Every mixin the tenant has, across all pages.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<RawMixin>> ListAsync(CancellationToken cancellationToken = default)
    {
        // PaginatedItems.EnumerateAllAsync already walks every page and
        // terminates on HasNextPage. Rolling that loop by hand here would
        // duplicate logic the SDK maintains, including its documented
        // one-extra-empty-request case.
        List<SchemaResponse> all = [];

        await foreach (SchemaResponse schema in PaginatedItems.EnumerateAllAsync(
            (page, token) => _client.Schemas.ListAsync(
                pageNumber: page, pageSize: PageSize, cancellationToken: token),
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            all.Add(schema);
        }

        return await ToRawMixins(all, FetchAsync).ConfigureAwait(false);

        async Task<string?> FetchAsync(string url)
        {
            try
            {
                using HttpResponseMessage response = await _http
                    .GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);

                return response.IsSuccessStatusCode
                    ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                    : null;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Turns schemas into mixins, one per assigned entity type.
    /// </summary>
    /// <param name="schemas">What the Schema Service returned.</param>
    /// <param name="fetch">Fetches a schema URL, or returns null on failure.</param>
    /// <remarks>
    /// Separated from the HTTP so it can be tested without a tenant.
    /// </remarks>
    public static async Task<IReadOnlyList<RawMixin>> ToRawMixins(
        IEnumerable<SchemaResponse> schemas,
        Func<string, Task<string?>> fetch)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(fetch);

        List<RawMixin> mixins = [];

        foreach (SchemaResponse schema in schemas)
        {
            string? key = schema.Id;
            double? version = schema.Metadata?.Version;
            string? url = schema.Metadata?.Url;

            // Without all three there is nothing to generate from and nothing to
            // record in metadata.mixins, so the schema is not usable as a mixin.
            if (string.IsNullOrWhiteSpace(key) || version is null || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            string body = await fetch(url).ConfigureAwait(false)
                ?? AttributeSchema.FromAttributes(schema.Attributes ?? []);

            foreach (SchemaType type in schema.Types ?? [])
            {
                mixins.Add(new RawMixin
                {
                    Key = key,
                    Entity = type.ToString(),
                    Version = (int)version.Value,
                    Url = url,
                    Schema = body,
                });
            }
        }

        return mixins;
    }
}
```

- [ ] **Step 4: Wire up `pull`**

Replace the `PullAsync` placeholder in `Program.cs`:

```csharp
static async Task<int> PullAsync(MixinConfig config, string configPath)
{
    string root = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

    string? clientId = Environment.GetEnvironmentVariable("EMPORIX_BACKEND_CLIENT_ID");
    string? secret = Environment.GetEnvironmentVariable("EMPORIX_BACKEND_SECRET");

    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
    {
        throw new InvalidOperationException(
            "Set EMPORIX_BACKEND_CLIENT_ID and EMPORIX_BACKEND_SECRET. The Schema Service is seller-side.");
    }

    EmporixOptions options = new() { Tenant = config.Tenant };
    options.Credentials.Backend = new EmporixServiceCredentials { ClientId = clientId, Secret = secret };

    using EmporixClient client = new(options);
    using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    IReadOnlyList<RawMixin> mixins = await new SchemaSource(client, http).ListAsync();

    string lockPath = Path.Combine(root, config.LockFile);
    string snapshotPath = Path.Combine(
        Path.GetDirectoryName(lockPath) ?? root, "mixins.snapshot.json");

    Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
    File.WriteAllText(snapshotPath, JsonSerializer.Serialize(mixins, MixinJson.Options));
    Lockfile.Write(lockPath, Lockfile.Build(mixins, DateTimeOffset.UtcNow));

    Console.WriteLine($"Pulled {mixins.Count} mixins into {snapshotPath} and {lockPath}.");

    return 0;
}
```

Add `using System.Text.Json;` and `using Viu.Emporix;` to `Program.cs`. If `EmporixClient` is not `IDisposable`, drop the `using` on it — check the class rather than assuming.

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: 21 passed.

- [ ] **Step 6: Commit**

```bash
git add src/Viu.Emporix.MixinSync tests/Viu.Emporix.Tests
git commit -m "feat: pull mixins from a tenant's schema service

The hosted schema url is authoritative and is fetched first; the attribute
model stands in only when that fails. A schema assigned to several entity
types yields one mixin each, and one missing an id, version or url is skipped
because none of it can be generated from.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 11: Generate

**Files:**
- Create: `src/Viu.Emporix.MixinSync/Generator.cs`
- Modify: `src/Viu.Emporix.MixinSync/Program.cs`
- Modify: `tests/Viu.Emporix.Tests/MixinSyncTests.cs`

**Interfaces:**
- Consumes: `RawMixin` from Task 6.
- Produces: `Generator.Generate(IEnumerable<RawMixin>, string rootNamespace)` returning `IReadOnlyDictionary<string, string>` — file name to content, including `Registry.g.cs`. Throws `InvalidOperationException` on an attribute-name collision.

**The three collisions this must handle**, all reproduced against NJsonSchema 11.6.1:

1. Two mixins each declaring a `note` object emit two `partial class Note`, which merge instead of clashing — a wrong type that compiles. **One namespace per mixin** — `{root}.{PascalKey}` — resolves it.
2. One shared `JsonSerializerContext` over both hits `SYSLIB1031`, an error under warnings-as-errors. **One context per mixin.**
3. `x-custom` and `xCustom` both emit `XCustom`, giving `CS0102` in code the consumer cannot fix. **Detect and refuse**, naming both attributes.

- [ ] **Step 1: Write the failing tests**

Append to `MixinSyncTests.cs`:

```csharp
    [Fact]
    public void Each_mixin_gets_its_own_namespace_and_context()
    {
        // Two mixins both declaring «note» would otherwise emit two Note classes
        // into one namespace, where they merge rather than clash.
        IReadOnlyDictionary<string, string> files = Generator.Generate(
        [
            new RawMixin { Key = "delivery", Entity = "PRODUCT", Version = 6, Url = "https://cdn/d.v6.json",
                Schema = """{"type":"object","properties":{"note":{"type":"object","properties":{"en":{"type":"string"}}}}}""" },
            new RawMixin { Key = "warranty", Entity = "PRODUCT", Version = 2, Url = "https://cdn/w.v2.json",
                Schema = """{"type":"object","properties":{"note":{"type":"object","properties":{"en":{"type":"string"}}}}}""" },
        ], "Acme.Mixins");

        Assert.Contains("namespace Acme.Mixins.Delivery", files["Delivery.g.cs"], StringComparison.Ordinal);
        Assert.Contains("namespace Acme.Mixins.Warranty", files["Warranty.g.cs"], StringComparison.Ordinal);
        Assert.Contains("DeliveryContext", files["Delivery.g.cs"], StringComparison.Ordinal);
        Assert.Contains("WarrantyContext", files["Warranty.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_registry_binds_every_mixin_to_a_descriptor()
    {
        IReadOnlyDictionary<string, string> files = Generator.Generate(
        [
            new RawMixin { Key = "delivery", Entity = "PRODUCT", Version = 6, Url = "https://cdn/d.v6.json",
                Schema = """{"type":"object","properties":{"packaging":{"type":"string"}}}""" },
        ], "Acme.Mixins");

        string registry = files["Registry.g.cs"];

        Assert.Contains("MixinDescriptor<", registry, StringComparison.Ordinal);
        Assert.Contains("\"delivery\"", registry, StringComparison.Ordinal);
        Assert.Contains("\"PRODUCT\"", registry, StringComparison.Ordinal);
        Assert.Contains("Version = 6", registry, StringComparison.Ordinal);
        Assert.Contains("[\"Packaging\"] = \"packaging\"", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_attributes_normalising_to_one_name_are_refused_by_name()
    {
        // NJsonSchema appends no disambiguating suffix, so both emit XCustom and
        // the consumer receives code that does not compile, with a diagnostic
        // naming nothing about Emporix.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Generator.Generate(
        [
            new RawMixin { Key = "attrs", Entity = "PRODUCT", Version = 1, Url = "https://cdn/a.v1.json",
                Schema = """{"type":"object","properties":{"x-custom":{"type":"string"},"xCustom":{"type":"string"}}}""" },
        ], "Acme.Mixins"));

        Assert.Contains("x-custom", error.Message, StringComparison.Ordinal);
        Assert.Contains("xCustom", error.Message, StringComparison.Ordinal);
        Assert.Contains("attrs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_that_is_not_an_identifier_still_produces_a_valid_type()
    {
        // Emporix schema ids can be object ids, which cannot start a C# name.
        IReadOnlyDictionary<string, string> files = Generator.Generate(
        [
            new RawMixin { Key = "68e27d7a68ce91215abc0f23", Entity = "PRODUCT", Version = 1, Url = "https://cdn/x.v1.json",
                Schema = """{"type":"object","properties":{"a":{"type":"string"}}}""" },
        ], "Acme.Mixins");

        Assert.Single(files.Keys.Where(k => k != "Registry.g.cs"));
        Assert.Contains("\"68e27d7a68ce91215abc0f23\"", files["Registry.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_contexts_skip_null_attributes()
    {
        IReadOnlyDictionary<string, string> files = Generator.Generate(
        [
            new RawMixin { Key = "delivery", Entity = "PRODUCT", Version = 6, Url = "https://cdn/d.v6.json",
                Schema = """{"type":"object","properties":{"packaging":{"type":"string"}}}""" },
        ], "Acme.Mixins");

        Assert.Contains("WhenWritingNull", files["Delivery.g.cs"], StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: compile failure — `Generator` does not exist.

- [ ] **Step 3: Write the generator**

Create `src/Viu.Emporix.MixinSync/Generator.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;

namespace Viu.Emporix.MixinSync;

/// <summary>
/// Turns mixin schemas into C# types, serializer contexts and a registry.
/// </summary>
/// <remarks>
/// Uses NJsonSchema as a library rather than shelling out to the nswag CLI, so
/// the namespace can be set per mixin and the consumer needs no tool manifest.
/// </remarks>
public static partial class Generator
{
    private const string Banner = "// AUTO-GENERATED by Viu.Emporix.MixinSync — do not edit.";

    /// <summary>Generates every file for a set of mixins.</summary>
    /// <param name="mixins">The mixins, as pulled.</param>
    /// <param name="rootNamespace">The namespace the generated code goes under.</param>
    /// <returns>File name to content, including <c>Registry.g.cs</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Two attributes of one mixin normalise to the same C# name.
    /// </exception>
    public static IReadOnlyDictionary<string, string> Generate(
        IEnumerable<RawMixin> mixins,
        string rootNamespace)
    {
        ArgumentNullException.ThrowIfNull(mixins);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootNamespace);

        Dictionary<string, string> files = new(StringComparer.Ordinal);
        List<string> registryEntries = [];

        // A schema assigned to several entity types arrives several times; the
        // generated type is the same, so emit it once and let the registry carry
        // one descriptor per entity.
        foreach (IGrouping<string, RawMixin> group in mixins.GroupBy(m => m.Key, StringComparer.Ordinal))
        {
            RawMixin mixin = group.First();
            string name = TypeName(mixin);
            string mixinNamespace = $"{rootNamespace}.{Identifier(mixin.Key)}";

            string code = Emit(mixin, name, mixinNamespace);
            IReadOnlyDictionary<string, string> attributes = AttributeTable(code, mixin.Key);

            files[$"{Identifier(mixin.Key)}.g.cs"] = code;

            foreach (RawMixin perEntity in group)
            {
                registryEntries.Add(RegistryEntry(perEntity, name, mixinNamespace, attributes));
            }
        }

        files["Registry.g.cs"] = Registry(rootNamespace, registryEntries);

        return files;
    }

    private static string Emit(RawMixin mixin, string name, string mixinNamespace)
    {
        JsonSchema schema = JsonSchema.FromJsonAsync(mixin.Schema).GetAwaiter().GetResult();

        string types = new CSharpGenerator(schema, new CSharpGeneratorSettings
        {
            Namespace = mixinNamespace,
            ClassStyle = CSharpClassStyle.Poco,
            JsonLibrary = CSharpJsonLibrary.SystemTextJson,
            GenerateDataAnnotations = false,
            GenerateNullableReferenceTypes = true,
            GenerateOptionalPropertiesAsNullable = true,
        }).GenerateFile(name);

        // One context per mixin, without exception: Emporix reuses attribute
        // names across schemas, and a shared context collides on same-named
        // nested types with SYSLIB1031 — an error under warnings-as-errors.
        // WhenWritingNull because a schema with additionalProperties:false has
        // no use for an explicit null.
        StringBuilder context = new();
        context.AppendLine();
        context.AppendLine($"namespace {mixinNamespace}");
        context.AppendLine("{");
        context.AppendLine("    [System.Text.Json.Serialization.JsonSourceGenerationOptions(");
        context.AppendLine("        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]");
        context.AppendLine($"    [System.Text.Json.Serialization.JsonSerializable(typeof({name}))]");
        context.AppendLine($"    public sealed partial class {Identifier(NameOf(name))}Context");
        context.AppendLine("        : System.Text.Json.Serialization.JsonSerializerContext;");
        context.AppendLine("}");

        return $"{Banner}{Environment.NewLine}{types}{context}";
    }

    private static string RegistryEntry(
        RawMixin mixin,
        string typeName,
        string mixinNamespace,
        IReadOnlyDictionary<string, string> attributes)
    {
        string member = Identifier(mixin.Key) + (mixin.Entity is "PRODUCT" ? string.Empty : $"On{Identifier(mixin.Entity)}");
        string table = string.Join(
            ", ",
            attributes.Select(pair => $"[\"{pair.Key}\"] = \"{pair.Value}\""));

        return $$"""
                /// <summary>The «{{mixin.Key}}» mixin on {{mixin.Entity}}, schema version {{mixin.Version}}.</summary>
                public static readonly Viu.Emporix.Mixins.MixinDescriptor<{{mixinNamespace}}.{{typeName}}> {{member}} = new()
                {
                    Key = "{{mixin.Key}}",
                    Entity = "{{mixin.Entity}}",
                    Url = "{{mixin.Url}}",
                    Version = {{mixin.Version.ToString(CultureInfo.InvariantCulture)}},
                    TypeInfo = {{mixinNamespace}}.{{Identifier(NameOf(typeName))}}Context.Default.{{typeName}},
                    Attributes = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal) { {{table}} },
                };
        """;
    }

    private static string Registry(string rootNamespace, IEnumerable<string> entries)
        => $$"""
            {{Banner}}
            namespace {{rootNamespace}}
            {
                /// <summary>This tenant's mixins.</summary>
                public static class Mixins
                {
            {{string.Join(Environment.NewLine + Environment.NewLine, entries)}}
                }
            }
            """;

    // Parsed out of the emitted code rather than recomputed. The conversion and
    // the emitted result can diverge, and the Node package tripped on exactly
    // that — its comment reads «reference the name it ACTUALLY emitted».
    private static IReadOnlyDictionary<string, string> AttributeTable(string code, string mixinKey)
    {
        Dictionary<string, string> table = new(StringComparer.Ordinal);
        Dictionary<string, string> seen = new(StringComparer.Ordinal);

        foreach (Match match in PropertyPattern().Matches(code))
        {
            string json = match.Groups["json"].Value;
            string clr = match.Groups["clr"].Value;

            if (seen.TryGetValue(clr, out string? first) && !string.Equals(first, json, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The mixin \"{mixinKey}\" has attributes \"{first}\" and \"{json}\", which both become the C# name {clr}. The generated code would not compile. Rename one of them in the schema.");
            }

            seen[clr] = json;
            table[clr] = json;
        }

        return table;
    }

    private static string TypeName(RawMixin mixin)
        => $"{Identifier(mixin.Key)}MixinV{mixin.Version.ToString(CultureInfo.InvariantCulture)}";

    // NJsonSchema renormalises the name it is given, so the emitted type may
    // differ from the one requested. Everything downstream must use what came
    // back, which is why the registry reads the name out of the code.
    private static string NameOf(string typeName) => typeName;

    /// Emporix schema ids can be object ids, which cannot start a C# name.
    private static string Identifier(string value)
    {
        StringBuilder builder = new();
        bool upper = true;

        foreach (char character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }

        string identifier = builder.ToString();

        return identifier.Length == 0 || !char.IsLetter(identifier[0])
            ? $"Mixin{identifier}"
            : identifier;
    }

    [GeneratedRegex(
        """JsonPropertyName\("(?<json>[^"]+)"\)\]\s*public\s+[^\s]+\s+(?<clr>\w+)\s*\{""",
        RegexOptions.Singleline)]
    private static partial Regex PropertyPattern();
}
```

- [ ] **Step 4: Wire up `generate`**

Replace the `Generate` placeholder in `Program.cs`:

```csharp
static int Generate(MixinConfig config, string configPath)
{
    string root = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();
    string lockPath = Path.Combine(root, config.LockFile);
    string snapshotPath = Path.Combine(Path.GetDirectoryName(lockPath) ?? root, "mixins.snapshot.json");

    if (!File.Exists(snapshotPath))
    {
        throw new FileNotFoundException($"No snapshot at {snapshotPath}. Run pull first.", snapshotPath);
    }

    List<RawMixin> mixins = JsonSerializer.Deserialize<List<RawMixin>>(
        File.ReadAllText(snapshotPath), MixinJson.Options) ?? [];

    string outputDirectory = Path.Combine(root, config.Out);
    Directory.CreateDirectory(outputDirectory);

    // A mixin removed from the tenant must not leave an orphaned file behind,
    // the same reasoning SpecSync applies to the generated specifications.
    foreach (string stale in Directory.GetFiles(outputDirectory, "*.g.cs"))
    {
        File.Delete(stale);
    }

    foreach ((string name, string content) in Generator.Generate(mixins, config.Namespace))
    {
        File.WriteAllText(Path.Combine(outputDirectory, name), content);
    }

    Console.WriteLine($"Generated {mixins.Count} mixins into {outputDirectory}.");

    return 0;
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~MixinSyncTests"`
Expected: 26 passed.

The registry's `TypeInfo` line and the context class name must agree. If a test shows they do not, fix it by reading the emitted type name out of `code` with a `export`-style regex on `public partial class (\w+)` and using that everywhere, rather than by adjusting `NameOf`.

- [ ] **Step 6: Commit**

```bash
git add src/Viu.Emporix.MixinSync tests/Viu.Emporix.Tests
git commit -m "feat: generate typed mixins with one namespace and context each

One namespace per mixin, because two schemas each declaring a note object
would otherwise emit two Note classes into one namespace. One serializer
context per mixin, because a shared one collides on same-named nested types.
Two attributes normalising to the same C# name are refused by name rather
than emitted as code that cannot compile.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 12: Check

**Files:**
- Modify: `src/Viu.Emporix.MixinSync/Program.cs`

**Interfaces:**
- Consumes: `SchemaSource.ListAsync` from Task 10, `Lockfile.Build`, `Lockfile.Read` and `Lockfile.Diff` from Task 7.
- Produces: no new API — `check` exits 0 in sync, 1 on drift.

- [ ] **Step 1: Replace the placeholder**

```csharp
static async Task<int> CheckAsync(MixinConfig config, string configPath)
{
    string root = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

    string? clientId = Environment.GetEnvironmentVariable("EMPORIX_BACKEND_CLIENT_ID");
    string? secret = Environment.GetEnvironmentVariable("EMPORIX_BACKEND_SECRET");

    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
    {
        throw new InvalidOperationException(
            "Set EMPORIX_BACKEND_CLIENT_ID and EMPORIX_BACKEND_SECRET. The Schema Service is seller-side.");
    }

    EmporixOptions options = new() { Tenant = config.Tenant };
    options.Credentials.Backend = new EmporixServiceCredentials { ClientId = clientId, Secret = secret };

    using EmporixClient client = new(options);
    using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    IReadOnlyList<RawMixin> live = await new SchemaSource(client, http).ListAsync();
    IReadOnlyList<string> drift = Lockfile.Diff(
        Lockfile.Read(Path.Combine(root, config.LockFile)),
        Lockfile.Build(live, DateTimeOffset.UtcNow));

    if (drift.Count == 0)
    {
        Console.WriteLine($"In sync: {live.Count} mixins match the lockfile.");
        return 0;
    }

    Console.Error.WriteLine($"Drift against {config.LockFile}:");

    foreach (string line in drift)
    {
        Console.Error.WriteLine($"  {line}");
    }

    Console.Error.WriteLine();
    Console.Error.WriteLine("Run pull and generate, then review the type diff before committing.");

    return 1;
}
```

- [ ] **Step 2: Verify the command surface**

Run: `dotnet run --project src/Viu.Emporix.MixinSync -- check`
Expected: exit 1 with «No configuration at …» — the config is missing, which is the intended message, not a stack trace.

Run: `dotnet build`
Expected: 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/Viu.Emporix.MixinSync
git commit -m "feat: detect mixin schema drift for ci

check compares the tenant against the lockfile and exits non-zero on any
difference, so a scheduled workflow can raise a pull request when Emporix
assigns a new schema version. Noticing that is the part nobody does by hand.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
## Phase 4 — Structural verification and integration

### Task 13: The generated code must compile

**Files:**
- Create: `tests/Viu.Emporix.Tests/MixinGeneratorCompilationTests.cs`
- Modify: `tests/Viu.Emporix.Tests/Viu.Emporix.Tests.csproj`
- Modify: `Directory.Packages.props`

**Interfaces:**
- Consumes: `Generator.Generate` from Task 11.
- Produces: no API. This is the plan's most valuable test.

**Why this one matters most.** No defect in this SDK has ever been found by a unit test — a stubbed handler asserts the same wrong call the code builds. `SpecPathTests` earns its place by checking a structural property instead of an expectation, and this is the same idea for generated code: compile it. All three collisions then fail the test without anyone having to predict them, and so does a fourth nobody has thought of yet.

- [ ] **Step 1: Add the Roslyn reference**

In `Directory.Packages.props`, add to the test dependencies group:

```xml
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />
```

Check the current version with `dotnet package search Microsoft.CodeAnalysis.CSharp` and take the latest stable that restores on .NET 10.

In `tests/Viu.Emporix.Tests/Viu.Emporix.Tests.csproj`:

```xml
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
```

- [ ] **Step 2: Write the test**

Create `tests/Viu.Emporix.Tests/MixinGeneratorCompilationTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Reflection;
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
/// that break it, and compile the result.
/// </para>
/// <para>
/// Three collisions are known and each has its own fixture below. The reason to
/// compile rather than to assert on the text is the fourth collision, which
/// nobody has thought of yet and which this catches anyway.
/// </para>
/// </remarks>
public class MixinGeneratorCompilationTests
{
    [Fact]
    public void Two_mixins_with_a_same_named_nested_object_compile()
    {
        // Without one namespace per mixin, two «partial class Note» merge.
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
    public void A_mixin_on_several_entities_compiles_with_one_descriptor_each()
    {
        IReadOnlyDictionary<string, string> files = Generator.Generate(
        [
            new RawMixin { Key = "shared", Entity = "PRODUCT", Version = 3, Url = "https://cdn/s.v3.json",
                Schema = """{"type":"object","additionalProperties":false,"properties":{"a":{"type":"string"}}}""" },
            new RawMixin { Key = "shared", Entity = "CATEGORY", Version = 3, Url = "https://cdn/s.v3.json",
                Schema = """{"type":"object","additionalProperties":false,"properties":{"a":{"type":"string"}}}""" },
        ], "Acme.Mixins");

        AssertCompiles(files);

        // One type, two descriptors — the entity is part of the caller's identity
        // for a mixin, but not of the generated shape.
        Assert.Equal(2, files.Keys.Count);
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
        => AssertCompiles(Generator.Generate(mixins, "Acme.Mixins"));

    private static void AssertCompiles(IReadOnlyDictionary<string, string> files)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratedMixins",
            files.Values.Select(source => CSharpSyntaxTree.ParseText(source)),
            ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();

        // SYSLIB1031 is a warning, not an error, but warnings are errors in this
        // repository — so a shared context would break a consumer's build and
        // must fail here too.
        Diagnostic[] fatal = [.. diagnostics.Where(d =>
            d.Severity == DiagnosticSeverity.Error
            || d.Id is "SYSLIB1031" or "SYSLIB1030")];

        Assert.True(
            fatal.Length == 0,
            $"The generated code does not compile:{Environment.NewLine}"
            + string.Join(Environment.NewLine, fatal.Select(d => $"  {d.Id}: {d.GetMessage()}")));
    }

    private static IEnumerable<MetadataReference> ReferenceAssemblies()
    {
        // Every assembly the test host already loaded covers the framework plus
        // Viu.Emporix itself, which the registry references.
        string directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        foreach (string name in new[] { "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Text.Json", "netstandard" })
        {
            string path = Path.Combine(directory, $"{name}.dll");

            if (File.Exists(path))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }

        yield return MetadataReference.CreateFromFile(typeof(MixinDescriptor<>).Assembly.Location);
    }
}
```

- [ ] **Step 3: Run the test**

Run: `dotnet test --filter "FullyQualifiedName~MixinGeneratorCompilationTests"`
Expected: 4 passed.

The source-generated context inside the emitted code will **not** produce its `Default` member in this compilation, because the STJ generator does not run here — so the registry's `TypeInfo = …Context.Default.X` line resolves to nothing and reports `CS0117`. Two acceptable resolutions, pick one and note which:

1. Add the STJ generator to the compilation with `.WithAnalyzers`, running the real source generator — the honest version, and the one that also catches `SYSLIB1031`.
2. Compile only the per-mixin type files and exclude `Registry.g.cs`, then assert separately that the registry's text references a type and a context that the type files declare.

Prefer 1. If wiring the generator proves impractical inside a test, take 2 and say so in the test's remarks, because it weakens what the test proves.

- [ ] **Step 4: Check the suite still runs fast**

Run: `dotnet test`
Expected: all pass. CLAUDE.md advertises «~495 tests, under a second». If Roslyn pushes it past a couple of seconds, move this class behind a trait and run it in CI separately rather than slowing every local run:

```csharp
[Trait("Category", "Slow")]
```

- [ ] **Step 5: Commit**

```bash
git add tests/Viu.Emporix.Tests Directory.Packages.props
git commit -m "test: compile the generated mixin code

The structural counterpart to SpecPathTests. Three collisions are known and
each has a fixture, but the reason to compile rather than assert on the text
is the fourth one, which nobody has thought of yet.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 14: Publish, document, and record what stays unverified

**Files:**
- Modify: `.github/workflows/publish.yml:73`
- Modify: `README.md`
- Modify: `docs/analysis.md:613`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: everything above.
- Produces: no API.

- [ ] **Step 1: Pack the second project**

In `.github/workflows/publish.yml`, the pack step currently reads:

```yaml
        run: dotnet pack src/Viu.Emporix --no-build --configuration Release --output artifacts
```

Add the tool alongside it:

```yaml
        run: |
          dotnet pack src/Viu.Emporix --no-build --configuration Release --output artifacts
          dotnet pack src/Viu.Emporix.MixinSync --no-build --configuration Release --output artifacts
```

The push step already globs `artifacts/*.nupkg`, so it needs no change. **Do not touch the NuGet trusted-publishing policy**: it names `publish.yml`, which is still the workflow containing the job. That was established the hard way when the policy rejected `release.yml`.

- [ ] **Step 2: Verify the package id is free**

Run:

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://api.nuget.org/v3-flatcontainer/viu.emporix.mixinsync/index.json
```

404 means free. 200 means someone holds the id and the name must change — the `Viu.*` prefix is not reserved. Do this **before** the release PR, not after.

- [ ] **Step 3: Document it in the README**

Add a section after the existing environments section. Keep it short — the spec carries the detail:

```markdown
## Mixins

A mixin is a set of tenant-defined fields under `entity.mixins.<key>`, described
by a JSON Schema that Emporix versions for you. The SDK reads and writes them
typed, and filters on them:

```csharp
var delivery = MixinReader.Read(product.Mixins, Mixins.Delivery);

var w = MixinWriter.Create().Set(Mixins.Delivery, new DeliveryMixinV6 { Packaging = "Paper" });
product.Mixins          = w.Values;
product.Metadata.Mixins = w.SchemaUrls;   // Emporix leaves a mixin unvalidated without this

string q = MixinQuery.For(Mixins.Delivery)
    .Where(d => d.Packaging, Is.EqualTo("Paper"))
    .Build()
    .Build();
```

The types come from your tenant, so they are generated into your repository:

```bash
dotnet tool install --global Viu.Emporix.MixinSync
emporix-mixins pull && emporix-mixins generate    # commit the output
emporix-mixins check                              # for CI; exits 1 on drift
```

`check` is the part worth automating. Emporix assigns a new schema version on
every change, so put this in your own repository:

```yaml
on:
  schedule: [{ cron: "0 6 * * *" }]
  workflow_dispatch: {}
jobs:
  drift:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
      - run: dotnet tool install --global Viu.Emporix.MixinSync
      - run: emporix-mixins pull && emporix-mixins generate
        env:
          EMPORIX_BACKEND_CLIENT_ID: ${{ secrets.EMPORIX_BACKEND_CLIENT_ID }}
          EMPORIX_BACKEND_SECRET: ${{ secrets.EMPORIX_BACKEND_SECRET }}
      - uses: peter-evans/create-pull-request@v8
        with:
          title: "chore: sync mixin schema versions"
          branch: mixins/sync
```

A raised version arrives as a pull request with the type diff beside it.

Five `q` forms are taken from the Node SDK and are not yet verified against a
live tenant — the range syntax, the localized path, `exists`/`missing`
semantics, whitespace escaping, and whether `metadata.mixins` must be resent on
PATCH. They are listed in
[the design spec](docs/superpowers/specs/2026-09-03-mixin-codegen-design.md).
```

- [ ] **Step 4: Update the roadmap row**

`docs/analysis.md:613` was corrected to «Designed» when the spec landed. Change it to «V1» now that it is built:

```
| `@viu/emporix-mixins` code generation | CLI tool plus core runtime (not a source generator) | **V1** |
```

- [ ] **Step 5: Tell CLAUDE.md about the second package**

The «What this is» section says the repository publishes one package. Add after the commands block:

```markdown
`Viu.Emporix.MixinSync` is the second published project — a `dotnet tool` that
generates typed mixins into a consumer's repository. It is the only project
besides `tools/Viu.Emporix.SpecSync` that opts out of `IsAotCompatible`, because
NJsonSchema cannot satisfy it. It shares the core's version line: one tag, one
Release Please package, two `dotnet pack` calls in `publish.yml`.
```

- [ ] **Step 6: Full verification**

```bash
dotnet build                                                        # 0 warnings
dotnet test                                                         # all pass
dotnet publish samples/Viu.Emporix.Sample --configuration Release    # 0 AOT warnings
dotnet publish samples/Viu.Emporix.Storefront --configuration Release
dotnet pack src/Viu.Emporix.MixinSync --configuration Release --output /tmp/mixinsync
dotnet tool install --global --add-source /tmp/mixinsync Viu.Emporix.MixinSync
emporix-mixins help
dotnet tool uninstall --global Viu.Emporix.MixinSync
```

The tool install is the check that `PackAsTool` and `ToolCommandName` are right, and it is the equivalent of the sample build that once caught a defect no unit test saw.

- [ ] **Step 7: Commit**

```bash
git add .github/workflows/publish.yml README.md docs/analysis.md CLAUDE.md
git commit -m "docs: document the mixin tooling and pack it

publish.yml packs both projects now. The trusted-publishing policy is
untouched on purpose: it names publish.yml, which is still the workflow
containing the job.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## After the plan

**The five unverified q forms are the real remaining risk**, and no task above closes them. Extend `samples/Viu.Emporix.SmokeTest` with a mixin pass once a tenant with mixins is available: write a mixin through `MixinWriter`, read it back, then filter for it with a range and a localized clause. That is the only method that has ever found a defect of this kind here — roughly half of this SDK's known defects came from reading specifications against the code, most of the rest from live calls, and none from a unit test.

Task 9 is evidence the first half still works: the schema listing dropped every page but the first, and reading `specs/schema.yml` against the facade is what surfaced it.
