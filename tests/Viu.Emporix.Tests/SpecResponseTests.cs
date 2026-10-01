using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Viu.Emporix.Tests;

/// <summary>
/// Checks that every response the hand-written services read is deserialised
/// into the type its specification declares.
/// </summary>
/// <remarks>
/// <para>
/// The other half of <see cref="SpecPathTests"/>: that one checks where a call
/// goes, this one what it reads when the answer comes back.
/// <c>CartService.AddItemAsync</c> read the <c>201</c> of
/// <c>POST /carts/{cartId}/items</c> as <c>CartItemResponse</c> while the
/// specification declares <c>createdCartItem</c>, so the new item's id landed in
/// <c>AdditionalProperties</c>. Its unit test stubbed the same wrong body, the
/// smoke test only checked that a body came back, and a live call found it. When
/// this check first ran, it found forty more reads of a type the specification
/// does not declare, and five places where the specification itself is wrong.
/// </para>
/// <para>
/// Every <c>_http.SendAsync</c> and <c>SendPageAsync</c> that passes a
/// <c>XJsonContext.Default.T</c> is paired with the call
/// <see cref="SpecPathTests"/> resolves inside its arguments, and T is compared
/// with the schemas of that operation's 2xx responses. A named schema is compared
/// by the type NSwag generated for it, an inline one without a usable name by its
/// property names.
/// </para>
/// <para>
/// T comes out of the serialization contexts by reflection, and the
/// specifications are read with YamlDotNet's deserializer, which keeps the last of
/// a duplicate key — as the generator does: a <c>SpecPatch</c> once gave a schema
/// in <c>specs/shipping.yml</c> two titles, and NSwag named the type after the
/// second. Both trip the trimming and AOT analysers, which run on this project as
/// well, so they are suppressed for this class alone. The package stays
/// reflection-free (ADR-0004); a test is never trimmed.
/// </para>
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = NeverTrimmed)]
[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = NeverTrimmed)]
[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = NeverTrimmed)]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = NeverTrimmed)]
public class SpecResponseTests
{
    private const string NeverTrimmed =
        "Inspects the SDK's own types and the vendored specifications; tests are neither trimmed nor compiled ahead of time.";

    private static readonly Assembly Sdk = typeof(EmporixClient).Assembly;

    [Fact]
    public void Every_response_a_service_reads_is_the_type_its_specification_declares()
    {
        DirectoryInfo root = SpecPathTests.FindRepositoryRoot();
        Dictionary<string, Operation> operations = ReadOperations(root);
        List<Read> reads = ReadServiceResponses(root, out _);

        // A guard on the guard: if the scan finds nothing, it is broken rather
        // than the code being clean.
        Assert.True(operations.Count > 600, $"Only {operations.Count} specification operations found.");
        Assert.True(reads.Count > 350, $"Only {reads.Count} typed reads found.");

        Dictionary<string, string> found = new(StringComparer.Ordinal);
        foreach (Read read in reads)
        {
            foreach (string call in read.Calls)
            {
                if (Mismatch(read.Type, call, operations) is string mismatch)
                {
                    found.TryAdd(mismatch, $"{read.File}:{read.Line}");
                }
            }
        }

        string[] pinned = [.. Deliberate.Keys, .. KnownMismatches.Keys, .. SpecDefects.Keys];
        string[] unexpected = [.. found.Keys.Except(pinned).Order().Select(m => $"{m}  ({found[m]})")];
        string[] stale = [.. pinned.Except(found.Keys).Order()];

        Assert.True(
            unexpected.Length == 0 && stale.Length == 0,
            string.Join(
                Environment.NewLine,
                [
                    $"Not listed, though the specification declares another type ({unexpected.Length}):",
                    .. unexpected,
                    $"Listed, though the mismatch is gone — remove the entry ({stale.Length}):",
                    .. stale,
                ]));
    }

