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
    /// <returns>The configuration.</returns>
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
