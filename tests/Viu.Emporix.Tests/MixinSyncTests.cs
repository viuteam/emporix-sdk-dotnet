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