    /// <summary>
    /// Mismatches this SDK means. Each says why, and stays until the reason goes.
    /// </summary>
    private static readonly Dictionary<string, string> Deliberate = new(StringComparer.Ordinal)
    {
        ["GET /ai-service/{}/agentic/mcp-servers reads JsonElement, declared List<oneOf(AiServiceModels.CustomMcpServerResponse | AiServiceModels.DynamicMcpServerResponse)>"] = Union,
        ["GET /ai-service/{}/agentic/mcp-servers/{} reads JsonElement, declared oneOf(AiServiceModels.CustomMcpServerResponse | AiServiceModels.DynamicMcpServerResponse)"] = Union,
        ["POST /ai-service/{}/agentic/mcp-servers/search reads JsonElement, declared List<oneOf(AiServiceModels.CustomMcpServerResponse | AiServiceModels.DynamicMcpServerResponse)>"] = Union,
        ["GET /ai-service/{}/agentic/tools reads JsonElement, declared List<List<oneOf(AiServiceModels.SlackNativeToolResponse | AiServiceModels.RagCustomNativeToolResponse | AiServiceModels.RagEmporixNativeToolResponse | AiServiceModels.TeamsNativeToolResponse)>>"] = Union,
        ["GET /ai-service/{}/agentic/tools/{} reads JsonElement, declared oneOf(AiServiceModels.SlackNativeToolResponse | AiServiceModels.RagCustomNativeToolResponse | AiServiceModels.RagEmporixNativeToolResponse | AiServiceModels.TeamsNativeToolResponse)"] = Union,
        ["POST /ai-service/{}/agentic/tools/search reads JsonElement, declared List<List<oneOf(AiServiceModels.SlackNativeToolResponse | AiServiceModels.RagCustomNativeToolResponse | AiServiceModels.RagEmporixNativeToolResponse | AiServiceModels.TeamsNativeToolResponse)>>"] = Union,

        ["GET /schema/{}/custom-entities/{}/instances reads JsonElement, declared List<SchemaModels.CustomInstanceResponse>"] = TenantDefined,
        ["GET /schema/{}/custom-entities/{}/instances/{} reads JsonElement, declared SchemaModels.CustomInstanceResponse"] = TenantDefined,
        ["POST /schema/{}/custom-entities/{}/instances/search reads JsonElement, declared List<SchemaModels.CustomInstanceResponse>"] = TenantDefined,
        ["GET /site/{}/sites/{}/mixins reads JsonElement, declared free-form object"] =
            "A mixin is arbitrary JSON under a name of the tenant's choosing; the specification declares only an open object.",
        ["GET /site/{}/sites/{}/mixins/{} reads JsonElement, declared free-form object"] =
            "A mixin is arbitrary JSON under a name of the tenant's choosing; the specification declares only an open object.",

        ["GET /schema/{}/types reads JsonElement, declared List<string>"] = UntypedUnexplained,
        ["GET /schema/{}/references reads JsonElement, declared List<SchemaModels.AbstractSchemaResponse>"] = UntypedUnexplained,
        ["GET /schema/{}/references/{} reads JsonElement, declared SchemaModels.AbstractSchemaResponse"] = UntypedUnexplained,
        ["GET /unit-handling/{}/types reads JsonElement, declared List<string>"] = UntypedUnexplained,
        ["GET /webhook/{}/statistics reads JsonElement, declared WebhookModels.WebhookStatistics"] =
            "Untyped since it was written. The remark at the call calls the shape undocumented, but the "
            + "specification declares WebhookStatistics; JsonElement reads it without loss.",

        ["POST /cart/{}/carts reads CartCreated, declared CartModels.CreatedCart"] =
            "Hand-written since the first version; no reason is recorded. It reads cartId and leaves out the "
            + "yrn the specification also declares.",
        ["POST /ai-service/{}/agentic/templates/{}/agents reads AiIdResponse, declared AiServiceModels.IdResponse"] = PropertylessId,
        ["PUT /ai-service/{}/agentic/agents/{} reads AiIdResponse, declared AiServiceModels.IdResponse | no body"] = PropertylessId,
        ["PUT /ai-service/{}/agentic/mcp-servers/{} reads AiIdResponse, declared AiServiceModels.IdResponse | no body"] = PropertylessId,
        ["PUT /ai-service/{}/agentic/oauths/{} reads AiIdResponse, declared AiServiceModels.IdResponse | no body"] = PropertylessId,
        ["PUT /ai-service/{}/agentic/tokens/{} reads AiIdResponse, declared AiServiceModels.IdResponse | no body"] = PropertylessId,
        ["PUT /ai-service/{}/agentic/tools/{} reads AiIdResponse, declared AiServiceModels.IdResponse | no body"] = PropertylessId,

        ["GET /return/{}/returns reads List<ReturnsModels.FullEmployeeReturn>, declared anyOf(ReturnsModels.FullCustomerReturn | ReturnsModels.FullEmployeeReturn)"] = WiderView,
        ["GET /return/{}/returns/{} reads ReturnsModels.FullEmployeeReturn, declared anyOf(ReturnsModels.FullCustomerReturn | ReturnsModels.FullEmployeeReturn)"] = WiderView,
        ["GET /customer-segment/{}/segments/{}/customers/{} reads CustomerSegmentModels.CustomerAssignmentResponse, declared CustomerSegmentModels.CustomerAssignmentB2CResponse"] =
            "The list reads declare CustomerAssignmentResponse, the single read its subset without legalEntity. "
            + "One type serves all three, and legalEntity simply stays empty here.",
        ["POST /approval/{}/approvals reads ApprovalServiceModels.CreatedResource, declared ApprovalServiceModels.ApprovalId"] =
            "Both generated types are exactly { id }; only the name differs, and renaming a public return type would buy nothing.",
    };

