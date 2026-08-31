using System.Text.RegularExpressions;

namespace Viu.Emporix.SpecSync;

/// <summary>A single, self-contained repair applied to one specification.</summary>
/// <param name="Reason">
/// What is wrong upstream and what the corrected form should be. Appears in the
/// sync log and in the review of the resulting change.
/// </param>
/// <param name="Apply">
/// Returns the repaired text, or <see langword="null"/> when there is nothing to
/// do — because the defect is absent or already fixed.
/// </param>
internal sealed record SpecPatch(string Reason, Func<string, string?> Apply);

/// <summary>The outcome of all repairs on one specification.</summary>
/// <param name="Yaml">The repaired text.</param>
/// <param name="Applied">Reasons of the repairs that changed something.</param>
/// <param name="Stale">Repairs that changed nothing — candidates for removal.</param>
internal sealed record PatchOutcome(string Yaml, IReadOnlyList<string> Applied, IReadOnlyList<SpecPatch> Stale);

/// <summary>
/// Repairs for known defects in the Emporix specifications.
/// </summary>
/// <remarks>
/// <para>
/// The specifications are vendored verbatim. Occasionally one carries a defect
/// that breaks generation outright, or describes an operation in a way that
/// makes it uncallable. Waiting for a fix from Emporix would mean generating no
/// types at all, so such spots are repaired before the file is written and
/// hashed. The vendored specification is therefore already correct, and the
/// digest stays stable across runs.
/// </para>
/// <para>
/// Every repair is idempotent. One that no longer changes anything is reported
/// as stale: either Emporix fixed the defect, or the surrounding text moved.
/// Both are worth noticing — a dead repair is dead weight, and one that applies
/// only partly would be dangerous.
/// </para>
/// <para>
/// Staleness is decided by a sync run, which applies the repairs to the
/// <em>freshly downloaded</em> text. Inspecting the vendored file does not help:
/// it is already repaired and looks clean either way.
/// </para>
/// </remarks>
internal static partial class SpecPatches
{
    /// <summary>
    /// Repairs applied to every specification.
    /// </summary>
    /// <remarks>
    /// For defects that can occur anywhere and are not tied to a specific service.
    /// </remarks>
    public static IReadOnlyList<SpecPatch> Global { get; } =
    [
        new SpecPatch(
            "YAML does not tolerate a whitespace-only line immediately after a block is "
            + "opened (»description: |«): the parser reports «found extra spaces in first "
            + "line» and gives up. Such lines become truly empty. To YAML that is "
            + "equivalent — a line of nothing but spaces already counts as blank. There "
            + "are 175 such spots across 25 specifications; only a few sit at the critical "
            + "position today, the rest would get there with the next upstream reshuffle.",
            yaml =>
            {
                string next = WhitespaceOnlyLine().Replace(yaml, string.Empty);
                return next == yaml ? null : next;
            }),

        new SpecPatch(
            "YAML has escapes JSON does not know: \\_ is a non-breaking space, \\L a line "
            + "separator. NSwag converts the description to JSON and fails on them with "
            + "«Bad JSON escape sequence». Both are replaced by the character they mean. "
            + "Only these two — \\d, \\s, \\S and \\w appear in the specifications as "
            + "regular expressions and must not be touched.",
            yaml =>
            {
                string next = yaml
                    .Replace(@"\_", " ", StringComparison.Ordinal)
                    .Replace(@"\L", string.Empty, StringComparison.Ordinal);

                return next == yaml ? null : next;
            }),
    ];

    /// <summary>Repairs by specification name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<SpecPatch>> ByService { get; } =
        new Dictionary<string, IReadOnlyList<SpecPatch>>(StringComparer.Ordinal)
        {
            ["approval-service"] =
            [
                new SpecPatch(
                    "upstream: the JSON Patch operation enum is uppercase (ADD/REMOVE/REPLACE); "
                    + "the live API rejects those values with 400 and accepts only lowercase. "
                    + "Lowercasing is what makes the generated type usable.",
                    ReplaceAll(
                        "              - enum:\n                  - ADD\n                  - REMOVE\n                  - REPLACE",
                        "              - enum:\n                  - add\n                  - remove\n                  - replace")),

                new SpecPatch(
                    "upstream: the approval update examples repeat the same uppercase "
                    + "operations. That does not affect generation, but a specification whose "
                    + "examples contradict its own enum misleads the next reader.",
                    yaml =>
                    {
                        string next = yaml
                            .Replace("- op: REPLACE", "- op: replace", StringComparison.Ordinal)
                            .Replace("- op: ADD", "- op: add", StringComparison.Ordinal)
                            .Replace("- op: REMOVE", "- op: remove", StringComparison.Ordinal);

                        return next == yaml ? null : next;
                    }),
            ],
        };

    /// <summary>
    /// Applies every repair for <paramref name="serviceName"/> in order.
    /// </summary>
    /// <remarks>
    /// The repairs are independent of one another. One that changes nothing is
    /// reported rather than fatal — an obsolete repair should not block a sync.
    /// </remarks>
    public static PatchOutcome Apply(string serviceName, string yaml)
    {
        List<string> applied = [];
        List<SpecPatch> stale = [];
        string current = yaml;

        // The global ones first, then those for this service. A global repair
        // that finds nothing here is normal — most specifications do not carry
        // the respective defect — and is therefore not reported as stale.
        foreach (SpecPatch patch in Global)
        {
            string? next = patch.Apply(current);

            if (next is not null && !string.Equals(next, current, StringComparison.Ordinal))
            {
                current = next;
                applied.Add(patch.Reason);
            }
        }

        if (!ByService.TryGetValue(serviceName, out IReadOnlyList<SpecPatch>? patches))
        {
            return new PatchOutcome(current, applied, stale);
        }

        foreach (SpecPatch patch in patches)
        {
            string? next = patch.Apply(current);

            if (next is not null && !string.Equals(next, current, StringComparison.Ordinal))
            {
                current = next;
                applied.Add(patch.Reason);
            }
            else
            {
                stale.Add(patch);
            }
        }

        return new PatchOutcome(current, applied, stale);
    }

    /// <summary>A line consisting purely of whitespace.</summary>
    [GeneratedRegex(@"^[ \t]+$", RegexOptions.Multiline)]
    private static partial Regex WhitespaceOnlyLine();

    /// <summary>Replaces every occurrence; <see langword="null"/> when there is none.</summary>
    private static Func<string, string?> ReplaceAll(string find, string replace)
        => yaml => yaml.Contains(find, StringComparison.Ordinal)
            ? yaml.Replace(find, replace, StringComparison.Ordinal)
            : null;
}
