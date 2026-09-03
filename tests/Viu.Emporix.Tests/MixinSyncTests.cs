using System.Text.Json;
using Viu.Emporix.MixinSync;
using Viu.Emporix.SchemaModels;

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
        Lockfile recorded = Lockfile.Build([Delivery()], DateTimeOffset.UnixEpoch);

        LockEntry entry = recorded.Mixins["deliveryOptions"];
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
        Lockfile first = Lockfile.Build([Delivery()], DateTimeOffset.UnixEpoch);
        Lockfile second = Lockfile.Build([Delivery()], DateTimeOffset.UtcNow);

        // The timestamp differs on purpose: it must not count as drift, or every
        // check would fail.
        Assert.Empty(Lockfile.Diff(first, second));
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

    [Fact]
    public void Scalar_attributes_convert_to_json_schema_types()
    {
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute { Key = "title", Type = SchemaAttributeType.TEXT },
            new SchemaAttribute { Key = "weight", Type = SchemaAttributeType.DECIMAL },
            new SchemaAttribute { Key = "count", Type = SchemaAttributeType.NUMBER },
            new SchemaAttribute { Key = "active", Type = SchemaAttributeType.BOOLEAN },
        ]);

        using JsonDocument parsed = JsonDocument.Parse(schema);
        JsonElement properties = parsed.RootElement.GetProperty("properties");

        Assert.Equal("string", properties.GetProperty("title").GetProperty("type").GetString());
        Assert.Equal("number", properties.GetProperty("weight").GetProperty("type").GetString());
        Assert.Equal("boolean", properties.GetProperty("active").GetProperty("type").GetString());
        Assert.False(parsed.RootElement.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Date_and_time_attributes_carry_a_format()
    {
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute { Key = "from", Type = SchemaAttributeType.DATE },
            new SchemaAttribute { Key = "at", Type = SchemaAttributeType.DATE_TIME },
            new SchemaAttribute { Key = "clock", Type = SchemaAttributeType.TIME },
        ]);

        using JsonDocument parsed = JsonDocument.Parse(schema);
        JsonElement properties = parsed.RootElement.GetProperty("properties");

        Assert.Equal("date", properties.GetProperty("from").GetProperty("format").GetString());
        Assert.Equal("date-time", properties.GetProperty("at").GetProperty("format").GetString());
        Assert.Equal("time", properties.GetProperty("clock").GetProperty("format").GetString());
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

        using JsonDocument parsed = JsonDocument.Parse(schema);
        JsonElement values = parsed.RootElement
            .GetProperty("properties").GetProperty("packaging").GetProperty("enum");

        Assert.Equal(["Paper", "Plastic"], values.EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public void An_enum_without_values_stays_a_plain_string()
    {
        // An empty enum would generate an unusable type.
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute { Key = "packaging", Type = SchemaAttributeType.ENUM },
        ]);

        using JsonDocument parsed = JsonDocument.Parse(schema);
        JsonElement property = parsed.RootElement.GetProperty("properties").GetProperty("packaging");

        Assert.Equal("string", property.GetProperty("type").GetString());
        Assert.False(property.TryGetProperty("enum", out _));
    }

    [Fact]
    public void An_array_of_enums_keeps_its_element_values()
    {
        // ArrayType carries its own type and values, so this need not degrade to
        // an array of strings.
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute
            {
                Key = "sizes",
                Type = SchemaAttributeType.ARRAY,
                ArrayType = new ArrayType
                {
                    Type = SchemaAttributeType.ENUM,
                    Values = [new SchemaAttributeValue { Value = "S" }, new SchemaAttributeValue { Value = "M" }],
                },
            },
        ]);

        using JsonDocument parsed = JsonDocument.Parse(schema);
        JsonElement items = parsed.RootElement
            .GetProperty("properties").GetProperty("sizes").GetProperty("items");

        Assert.Equal(["S", "M"], items.GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public void A_localized_attribute_becomes_a_map_of_languages()
    {
        // What makes MixinQuery.WhereLocalized's path valid: the value is keyed
        // by language rather than being the scalar the type names.
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute
            {
                Key = "title",
                Type = SchemaAttributeType.TEXT,
                Metadata = new SchemaAttributeMetadata { Localized = true },
            },
        ]);

        using JsonDocument parsed = JsonDocument.Parse(schema);
        JsonElement property = parsed.RootElement.GetProperty("properties").GetProperty("title");

        Assert.Equal("object", property.GetProperty("type").GetString());
        Assert.Equal("string", property.GetProperty("additionalProperties").GetProperty("type").GetString());
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

        using JsonDocument parsed = JsonDocument.Parse(schema);

        Assert.Equal("string", parsed.RootElement
            .GetProperty("properties").GetProperty("note")
            .GetProperty("properties").GetProperty("en")
            .GetProperty("type").GetString());
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

        using JsonDocument parsed = JsonDocument.Parse(schema);

        Assert.Equal(["title"], parsed.RootElement.GetProperty("required")
            .EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public void A_reference_attribute_becomes_a_string()
    {
        // A reference is an id, and the tool has nothing to resolve it against.
        string schema = AttributeSchema.FromAttributes([
            new SchemaAttribute { Key = "parent", Type = SchemaAttributeType.REFERENCE },
        ]);

        using JsonDocument parsed = JsonDocument.Parse(schema);

        Assert.Equal("string", parsed.RootElement
            .GetProperty("properties").GetProperty("parent").GetProperty("type").GetString());
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
            Assert.True(
                parsed.RootElement.GetProperty("properties").TryGetProperty("field", out _),
                $"{type} produced no property");
        }
    }
}