    private const string Union =
        "A oneOf without a discriminator the generator could act on: any one alternative would drop the "
        + "others' fields, so the read stays raw while the writes are typed, one method per kind.";

    private const string TenantDefined =
        "An instance is arbitrary JSON: the tenant defines a custom entity's shape, and the SDK deliberately does not know it.";

    private const string UntypedUnexplained =
        "Untyped since it was written; no reason is recorded at the call. JsonElement reads the declared shape without loss.";

    private const string PropertylessId =
        "The generated IdResponse has no id property, so there is nothing to read it into; AiIdResponse declares { id } itself.";

    private const string WiderView =
        "The customer's or the employee's view, depending on who asks. The SDK reads the wider employee view, "
        + "which derives from the customer one, so either answer fits.";

    /// <summary>
    /// Facades that read a type the specification does not declare, waiting for a fix.
    /// </summary>
    /// <remarks>
    /// Pinned rather than failing, so the check guards every other read from the
    /// day it lands. A fix removes its entry; the assertion is set equality in
    /// both directions, so forgetting that fails as loudly as a new mismatch.
    /// Changing a return type is a breaking change, which is why these wait for
    /// a <c>fix!</c> of their own rather than riding along with the check.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownMismatches = new(StringComparer.Ordinal)
    {
        ["POST /price/{}/prices/search reads List<PriceModels.GetPrice>, declared List<PriceModels.ItemPrices>"] =
            "The answer groups prices per item, [{ itemId, prices }], by schema and example; itemId arrives as a "
            + "string where GetPrice expects an object, so a non-empty answer throws. The Node SDK assumes what "
            + "this facade does. Unverified against a live tenant.",
        ["GET /sequential-id/{}/schemas/types/{} reads SequentialIdModels.SequenceSchema, declared List<SequentialIdModels.SequenceSchema>"] =
            "Declared as every schema of the type, with a two-entry example, while the facade reads one; an array "
            + "read as an object throws. Unverified against a live tenant.",
        ["GET /fee/{}/itemFees/{}/fees reads List<FeeModels.Fee>, declared oneOf(List<FeeModels.Fee> | List<string>)"] = FeeIdsByDefault,
        ["GET /fee/{}/productFees/{}/fees reads List<FeeModels.Fee>, declared oneOf(List<FeeModels.Fee> | List<string>)"] = FeeIdsByDefault,
        ["POST /customer-segment/{}/segments/match reads List<CustomerSegmentModels.SegmentResponse>, declared List<CustomerSegmentModels.MatchItem>"] =
            "The answer is the matching items, one product id each, read as segments: the product id lands in a "
            + "segment's Id. Unverified against a live tenant.",
        ["GET /price/{}/priceModels/{} reads PriceModels.PriceModelDefinitionRetrieval, declared List<PriceModels.PriceModelDefinitionRetrieval>"] =
            "Disputed: the specification's schema and example both answer an array, and both SDKs read one "
            + "object. A live call has to decide which side gets fixed.",

        ["* /customer-segment/{}/segments/bulk reads List<CustomerSegmentModels.BulkAssignmentResponse>, declared List<CustomerSegmentModels.BulkResponse>"] = SegmentIds,
        ["DELETE /customer-segment/{}/segments/bulk reads List<CustomerSegmentModels.BulkAssignmentResponse>, declared List<CustomerSegmentModels.BulkResponse>"] = SegmentIds,
        ["POST /payment-gateway/{}/payment/{}/capture reads PaymentModels.CommonPaymentResponse, declared {captureId, message, successful}"] =
            "Drops captureId, the payment provider's identifier of the capture.",
        ["GET /configuration/{}/clients/{}/configurations reads List<ConfigurationModels.BaseConfiguration>, declared List<ConfigurationModels.ClientConfiguration>"] = ClientFields,
        ["GET /configuration/{}/clients/{}/configurations/{} reads ConfigurationModels.BaseConfiguration, declared ConfigurationModels.ClientConfiguration"] = ClientFields,
        ["GET /availability/{}/availability/{}/{} reads AvailabilityModels.Availability, declared AvailabilityModels.AvailabilityWithBundle"] = BundleAvailability,
        ["POST /availability/{}/availability/search reads List<AvailabilityModels.Availability>, declared List<AvailabilityModels.AvailabilityWithBundle>"] = BundleAvailability,
        ["GET /order-v2/{}/legal-entity-orders/{} reads List<OrderV2Models.Order>, declared List<OrderV2Models.SalesOrder>"] = SalesOrderFields,
        ["GET /order-v2/{}/legal-entity-orders/{}/{} reads OrderV2Models.Order, declared OrderV2Models.SalesOrder"] = SalesOrderFields,
        ["GET /price/{}/prices/{} reads PriceModels.GetPrice, declared PriceModels.GetSinglePrice"] =
            "Drops priceModel, which the single read adds to a price.",
        ["GET /media/{}/assets/{} reads MediaModels.GetAsset, declared oneOf(MediaModels.GetAssetBlob | MediaModels.GetAssetLink)"] =
            "GetAsset covers both kinds of asset, but drops the status a BLOB created through an upload session carries.",

        ["POST /customer-segment/{}/segments reads CustomerSegmentModels.SegmentResponse, declared {id}"] = IdOnly,
        ["POST /customer/{}/signup reads CustomerModels.Customer, declared CustomerModels.ResourceLocation"] = IdOnly,
        ["POST /customer/{}/me/addresses reads CustomerModels.AddressDto, declared CustomerModels.ResourceLocation"] = IdOnly,
        ["POST /media/{}/assets reads MediaModels.GetAsset, declared {id}"] = IdOnly,
        ["POST /media/{}/assets reads MediaModels.GetAssetLink, declared {id}"] = IdOnly,
        ["POST /product/{}/product-templates reads ProductModels.ResourceLocation, declared {id}"] =
            "ResourceLocation adds a yrn, which this answer does not carry and which stays empty.",

        ["PATCH /customer/{}/me reads CustomerModels.Customer, declared no body"] = NoBody,
        ["POST /cart/{}/carts/{}/merge reads CartModels.Cart, declared no body"] = NoBody,
        ["POST /order-v2/{}/salesorders/{}/calculations reads OrderV2Models.SalesOrder, declared no body"] = NoBody,
        ["POST /schema/{}/custom-entities/import reads SchemaModels.ExportImportResponse, declared no body"] = NoBody,
        ["DELETE /price/{}/price-lists/{}/prices/bulk reads List<PriceModels.PriceBulkResponseEntry>, declared no body"] = NoBody,
        ["PUT /media/{}/assets/{} reads MediaModels.GetAsset, declared no body"] =
            "Declared without a body, and the reference update behind attaching and detaching answered 204 "
            + "with none against tenant viu on 2026-09-10. Replacing a file and changing a link return null too, "
            + "unless the API answers more than its specification says.",
    };

