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
}
