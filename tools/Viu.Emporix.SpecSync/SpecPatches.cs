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
            ["price"] =
            [
                new SpecPatch(
                    "upstream: matchResponse.metadata.version is declared as a string, but the "
                    + "live API answers with a number. The generated model then fails to parse "
                    + "a successful price match — the storefront's central call. Every other "
                    + "«version» in this specification is already an integer, so the string is "
                    + "the outlier rather than the rule.",
                    ReplaceAll(
                        "            version:\n              type: string\n              description: Version of the price object.",
                        "            version:\n              type: integer\n              description: Version of the price object.")),

                new SpecPatch(
                    "upstream: matchResponse declares the matched item under «itemRef», but "
                    + "the API sends «itemId» — as does the specification's own response "
                    + "example a few lines further down. A caller then cannot tell which "
                    + "product a price belongs to, and cannot build the cart item that needs "
                    + "both the product and the price id.",
                    ReplaceAll(
                        "        itemRef:\n          type: object\n          description: Item (product or price) for which the price was matched.",
                        "        itemId:\n          type: object\n          description: Item (product or price) for which the price was matched.")),
                new SpecPatch(
                    "upstream: the element schema of a price-search response is written "
                    + "inline with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it ItemPrices.",
                    Title(
                        "itemPrices",
                        "                items:\n                  type: object\n                  properties:",
                        "                items:\n                  type: object\n                  title: itemPrices\n                  properties:")),
            ],

            ["cart"] =
            [
                new SpecPatch(
                    "upstream: the tax aggregate on a calculated cart price declares its "
                    + "«lines» as a single object, while its own description calls it «a list "
                    + "of tax values» and the API sends an array. Reading a cart that has a "
                    + "priced item then fails to parse — an empty cart does not, because the "
                    + "field only appears once there is something to tax. The sibling schema "
                    + "calculatedTaxAggregate models the same thing correctly, so the inline "
                    + "block is pointed at it.",
                    yaml =>
                    {
                        const string Marker =
                            "                taxAggregate:\n                  properties:\n"
                            + "                    lines:\n                      allOf:\n"
                            + "                        - $ref: '#/components/schemas/calculatedPrice'";

                        int start = yaml.IndexOf(Marker, StringComparison.Ordinal);
                        if (start < 0)
                        {
                            return null;
                        }

                        // The block runs until the next line indented no further
                        // than «taxAggregate» itself.
                        int end = start + Marker.Length;
                        while (true)
                        {
                            int lineEnd = yaml.IndexOf('\n', end);
                            if (lineEnd < 0)
                            {
                                end = yaml.Length;
                                break;
                            }

                            string line = yaml[(lineEnd + 1)..Math.Min(yaml.Length, lineEnd + 18)];
                            if (line.Length > 0 && !string.IsNullOrWhiteSpace(line) && !line.StartsWith("                 ", StringComparison.Ordinal))
                            {
                                end = lineEnd + 1;
                                break;
                            }

                            end = lineEnd + 1;
                        }

                        return yaml[..start]
                            + "                taxAggregate:\n"
                            + "                  $ref: '#/components/schemas/calculatedTaxAggregate'\n"
                            + yaml[end..];
                    }),
            ],


            ["shopping-list"] =
            [
                new SpecPatch(
                    "upstream: metadata timestamps are declared as TimeProperties, an object "
                    + "of epochSecond and nano — Java's Instant serialised field by field. The "
                    + "live API sends an ISO-8601 string (»2026-06-13T17:36:55.366Z«), so the "
                    + "generated model cannot read a shopping list at all. Verified against "
                    + "tenant viu on 2026-09-01.",
                    ReplaceAll(
                        "    TimeProperties:\n      type: object\n      properties:\n"
                        + "        epochSecond:\n          type: number\n"
                        + "        nano:\n          type: number",
                        "    TimeProperties:\n      type: string\n      format: date-time")),
            ],

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
                new SpecPatch(
                    "upstream: the element schema of updateApprovalRequest is written "
                    + "inline with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it PatchOperation.",
                    Title(
                        "patchOperation",
                        "      items:\n        type: object\n        properties:",
                        "      items:\n        type: object\n        title: patchOperation\n        properties:")),
            ],
            ["ai-service"] =
            [
                new SpecPatch(
                    "upstream: the element schema of the partial-update operation list is "
                    + "written inline with no title, so the generator has no name for it "
                    + "and emits Anonymous. A title names it PatchOperation.",
                    Title(
                        "patchOperation",
                        "      description: Partial update operation list.\n      items:\n        type: object\n        properties:",
                        "      description: Partial update operation list.\n      items:\n        type: object\n        title: patchOperation\n        properties:")),

                new SpecPatch(
                    "upstream: the element schema of agentCollaborations is written "
                    + "inline with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it AgentCollaboration.",
                    Title(
                        "agentCollaboration",
                        "      description: List of agent collaborations which allows an agent to hand off its task to other agents.\n      items:\n        type: object\n        properties:",
                        "      description: List of agent collaborations which allows an agent to hand off its task to other agents.\n      items:\n        type: object\n        title: agentCollaboration\n        properties:")),
            ],
            ["category"] =
            [
                new SpecPatch(
                    "upstream: the element schema of BulkAssignmentRequest is written "
                    + "inline with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it BulkAssignment.",
                    Title(
                        "bulkAssignment",
                        "    BulkAssignmentRequest:\n      type: array\n      minItems: 1\n      maxItems: 200\n      items:\n        type: object\n        properties:",
                        "    BulkAssignmentRequest:\n      type: array\n      minItems: 1\n      maxItems: 200\n      items:\n        type: object\n        title: bulkAssignment\n        properties:")),

                new SpecPatch(
                    "upstream: the element schema of BulkAssignmentUpsertRequest is "
                    + "written inline with no title, so the generator has no name for it "
                    + "and emits Anonymous. A title names it BulkAssignmentUpsert.",
                    Title(
                        "bulkAssignmentUpsert",
                        "    BulkAssignmentUpsertRequest:\n      type: array\n      minItems: 1\n      maxItems: 200\n      items:\n        type: object\n        properties:",
                        "    BulkAssignmentUpsertRequest:\n      type: array\n      minItems: 1\n      maxItems: 200\n      items:\n        type: object\n        title: bulkAssignmentUpsert\n        properties:")),

                new SpecPatch(
                    "upstream: the element schema of BulkAssignmentResponse is written "
                    + "inline with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it BulkAssignmentResult.",
                    Title(
                        "bulkAssignmentResult",
                        "      type: array\n      items:\n        type: object\n        properties:",
                        "      type: array\n      items:\n        type: object\n        title: bulkAssignmentResult\n        properties:")),
            ],
            ["customer-segment"] =
            [
                new SpecPatch(
                    "upstream: the element schema of CategoryTreeResponse is written "
                    + "inline with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it CategoryTreeNode.",
                    Title(
                        "categoryTreeNode",
                        "      items:\n        type: object\n        properties:",
                        "      items:\n        type: object\n        title: categoryTreeNode\n        properties:")),

                new SpecPatch(
                    "upstream: the object member composed into CommonSegment is written "
                    + "inline with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it SegmentCore.",
                    Title(
                        "segmentCore",
                        "    CommonSegment:\n      type: object\n      description: |\n\n      allOf:\n        - type: object\n          properties:",
                        "    CommonSegment:\n      type: object\n      description: |\n\n      allOf:\n        - type: object\n          title: segmentCore\n          properties:")),

                new SpecPatch(
                    "upstream: the object member composed into ItemAssignmentUpsert is "
                    + "written inline with no title, so the generator has no name for it "
                    + "and emits Anonymous. A title names it ItemAssignmentCore.",
                    Title(
                        "itemAssignmentCore",
                        "    ItemAssignmentUpsert:\n      type: object\n      description: |\n\n      allOf:\n        - type: object\n          properties:",
                        "    ItemAssignmentUpsert:\n      type: object\n      description: |\n\n      allOf:\n        - type: object\n          title: itemAssignmentCore\n          properties:")),
            ],
            ["label-service"] =
            [
                new SpecPatch(
                    "upstream: the element schema of the 400 response's details list is "
                    + "written inline with no title, so the generator has no name for it "
                    + "and emits Anonymous. A title names it ErrorDetail.",
                    Title(
                        "errorDetail",
                        "            items:\n              type: object\n              properties:",
                        "            items:\n              type: object\n              title: errorDetail\n              properties:")),
            ],
            ["product"] =
            [
                new SpecPatch(
                    "upstream: the element schema of bundledProducts, which is the type a "
                    + "caller has to name when building a bundle is written inline with no "
                    + "title, so the generator has no name for it and emits Anonymous. A "
                    + "title names it BundledProduct.",
                    Title(
                        "bundledProduct",
                        "      items:\n        type: object\n        properties:",
                        "      items:\n        type: object\n        title: bundledProduct\n        properties:")),

                new SpecPatch(
                    "upstream: the element schema of salePricesData is written inline "
                    + "with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it SalePrice.",
                    Title(
                        "salePrice",
                        "      items:\n        description: Mixins of the `salePricesData`.\n        properties:",
                        "      items:\n        description: Mixins of the `salePricesData`.\n        title: salePrice\n        properties:")),

                new SpecPatch(
                    "upstream: the element schema of productMedia is written inline with "
                    + "no title, so the generator has no name for it and emits Anonymous. A "
                    + "title names it ProductMediaFile.",
                    Title(
                        "productMediaFile",
                        "        type: object\n        additionalProperties: false\n        properties:",
                        "        type: object\n        additionalProperties: false\n        title: productMediaFile\n        properties:")),
            ],
            ["quote"] =
            [
                new SpecPatch(
                    "upstream: the element schema of QuoteUpdateRequest is written inline "
                    + "with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it PatchOperation.",
                    Title(
                        "patchOperation",
                        "    QuoteUpdateRequest:\n      type: array\n      description: Quote update operation list.\n      items:\n        type: object\n        properties:",
                        "    QuoteUpdateRequest:\n      type: array\n      description: Quote update operation list.\n      items:\n        type: object\n        title: patchOperation\n        properties:")),

                new SpecPatch(
                    "upstream: the element schema of QuoteHistory is written inline with "
                    + "no title, so the generator has no name for it and emits Anonymous. A "
                    + "title names it QuoteHistoryEntry.",
                    Title(
                        "quoteHistoryEntry",
                        "    QuoteHistory:\n      type: array\n      description: Quote update operation list.\n      items:\n        type: object\n        properties:",
                        "    QuoteHistory:\n      type: array\n      description: Quote update operation list.\n      items:\n        type: object\n        title: quoteHistoryEntry\n        properties:")),

                new SpecPatch(
                    "upstream: the object member composed into the "
                    + "QuoteItemsReplaceUpdate element is written inline with no title, so "
                    + "the generator has no name for it and emits Anonymous. A title names "
                    + "it QuoteItemReplacement.",
                    Title(
                        "quoteItemReplacement",
                        "      description: Quote item ID.\n      items:\n        allOf:",
                        "      description: Quote item ID.\n      items:\n        title: quoteItemReplacement\n        allOf:")),

                new SpecPatch(
                    "upstream: the element schema of QuoteItemIds is written inline with "
                    + "no title, so the generator has no name for it and emits Anonymous. A "
                    + "title names it QuoteItemReference.",
                    Title(
                        "quoteItemReference",
                        "      description: List of item IDs.\n      items:\n        type: object\n        properties:",
                        "      description: List of item IDs.\n      items:\n        type: object\n        title: quoteItemReference\n        properties:")),
            ],
            ["schema"] =
            [
                new SpecPatch(
                    "upstream: the object member composed into a schema-update response "
                    + "element is written inline with no title, so the generator has no "
                    + "name for it and emits Anonymous. A title names it SchemaReference.",
                    Title(
                        "schemaReference",
                        "              type: array\n              items:\n                allOf:",
                        "              type: array\n              items:\n                title: schemaReference\n                allOf:")),

                new SpecPatch(
                    "upstream: the element schema of BulkResponse is written inline with "
                    + "no title, so the generator has no name for it and emits Anonymous. A "
                    + "title names it BulkResponseEntry.",
                    Title(
                        "bulkResponseEntry",
                        "      items:\n        type: object\n        properties:",
                        "      items:\n        type: object\n        title: bulkResponseEntry\n        properties:")),
            ],
            ["sequential-id"] =
            [
                new SpecPatch(
                    "upstream: the value schema of SchemaBatchNextIdRequest is written "
                    + "inline with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it NextIdRequest.",
                    Title(
                        "nextIdRequest",
                        "      additionalProperties:\n        type: object\n        properties:",
                        "      additionalProperties:\n        type: object\n        title: nextIdRequest\n        properties:")),

                new SpecPatch(
                    "upstream: the value schema of SchemaBatchNextIdResponse is written "
                    + "inline with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it NextIdResult.",
                    Title(
                        "nextIdResult",
                        "      type: object\n      additionalProperties:\n        type: object\n        description: Properties used as placeholders.\n        properties:",
                        "      type: object\n      additionalProperties:\n        type: object\n        description: Properties used as placeholders.\n        title: nextIdResult\n        properties:")),

                new SpecPatch(
                    "upstream: the value schema of Placeholders is written inline with no "
                    + "title, so the generator has no name for it and emits Anonymous. A "
                    + "title names it PlaceholderDefinition.",
                    Title(
                        "placeholderDefinition",
                        "      description: Placeholder definitions used in `preText` and `postText`. Names must start and end with `__`.\n      additionalProperties:\n        type: object\n        description: Properties used as placeholders.\n        properties:",
                        "      description: Placeholder definitions used in `preText` and `postText`. Names must start and end with `__`.\n      additionalProperties:\n        type: object\n        description: Properties used as placeholders.\n        title: placeholderDefinition\n        properties:")),
            ],
            ["shipping"] =
            [
                new SpecPatch(
                    "upstream: the element schema of Patch is written inline with no "
                    + "title, so the generator has no name for it and emits Anonymous. A "
                    + "title names it PatchOperation.",
                    Title(
                        "patchOperation",
                        "      items:\n        type: object\n        properties:",
                        "      items:\n        type: object\n        title: patchOperation\n        properties:")),

                new SpecPatch(
                    "upstream: the element schema of a bulk response is written inline "
                    + "with no title, so the generator has no name for it and emits "
                    + "Anonymous. A title names it BulkResponseEntry.",
                    Title(
                        "bulkResponseEntry",
                        "                      - siteCode cannot be null\n                  type: object\n                  properties:",
                        "                      - siteCode cannot be null\n                  type: object\n                  title: bulkResponseEntry\n                  properties:")),
            ],
            ["webhook"] =
            [
                new SpecPatch(
                    "upstream: the object member composed into the single webhook-config "
                    + "response is written inline with no title, so the generator has no "
                    + "name for it and emits Anonymous. A title names it WebhookConfig. "
                    + "These two response schemas are structurally identical and collapse "
                    + "into one anonymous type today; distinct titles keep them apart, "
                    + "which is what the specification describes.",
                    Title(
                        "webhookConfig",
                        "        application/json:\n          schema:\n            allOf:",
                        "        application/json:\n          schema:\n            title: webhookConfig\n            allOf:")),

                new SpecPatch(
                    "upstream: the object member composed into the webhook-config list "
                    + "response is written inline with no title, so the generator has no "
                    + "name for it and emits Anonymous. A title names it "
                    + "WebhookConfigListItem. See the note on webhookConfig: the sibling "
                    + "response is the same shape.",
                    Title(
                        "webhookConfigListItem",
                        "            type: array\n            items:\n              allOf:",
                        "            type: array\n            items:\n              title: webhookConfigListItem\n              allOf:")),
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

    /// <summary>
    /// Names an anonymous schema by inserting a <c>title</c>, exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NSwag names a generated class after the schema's key under
    /// <c>components/schemas</c>, or after its <c>title</c> when it has no key.
    /// A schema with neither — one written inline under <c>items</c>, under
    /// <c>additionalProperties</c>, or as the object member of an
    /// <c>allOf</c> — becomes <c>Anonymous</c>, <c>Anonymous2</c> and so on,
    /// numbered in the order the generator happens to walk them.
    /// </para>
    /// <para>
    /// Four rules, each established by generating and looking rather than by
    /// reading NSwag's source:
    /// </para>
    /// <list type="number">
    /// <item>Directly on an inline object schema a <c>title</c> simply works.</item>
    /// <item>A schema that already has a key under <c>components/schemas</c>
    /// ignores its <c>title</c> — the key wins.</item>
    /// <item>For an <c>allOf</c> it depends on whether the composing schema is
    /// named. Unnamed, as in a response body, the merged result is what needs
    /// the title. Named, the key claims that name and the inline member becomes
    /// a separate base class, so the title belongs on the member. Getting this
    /// backwards leaves the type anonymous with no error.</item>
    /// <item>Two schemas in one document that share a title collide, and one of
    /// them stays anonymous after all. Titles are unique per specification.</item>
    /// </list>
    /// <para>
    /// <para>
    /// <b>Refuses an ambiguous anchor.</b> Unlike <see cref="ReplaceAll"/> this
    /// returns <see langword="null"/> when the anchor occurs more than once, so
    /// the run reports the patch as stale rather than titling a schema nobody
    /// meant. That matters more here than anywhere else in this file: two
    /// wrongly swapped titles both compile and both pass every test.
    /// </para>
    /// </remarks>
    private static Func<string, string?> Title(string name, string find, string replace)
        => yaml =>
        {
            int first = yaml.IndexOf(find, StringComparison.Ordinal);

            if (first < 0 || yaml.IndexOf(find, first + 1, StringComparison.Ordinal) >= 0)
            {
                return null;
            }

            return yaml.Replace(find, replace, StringComparison.Ordinal);
        };

    /// <summary>Replaces every occurrence; <see langword="null"/> when there is none.</summary>
    private static Func<string, string?> ReplaceAll(string find, string replace)
        => yaml => yaml.Contains(find, StringComparison.Ordinal)
            ? yaml.Replace(find, replace, StringComparison.Ordinal)
            : null;
}