    private const string FeeIdsByDefault =
        "Full fees come only with expand=true; by default the answer is their ids, and the facade sends no "
        + "expand, so a non-empty answer throws. Unverified against a live tenant.";

    private const string SegmentIds =
        "BulkResponse is BulkAssignmentResponse plus the segment's id, so a bulk create hands back no ids of what it created.";

    private const string ClientFields =
        "Drops _id and client, which ClientConfiguration adds to BaseConfiguration.";

    private const string BundleAvailability =
        "Drops bundleAvailabilities, which only a bundle carries.";

    private const string SalesOrderFields =
        "Drops the five fields salesOrder adds: assistedBuying, channel, checkout, createdBy and quoteId.";

    private const string IdOnly =
        "Declared to answer only { id }, read into a fuller type whose other fields stay empty. Harmless "
        + "while callers read Id, which is all the smoke test does; misleading to anyone else.";

    private const string NoBody =
        "Declared without a body, so the typed result is always null, or empty for a list, unless the API "
        + "answers more than its specification says.";

    /// <summary>
    /// Mismatches where the specification is wrong and the facade right.
    /// </summary>
    /// <remarks>
    /// These are repaired in the specification by a <c>SpecPatch</c> in
    /// <c>tools/Viu.Emporix.SpecSync</c>, as every other upstream defect is; the
    /// sync that applies one makes its entry here stale.
    /// </remarks>
    private static readonly Dictionary<string, string> SpecDefects = new(StringComparer.Ordinal)
    {
    };

