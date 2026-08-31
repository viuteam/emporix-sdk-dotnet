using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Viu.Emporix.SpecSync;

/// <summary>Where a vendored specification came from and what it looked like.</summary>
internal sealed class SpecManifestEntry
{
    /// <summary>The address it was downloaded from.</summary>
    public required string Url { get; init; }

    /// <summary>The version from <c>info.version</c>. Often empty upstream.</summary>
    public required string SpecVersion { get; init; }

    /// <summary>When it was last downloaded.</summary>
    public required DateTimeOffset FetchedAt { get; init; }

    /// <summary>
    /// The SHA-256 digest of the repaired text — the measure of whether anything
    /// changed.
    /// </summary>
    public required string Sha256 { get; init; }
}

/// <summary>The state of all vendored specifications.</summary>
internal sealed partial class SyncManifest
{
    /// <summary>When this state was produced.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>The entries, keyed by service name.</summary>
    public required SortedDictionary<string, SpecManifestEntry> Services { get; init; }

    /// <summary>How the state is written to disk.</summary>
    /// <remarks>
    /// Indented and camel-cased: the file is version-controlled and read in
    /// reviews, so its diff should be legible.
    /// </remarks>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The SHA-256 digest of a text, lowercase.</summary>
    public static string Hash(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// Reads <c>info.version</c> from an OpenAPI specification.
    /// </summary>
    /// <remarks>
    /// Via a regular expression rather than a YAML reader: the value is purely
    /// informational, and that does not justify another dependency. It looks for
    /// the first <c>version:</c> key indented by two spaces — the one under
    /// <c>info:</c>.
    /// </remarks>
    public static string ReadSpecVersion(string yaml)
    {
        Match match = VersionPattern().Match(yaml);

        return match.Success
            ? match.Groups[1].Value.Trim().Trim('\'', '"')
            : string.Empty;
    }

    /// <summary>
    /// The services whose digest is new or has changed, alphabetically.
    /// </summary>
    public static IReadOnlyList<string> Diff(SyncManifest? previous, SyncManifest next)
    {
        if (previous is null)
        {
            return [];
        }

        List<string> changed = [];

        foreach ((string name, SpecManifestEntry entry) in next.Services)
        {
            if (!previous.Services.TryGetValue(name, out SpecManifestEntry? before)
                || !string.Equals(before.Sha256, entry.Sha256, StringComparison.Ordinal))
            {
                changed.Add(name);
            }
        }

        changed.Sort(StringComparer.Ordinal);
        return changed;
    }

    [GeneratedRegex(@"^ {2}version:\s*(.*)$", RegexOptions.Multiline)]
    private static partial Regex VersionPattern();
}
