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
    /// <returns>The lockfile.</returns>
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
    /// <param name="recorded">The lockfile on disk, or <see langword="null"/> when absent.</param>
    /// <param name="live">What the tenant reports now.</param>
    /// <returns>One line per difference; empty when in sync.</returns>
    public static IReadOnlyList<string> Diff(Lockfile? recorded, Lockfile live)
    {
        ArgumentNullException.ThrowIfNull(live);

        SortedDictionary<string, LockEntry> before =
            recorded?.Mixins ?? new SortedDictionary<string, LockEntry>(StringComparer.Ordinal);
        List<string> drift = [];

        IEnumerable<string> keys = before.Keys
            .Union(live.Mixins.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        foreach (string key in keys)
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
                drift.Add($"{key}: v{was.Version} to v{now.Version}");
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

    /// <summary>Reads a lockfile, or <see langword="null"/> when there is none.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The lockfile, or <see langword="null"/>.</returns>
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