    /// <summary>
    /// Guards the scanner itself: a read whose type or address it cannot see is
    /// checked by nobody.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Six calls are unreadable, and deliberately so. The product service's four
    /// read helpers take their type from the caller — <c>BasicProductWithId</c>
    /// for the plain reads, <c>IEmporixProduct</c> for the ones that resolve the
    /// specification's <c>oneOf</c> of five product kinds. Cloud functions have no
    /// specification at all (ADR-0009): the caller supplies the response type, and
    /// the address is assembled by a helper.
    /// </para>
    /// <para>
    /// Anything else appearing here means a new way of passing a type or writing
    /// an address has crept in, and with it a read neither this check nor its
    /// sibling covers.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_scanner_reads_every_response_type_but_six()
    {
        ReadServiceResponses(SpecPathTests.FindRepositoryRoot(), out List<string> unread);

        Assert.Equal(
            [
                "CloudFunctionService.cs: JsonElement, at an address the scanner cannot read",
                "CloudFunctionService.cs: responseTypeInfo",
                "ProductService.cs: listTypeInfo",
                "ProductService.cs: listTypeInfo",
                "ProductService.cs: listTypeInfo",
                "ProductService.cs: typeInfo",
            ],
            unread.Order());
    }

    private sealed record Read(string File, int Line, Type Type, string[] Calls);

    private sealed record Operation(string Namespace, object Document, List<object?> Bodies);

    /// <summary>
    /// Describes the mismatch between a read and its operation, or returns
    /// <see langword="null"/> when the type is one the specification declares.
    /// </summary>
    /// <remarks>
    /// A method held in a variable is matched against every verb on the path, as
    /// <see cref="SpecPathTests"/> does — a helper serving several verbs passes
    /// when its type fits any of them. A call no specification declares at all is
    /// that test's finding, not this one's.
    /// </remarks>
    private static string? Mismatch(Type type, string call, Dictionary<string, Operation> operations)
    {
        Operation[] candidates = call.StartsWith("* ", StringComparison.Ordinal)
            ? [.. operations.Where(o => o.Key.EndsWith(call[1..], StringComparison.Ordinal)).Select(o => o.Value)]
            : operations.TryGetValue(call, out Operation? operation) ? [operation] : [];

        if (candidates.Length == 0)
        {
            return null;
        }

        string actual = Name(type);
        List<string> declared = [];

        foreach (Operation candidate in candidates)
        {
            foreach (object? schema in candidate.Bodies)
            {
                string expected = schema is null ? "no body" : Expected(schema, candidate);

                if (expected == actual || SameShape(expected, actual, type))
                {
                    return null;
                }

                declared.Add(expected);
            }
        }

        return $"{call} reads {actual}, declared "
            + (declared.Count == 0 ? "no 2xx response" : string.Join(" | ", declared.Distinct()));
    }

    /// <summary>
    /// Compares an inline schema without a name by its property names.
    /// </summary>
    /// <remarks>
    /// NSwag numbers such schemas — <c>Response</c>, <c>Response2</c> — in the
    /// order it happens to walk them, and the facades rightly prefer a named
    /// type of the same shape, such as <c>ResourceId</c> for <c>{ id }</c>. The
    /// names have to match exactly: a superset is how <c>CartItemResponse</c>
    /// passed for <c>{ itemId, yrn }</c> without a complaint from anyone.
    /// </remarks>
    private static bool SameShape(string expected, string actual, Type type)
    {
        while (expected.StartsWith("List<", StringComparison.Ordinal)
            && actual.StartsWith("List<", StringComparison.Ordinal)
            && ElementOf(type) is Type element)
        {
            expected = expected[5..^1];
            actual = actual[5..^1];
            type = element;
        }

        return expected.StartsWith('{') && JsonNames(type) is SortedSet<string> names && expected == Braces(names);
    }

    /// <summary>The type a facade reads, named the way <see cref="Expected"/> names a schema.</summary>
    private static string Name(Type type)
    {
        if (ElementOf(type) is Type element)
        {
            return $"List<{Name(element)}>";
        }

        if (type.IsGenericType)
        {
            string generic = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
            return $"{generic}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
        }

        return type == typeof(string) ? "string"
            : type == typeof(int) ? "int"
            : type == typeof(long) ? "long"
            : type == typeof(double) ? "double"
            : type == typeof(bool) ? "bool"
            : type.FullName!.Replace("Viu.Emporix.", string.Empty, StringComparison.Ordinal)
                .Replace("System.Text.Json.", string.Empty, StringComparison.Ordinal)
                .Replace("System.", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// The element of a list — as a facade writes it, <c>List&lt;T&gt;</c>, or as
    /// NSwag declares a named array schema, a class deriving from
    /// <c>Collection&lt;T&gt;</c>. Both read the same JSON array.
    /// </summary>
    private static Type? ElementOf(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && (current.GetGenericTypeDefinition() == typeof(List<>)
                    || current.GetGenericTypeDefinition() == typeof(Collection<>)))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>The JSON property names a type reads, or <see langword="null"/> for a scalar.</summary>
    private static SortedSet<string>? JsonNames(Type type)
    {
        if (type.IsPrimitive || type == typeof(string) || type == typeof(JsonElement))
        {
            return null;
        }

        return new(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<JsonExtensionDataAttribute>() is null
                    && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name)),
            StringComparer.Ordinal);
    }

    private static string Braces(IEnumerable<string> names) => $"{{{string.Join(", ", names)}}}";

    /// <summary>
    /// Names the type a schema declares, the way the generator names it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rules follow what <c>tools/Viu.Emporix.SpecSync</c> learnt by
    /// generating and looking. A schema under <c>components/schemas</c> is named
    /// after its key, and its <c>title</c> is ignored; an inline schema is named
    /// after a <c>title</c> that is a valid identifier, which is what most
    /// <c>SpecPatch</c> entries add. An <c>allOf</c> of one reference and nothing
    /// else would be an empty class, which <c>GeneratedCodeFixer</c> dissolves into
    /// its base, and a name differing from another only in case gains the suffix
    /// <c>Cased</c>.
    /// </para>
    /// <para>
    /// A name that comes out without a generated type is reported as such rather
    /// than guessed at, so a new naming rule shows up as a mismatch to look at.
    /// </para>
    /// </remarks>
    private static string Expected(object schema, Operation operation)
    {
        if (Text(schema, "$ref") is string reference)
        {
            object target = Resolve(schema, operation.Document);

            if (Get(target, "allOf") is List<object> parts
                && parts.Count(p => Text(p, "$ref") is not null) == 1
                && Get(target, "properties") is null
                && parts.All(p => Text(p, "$ref") is not null || Get(p, "properties") is null))
            {
                return Expected(parts.Single(p => Text(p, "$ref") is not null), operation);
            }

            return Structural(target, operation)
                ?? Generated(operation.Namespace, reference[(reference.LastIndexOf('/') + 1)..]);
        }

        if (Structural(schema, operation) is string structural)
        {
            return structural;
        }

        return Text(schema, "title") is string title && Regex.IsMatch(title, "^[A-Za-z0-9_]+$")
            ? Generated(operation.Namespace, title)
            : Braces(SpecNames(schema, operation));
    }

    /// <summary>The shapes that never become a generated class of their own.</summary>
    private static string? Structural(object schema, Operation operation)
    {
        foreach (string union in (string[])["oneOf", "anyOf"])
        {
            if (Get(schema, union) is List<object> branches)
            {
                return $"{union}({string.Join(" | ", branches.Select(b => Expected(b, operation)))})";
            }
        }

        if (Text(schema, "type") == "array" && Get(schema, "items") is object items)
        {
            return $"List<{Expected(items, operation)}>";
        }

        if (Get(schema, "properties") is not null || Get(schema, "enum") is not null || Get(schema, "allOf") is not null)
        {
            return null;
        }

        return (Text(schema, "type"), Text(schema, "format")) switch
        {
            ("string", "date-time") => "DateTimeOffset",
            ("string", "binary") => "binary",
            ("string", _) => "string",
            ("integer", "int64") => "long",
            ("integer", _) => "int",
            ("number", _) => "double",
            ("boolean", _) => "bool",
            ("object" or null, _) when Get(schema, "additionalProperties") is null or "true" => "free-form object",
            _ => null,
        };
    }

    private static string Generated(string ns, string name)
    {
        string pascal = Regex.Replace(
            char.ToUpperInvariant(name[0]) + name[1..],
            "-(.)",
            m => m.Groups[1].Value.ToUpperInvariant());

        foreach (string candidate in (string[])[pascal, pascal + "Cased"])
        {
            if (Sdk.GetType($"{ns}.{candidate}") is Type type)
            {
                return Name(type);
            }
        }

        return $"{ns["Viu.Emporix.".Length..]}.{pascal} (not generated)";
    }

    /// <summary>The property names a schema declares, through its <c>allOf</c> parts.</summary>
    private static SortedSet<string> SpecNames(object schema, Operation operation)
    {
        schema = Resolve(schema, operation.Document);
        SortedSet<string> names = new(StringComparer.Ordinal);

        if (Get(schema, "properties") is Dictionary<object, object> properties)
        {
            names.UnionWith(properties.Keys.Cast<string>());
        }

        foreach (object part in Get(schema, "allOf") as List<object> ?? [])
        {
            names.UnionWith(SpecNames(part, operation));
        }

        return names;
    }

    /// <summary>
    /// Collects every operation the specifications declare, with the schemas of
    /// its 2xx responses — <see langword="null"/> for one without a body.
    /// </summary>
    /// <remarks>
    /// Keyed exactly like <see cref="SpecPathTests"/> keys its paths, server
    /// prefix included, so the two scans meet. Only JSON bodies count: one
    /// endpoint also answers XML, which no facade asks for.
    /// </remarks>
    private static Dictionary<string, Operation> ReadOperations(DirectoryInfo root)
    {
        IDeserializer yaml = new DeserializerBuilder().Build();
        Dictionary<string, Operation> operations = new(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(Path.Combine(root.FullName, "specs"), "*.yml"))
        {
            object document;
            using (StreamReader reader = File.OpenText(file))
            {
                document = yaml.Deserialize<object>(reader);
            }

            string spec = Path.GetFileNameWithoutExtension(file);
            string ns = $"Viu.Emporix.{string.Concat(spec.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]))}Models";
            string prefix = Get(document, "servers") is List<object> { Count: > 0 } servers
                && Text(servers[0], "url") is string url
                    ? Regex.Match(url, @"^https://[^/]+(/[A-Za-z0-9-]*)?").Groups[1].Value.TrimEnd('/')
                    : string.Empty;

            foreach ((object path, object item) in (Dictionary<object, object>)Get(document, "paths")!)
            {
                foreach ((object verb, object definition) in (Dictionary<object, object>)item)
                {
                    if ((string)verb is not ("get" or "post" or "put" or "patch" or "delete"))
                    {
                        continue;
                    }

                    List<object?> bodies = [];
                    foreach ((object code, object response) in (Dictionary<object, object>)Get(definition, "responses")!)
                    {
                        if (!((string)code).StartsWith('2'))
                        {
                            continue;
                        }

                        bodies.AddRange(
                            Get(Resolve(response, document), "content") is Dictionary<object, object> content
                                ? content
                                    .Where(c => ((string)c.Key).Contains("json", StringComparison.Ordinal))
                                    .Select(c => Get(c.Value, "schema"))
                                : [null]);
                    }

                    operations[$"{((string)verb).ToUpperInvariant()} {SpecPathTests.Normalise(prefix + (string)path)}"] =
                        new Operation(ns, document, bodies);
                }
            }
        }

        return operations;
    }

    /// <summary>
    /// Collects every typed read in the services, with the calls
    /// <see cref="SpecPathTests"/> resolves inside its arguments.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <param name="unread">
    /// The reads whose type or address cannot be worked out, reported rather than
    /// dropped for the same reason <see cref="SpecPathTests"/> reports its own.
    /// </param>
    private static List<Read> ReadServiceResponses(DirectoryInfo root, out List<string> unread)
    {
        List<(string Call, string File, int Offset)> paths = SpecPathTests.ReadServicePaths(root, out _);
        List<Read> reads = [];
        unread = [];

        foreach (string file in Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "Viu.Emporix"), "*.cs"))
        {
            // The same text the path scan read, so that its offsets line up.
            string text = SpecPathTests.WithoutComments(File.ReadAllText(file));
            string name = Path.GetFileName(file);

            foreach (Match call in Regex.Matches(text, @"_http\.(?:SendAsync|SendPageAsync)\("))
            {
                (List<string> arguments, int end) = Arguments(text, call.Index + call.Length);

                // The overload without a type reads no body.
                if (arguments.Count < 2 || arguments[1] == "cancellationToken")
                {
                    continue;
                }

                Match info = Regex.Match(arguments[1], @"^(\w+JsonContext)\.Default\.(\w+)$");
                if (!info.Success)
                {
                    unread.Add($"{name}: {arguments[1]}");
                    continue;
                }

                string[] calls = [.. paths
                    .Where(p => p.File == name && p.Offset > call.Index && p.Offset < end)
                    .Select(p => p.Call)
                    .Distinct()];

                if (calls.Length == 0)
                {
                    unread.Add($"{name}: {info.Groups[2].Value}, at an address the scanner cannot read");
                    continue;
                }

                reads.Add(new Read(
                    name,
                    text[..call.Index].Count(c => c == '\n') + 1,
                    TypeOf(info.Groups[1].Value, info.Groups[2].Value),
                    calls));
            }
        }

        return reads;
    }

    /// <summary>
    /// Splits a call's arguments at the commas outside any bracket, and returns
    /// where the call ends.
    /// </summary>
    /// <remarks>
    /// Strings are not skipped: no argument these calls take carries an
    /// unbalanced bracket, and the guards above would notice the day one does.
    /// </remarks>
    private static (List<string> Arguments, int End) Arguments(string text, int start)
    {
        List<string> arguments = [];
        int depth = 0;
        int from = start;
        int end = start;

        for (; depth > 0 || text[end] != ')'; end++)
        {
            switch (text[end])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(text[from..end].Trim());
                    from = end + 1;
                    break;
            }
        }

        arguments.Add(text[from..end].Trim());
        return (arguments, end);
    }

    /// <summary>The type behind <c>XJsonContext.Default.Property</c>.</summary>
    private static Type TypeOf(string context, string property)
    {
        Type type = Sdk.GetType($"Viu.Emporix.{context}", throwOnError: true)!;
        object instance = type.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        return ((JsonTypeInfo)type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!).Type;
    }

    /// <summary>Follows local references — to a schema, or to a shared response.</summary>
    private static object Resolve(object node, object document)
    {
        while (Text(node, "$ref") is string reference && reference.StartsWith("#/", StringComparison.Ordinal))
        {
            node = reference[2..].Split('/').Aggregate(
                document,
                (current, segment) => Get(current, segment) ?? throw new InvalidOperationException($"Dangling {reference}"));
        }

        return node;
    }

    private static object? Get(object? node, string key)
        => node is Dictionary<object, object> map && map.TryGetValue(key, out object? value) ? value : null;

    private static string? Text(object? node, string key) => Get(node, key) as string;
}
