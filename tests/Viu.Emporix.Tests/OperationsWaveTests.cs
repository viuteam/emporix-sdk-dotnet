using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

/// <summary>
/// The operational services: imports, indexing, pick-pack, shopping lists,
/// reward points, AI, the RAG indexer and cloud functions.
/// </summary>
/// <remarks>
/// Aimed at the places where these differ from the rest of the API rather than
/// where they agree with it: a service that pages from zero, one that leaves the
/// tenant out of the address, one that needs a second credential in a header,
/// and the calls that must never be retried because they move points or start
/// work.
/// </remarks>
public class OperationsWaveTests
{
    private static IOptions<EmporixOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EmporixOptions { Tenant = "acme" });

    private static EmporixHttpClient Http(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), Options());

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    private static bool IsRepeatable(StubHttpMessageHandler handler)
        => handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out bool v) && v;

    // The stub sits where the network is, below the authentication handler, so
    // no token has been fetched yet. What a service decides is the auth context
    // it attaches, and that is what these tests read.
    private static AuthContext Auth(StubHttpMessageHandler handler)
    {
        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        return auth;
    }

    // ---------- Imports ----------

    [Fact]
    public async Task The_import_tool_pages_from_zero_and_spells_its_parameters_differently()
    {
        // Every other service in the SDK sends pageNumber/pageSize counting from
        // one. Copying that here silently skips the first page.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        ImportService imports = new(Http(handler), Options());

        await imports.ListRunsAsync("cfg-1");

        Assert.Equal("/importtool/acme/configs/cfg-1/runs?page=0&size=20", Uri(handler));
    }

    [Fact]
    public async Task Starting_and_retrying_an_import_are_never_repeatable()
    {
        // A retried start imports the same source twice, and the SDK cannot know
        // whether the target tolerates that.
        StubHttpMessageHandler start = new(HttpStatusCode.OK, "{}");
        StubHttpMessageHandler retry = new(HttpStatusCode.OK, "{}");

        await new ImportService(Http(start), Options()).StartRunAsync("cfg-1");
        await new ImportService(Http(retry), Options()).RetryRunAsync("run-1");

        Assert.False(IsRepeatable(start));
        Assert.False(IsRepeatable(retry));
    }

    [Fact]
    public async Task Starting_an_import_sends_only_the_options_that_were_set()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        ImportService imports = new(Http(handler), Options());

        await imports.StartRunAsync("cfg-1", ImportServiceModels.BodyMode.FULL, dryRun: true);

        Assert.Equal("""{"mode":"FULL","dryRun":true}""", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Following_an_import_asks_for_an_event_stream()
    {
        // Without the header a server may answer with JSON, and the caller's SSE
        // parser then waits for events that never arrive.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, string.Empty);
        ImportService imports = new(Http(handler), Options());

        using HttpResponseMessage response = await imports.StreamEventsAsync("run-1");

        Assert.Equal("/importtool/acme/runs/run-1/events", Uri(handler));
        Assert.Equal("text/event-stream", handler.LastHeader("Accept"));
    }

    [Fact]
    public async Task A_records_query_carries_the_required_type_and_drops_the_rest()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        ImportService imports = new(Http(handler), Options());

        await imports.ListRecordsAsync("product", outcome: ImportServiceModels.Outcome.FAILED);

        Assert.Equal(
            "/importtool/acme/data/records?type=product&outcome=FAILED&page=0&size=20",
            Uri(handler));
    }

    // ---------- Indexing ----------

    [Fact]
    public async Task The_public_index_configuration_is_readable_without_a_service_token()
    {
        // The full configuration carries the write key. A storefront must get
        // the public one, or a browser ends up holding a key that can rewrite
        // the index.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        IndexingService indexing = new(Http(handler), Options());

        await indexing.ListPublicConfigurationsAsync();

        Assert.Equal("/indexing/acme/public/configurations", Uri(handler));
        Assert.Equal(AuthKind.Anonymous, Auth(handler).Kind);
    }

    [Fact]
    public async Task Reading_the_full_index_configuration_uses_a_service_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        IndexingService indexing = new(Http(handler), Options());

        await indexing.ListConfigurationsAsync();

        Assert.Equal("/indexing/acme/configurations", Uri(handler));
        Assert.Equal(AuthKind.Service, Auth(handler).Kind);
    }

    [Fact]
    public async Task Starting_a_reindex_job_is_not_repeatable_but_reading_one_is()
    {
        StubHttpMessageHandler start = new(HttpStatusCode.Created, "{}");
        StubHttpMessageHandler read = new(HttpStatusCode.OK, "{}");

        await new IndexingService(Http(start), Options())
            .StartReindexJobAsync(new IndexingServiceModels.ReindexRequest { EntityType = "product" });
        await new IndexingService(Http(read), Options()).GetReindexJobAsync("job-1");

        Assert.False(IsRepeatable(start));
        Assert.True(IsRepeatable(read));
    }

    // ---------- Pick and pack ----------

    [Fact]
    public async Task The_calls_that_change_an_order_carry_the_packers_second_credential()
    {
        // saas-token is a required header, separate from the OAuth token. A call
        // that changes an order has to say who changed it.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"message":"ok","code":200}""");
        PickPackService pickPack = new(Http(handler), Options());

        string? message = await pickPack.FinishOrderAsync("order-1", "signed-packer-token");

        Assert.Equal("/pick-pack/acme/orders/order-1/finish", Uri(handler));
        Assert.Equal("signed-packer-token", handler.LastHeader("saas-token"));
        Assert.Equal("ok", message);
        Assert.False(IsRepeatable(handler));
    }

    [Fact]
    public async Task An_assignee_is_removed_at_its_own_address()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        PickPackService pickPack = new(Http(handler), Options());

        await pickPack.RemoveAssigneeAsync("order-1", "packer-7");

        Assert.Equal("/pick-pack/acme/orders/order-1/assignees/packer-7", Uri(handler));
        Assert.Equal(HttpMethod.Delete, handler.RequestMethods[0]);
    }

    [Fact]
    public async Task Reporting_a_picking_event_is_not_repeatable()
    {
        // The event carries its own id and Emporix answers 409 to a repeat, but
        // a 409 is a failure to the caller — so the SDK does not retry it.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        PickPackService pickPack = new(Http(handler), Options());

        await pickPack.CreateEventAsync(
            new PickPackModels.OrderEntryEventCreate
            {
                EventId = "e1",
                OrderNumber = "o1",
                ProductId = "p1",
                Unit = "kg",
            },
            "signed-packer-token");

        Assert.False(IsRepeatable(handler));
    }

    // ---------- Shopping lists ----------

    [Fact]
    public async Task Creating_a_list_for_oneself_and_for_a_customer_are_different_methods()
    {
        // Both are the same POST. Which one happens depends on the scope, and
        // without the scope the customerId in the body is ignored — so the two
        // read differently at the call site.
        StubHttpMessageHandler own = new(HttpStatusCode.Created, """{"id":"C1"}""");
        StubHttpMessageHandler employee = new(HttpStatusCode.Created, """{"id":"C2"}""");

        string? a = await new ShoppingListService(Http(own), Options())
            .CreateAsync(new ShoppingListModels.OwnShoppingListCreateRequest { Name = "default" });
        string? b = await new ShoppingListService(Http(employee), Options())
            .CreateForCustomerAsync(new ShoppingListModels.EmployeeShoppingListCreateRequest
            {
                Name = "default",
                CustomerId = "C2",
            });

        Assert.Equal("C1", a);
        Assert.Equal("C2", b);
        Assert.DoesNotContain("customerId", own.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"customerId\":\"C2\"", employee.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_without_a_name_deletes_every_list()
    {
        // Worth pinning: the parameter is optional and omitting it is not «the
        // default list» but «all of them».
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        ShoppingListService lists = new(Http(handler), Options());

        await lists.DeleteAsync("C1");

        Assert.Equal("/shoppinglist/acme/shopping-lists/C1", Uri(handler));
    }

    // ---------- Reward points ----------

    [Fact]
    public async Task Most_of_the_reward_service_leaves_the_tenant_out_of_the_address()
    {
        // Emporix takes the tenant from the access token here. Adding it to the
        // path is a 404.
        StubHttpMessageHandler points = new(HttpStatusCode.OK, "2000");
        StubHttpMessageHandler options = new(HttpStatusCode.OK, "[]");

        int balance = await new RewardPointsService(Http(points), Options()).GetPointsAsync("C1");
        await new RewardPointsService(Http(options), Options()).ListRedeemOptionsAsync();

        Assert.Equal("/reward-points/customer/C1", Uri(points));
        Assert.Equal(2000, balance);

        // The redemption options are the exception: they do name the tenant.
        Assert.Equal("/reward-points/acme/redeemOptions", Uri(options));
    }

    [Fact]
    public async Task Awarding_and_redeeming_points_are_never_repeatable()
    {
        // Points are money. A retry after a timeout awards or spends twice, and
        // nothing in the request lets Emporix recognise the repeat.
        StubHttpMessageHandler add = new(HttpStatusCode.Created, string.Empty);
        StubHttpMessageHandler redeem = new(HttpStatusCode.Created, string.Empty);

        await new RewardPointsService(Http(add), Options())
            .AddPointsAsync("C1", new RewardPointsModels.AddedPoints());
        await new RewardPointsService(Http(redeem), Options())
            .RedeemPointsAsync("C1", new RewardPointsModels.RedeemedPoints());

        Assert.False(IsRepeatable(add));
        Assert.False(IsRepeatable(redeem));
    }

    [Fact]
    public async Task A_shopper_reading_their_own_points_must_bring_a_customer_token()
    {
        // These endpoints carry no customer id at all. A service token would
        // reach them and fail in a way that reads like a permissions problem.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "10");
        RewardPointsService rewards = new(Http(handler), Options());

        await Assert.ThrowsAsync<EmporixConfigurationException>(
            () => rewards.GetMyPointsAsync(AuthContext.Service()));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_shopper_with_a_customer_token_reaches_the_public_endpoint()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "1153");
        RewardPointsService rewards = new(Http(handler), Options());

        int balance = await rewards.GetMyPointsAsync(AuthContext.Customer("customer-jwt"));

        Assert.Equal("/reward-points/public/customer", Uri(handler));
        Assert.Equal(AuthKind.Customer, Auth(handler).Kind);
        Assert.Equal(1153, balance);
    }

    // ---------- AI ----------

    [Fact]
    public async Task A_chat_continues_a_conversation_through_the_session_header()
    {
        // The session id is not part of the body. Dropping it starts a new
        // conversation on every turn, which looks like an agent with no memory.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        AiService ai = new(Http(handler), Options());

        await ai.ChatAsync(
            new AiServiceModels.AgenticRequest { AgentId = "a1", Message = "hello" },
            sessionId: "s-42");

        Assert.Equal("/ai-service/acme/agentic/chat", Uri(handler));
        Assert.Equal("s-42", handler.LastHeader("session-id"));
        Assert.False(IsRepeatable(handler));
    }

    [Fact]
    public async Task A_first_chat_sends_no_session_header_at_all()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        AiService ai = new(Http(handler), Options());

        await ai.ChatAsync(new AiServiceModels.AgenticRequest { AgentId = "a1", Message = "hi" });

        Assert.Null(handler.LastHeader("session-id"));
    }

    [Fact]
    public async Task Generation_is_billed_and_therefore_never_repeatable()
    {
        StubHttpMessageHandler texts = new(HttpStatusCode.OK, "{}");
        StubHttpMessageHandler completions = new(HttpStatusCode.OK, "{}");

        await new AiService(Http(texts), Options())
            .GenerateTextAsync(new AiServiceModels.TextGenerationRequest());
        await new AiService(Http(completions), Options())
            .CompleteAsync(new AiServiceModels.CompletionRequest());

        Assert.False(IsRepeatable(texts));
        Assert.False(IsRepeatable(completions));
    }

    [Fact]
    public async Task Searching_is_a_POST_that_reads_and_is_therefore_repeatable()
    {
        // The filter goes in the body because an address has a length limit. It
        // still changes nothing, so a retry after a 503 is safe.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        AiService ai = new(Http(handler), Options());

        await ai.Agents.SearchAsync("name:support", new AiListOptions { PageSize = 5 });

        Assert.Equal(HttpMethod.Post, handler.RequestMethods[0]);
        Assert.Equal("/ai-service/acme/agentic/agents/search?pageSize=5", Uri(handler));
        Assert.Equal("""{"q":"name:support"}""", handler.RequestBodies[0]);
        Assert.True(IsRepeatable(handler));
    }

    [Fact]
    public async Task A_patch_body_is_a_list_of_operations_in_the_APIs_own_spelling()
    {
        // The operation names are uppercase on the wire. A camelCase «replace»
        // is a 400 that reads like a schema problem.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        AiService ai = new(Http(handler), Options());

        await ai.Agents.PatchAsync(
            "a1",
            [
                new AiPatchOperation
                {
                    Op = AiPatchOperationKind.REPLACE,
                    Path = "/enabled",
                    Value = JsonDocument.Parse("true").RootElement.Clone(),
                },
                new AiPatchOperation { Op = AiPatchOperationKind.REMOVE, Path = "/icon" },
            ]);

        Assert.Equal(HttpMethod.Patch, handler.RequestMethods[0]);
        Assert.Equal(
            """[{"op":"REPLACE","path":"/enabled","value":true},{"op":"REMOVE","path":"/icon"}]""",
            handler.RequestBodies[0]);
    }

    [Fact]
    public async Task A_put_that_created_something_reports_its_id_and_one_that_replaced_does_not()
    {
        // Emporix answers 201 with the id when the resource is new and 204 when
        // it already existed. Both are success; only one has an id.
        StubHttpMessageHandler created = new(HttpStatusCode.Created, """{"id":"tok-1"}""");
        StubHttpMessageHandler replaced = new(HttpStatusCode.NoContent, string.Empty);

        string? a = await new AiService(Http(created), Options()).Tokens
            .ReplaceAsync("tok-1", new AiServiceModels.TokenRequest { Name = "slack" });
        string? b = await new AiService(Http(replaced), Options()).Tokens
            .ReplaceAsync("tok-1", new AiServiceModels.TokenRequest { Name = "slack" });

        Assert.Equal("tok-1", a);
        Assert.Null(b);
    }

    [Fact]
    public async Task Reading_a_tool_hands_back_JSON_because_four_shapes_share_the_endpoint()
    {
        // The specification declares a oneOf with no discriminator the generator
        // can use. Deserialising into one of the four would drop the other
        // three's configuration without saying so.
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK, """{"id":"t1","type":"teams","config":{"channelId":"c1"}}""");
        AiService ai = new(Http(handler), Options());

        JsonElement tool = await ai.Tools.GetAsync("t1");

        Assert.Equal("teams", tool.GetProperty("type").GetString());
        Assert.Equal("c1", tool.GetProperty("config").GetProperty("channelId").GetString());
    }

    [Fact]
    public async Task The_list_options_send_nothing_the_caller_did_not_set()
    {
        StubHttpMessageHandler bare = new(HttpStatusCode.OK, "[]");
        StubHttpMessageHandler full = new(HttpStatusCode.OK, "[]");

        await new AiService(Http(bare), Options()).Agents.ListAsync();
        await new AiService(Http(full), Options()).Agents.ListAsync(new AiListOptions
        {
            Query = "enabled:true",
            PageNumber = 2,
            PageSize = 10,
            Expand = "nativeTools",
            TotalCount = true,
            AcceptLanguage = "de",
        });

        Assert.Equal("/ai-service/acme/agentic/agents", Uri(bare));
        Assert.Null(bare.LastHeader("Accept-Language"));
        Assert.Equal(
            "/ai-service/acme/agentic/agents?q=enabled%3Atrue&pageNumber=2&pageSize=10&expand=nativeTools",
            Uri(full));
        Assert.Equal("de", full.LastHeader("Accept-Language"));
        Assert.Equal("true", full.LastHeader("X-Total-Count"));
    }

    [Fact]
    public async Task An_attachment_goes_up_as_multipart_under_the_name_the_API_expects()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, """{"id":"att-1"}""");
        AiService ai = new(Http(handler), Options());

        await ai.UploadAttachmentAsync("a1", new byte[] { 1, 2, 3 }, "notes.txt", "text/plain");

        Assert.Equal("/ai-service/acme/agentic/a1/attachments", Uri(handler));
        Assert.Contains("name=attachment", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("notes.txt", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.False(IsRepeatable(handler));
    }

    // ---------- RAG indexer ----------

    [Fact]
    public async Task The_rag_indexer_addresses_an_entity_type_rather_than_an_id()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        RagIndexerService indexer = new(Http(handler), Options());

        await indexer.ReindexAsync("PRODUCT");

        Assert.Equal("/ai-rag-indexer/acme/PRODUCT/reindex", Uri(handler));
        Assert.False(IsRepeatable(handler));
    }

    // ---------- Cloud functions ----------

    [Fact]
    public async Task A_cloud_function_is_anonymous_by_default_and_posted_to()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"ok":true}""");
        CloudFunctionService functions = new(Http(handler), Options());

        await functions.InvokeJsonAsync("price-check");

        Assert.Equal("/cloud-functions/acme/functions/price-check", Uri(handler));
        Assert.Equal(HttpMethod.Post, handler.RequestMethods[0]);
        Assert.Equal(AuthKind.Anonymous, Auth(handler).Kind);
    }

    [Fact]
    public async Task A_cloud_function_is_never_repeatable()
    {
        // It is someone else's code. The SDK cannot know whether running it
        // twice is safe, and this is the one call where guessing would be a
        // guess about another system's side effects.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        CloudFunctionService functions = new(Http(handler), Options());

        await functions.InvokeJsonAsync("charge", method: HttpMethod.Get);

        Assert.False(IsRepeatable(handler));
    }

    [Fact]
    public async Task A_cloud_function_sub_path_is_escaped_segment_by_segment()
    {
        // Escaping the whole sub-path at once would turn its slashes into %2F
        // and address a function that does not exist.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        CloudFunctionService functions = new(Http(handler), Options());

        await functions.InvokeJsonAsync("catalogue", path: "/items/a b");

        Assert.Equal("/cloud-functions/acme/functions/catalogue/items/a%20b", Uri(handler));
    }

    [Fact]
    public async Task A_typed_cloud_function_call_uses_the_callers_own_type_information()
    {
        // ADR-0009: the SDK cannot serialise an arbitrary type without
        // reflection, so the caller brings the JsonTypeInfo. TestProduct plays
        // the caller's own type here, serialised by the tests' own context —
        // which is exactly the arrangement a consumer would have.
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK, """{"id":"x","name":"Widget"}""");
        CloudFunctionService functions = new(Http(handler), Options());

        TestProduct? result = await functions.InvokeAsync(
            "echo",
            new TestProduct { Id = "x", Name = "Widget" },
            TestJsonContext.Default.TestProduct,
            TestJsonContext.Default.TestProduct);

        Assert.Equal("Widget", result?.Name);
        Assert.Equal("""{"id":"x","name":"Widget"}""", handler.RequestBodies[0]);
    }

    // ---------- Audit log ----------

    [Fact]
    public async Task The_audit_log_lives_under_changelog_and_pages_from_one()
    {
        // Emporix names the service «changelog» in the address even though it is
        // documented as the audit log, and it pages from one with page/size.
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """{"items":[],"page":1,"size":20,"totalElements":0,"totalPages":0}""");
        AuditLogService audit = new(Http(handler), Options());

        await audit.ListAsync("entity:order entityId:o1");

        Assert.Equal(
            "/changelog/acme/changelogs?page=1&size=20&q=entity%3Aorder%20entityId%3Ao1",
            Uri(handler));
        Assert.True(IsRepeatable(handler));
    }

    [Fact]
    public async Task The_audit_log_page_size_is_capped_at_a_hundred()
    {
        // The API's own maximum. Asking for more is a 400, and finding that out
        // from a live call is slower than finding it out here.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        AuditLogService audit = new(Http(handler), Options());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => audit.ListAsync(size: 101));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_schedule_can_be_removed_without_removing_its_configuration()
    {
        // The endpoint arrived in an upstream sync and nothing announced it —
        // the coverage check in SpecPathTests exists so the next one does not
        // have to be found by reading a diff.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        ImportService imports = new(Http(handler), Options());

        await imports.DeleteScheduleAsync("cfg-1");

        Assert.Equal(HttpMethod.Delete, handler.RequestMethods[0]);
        Assert.Equal("/importtool/acme/configs/cfg-1/schedule", Uri(handler));

        // Deleting a schedule that is not there answers 204 as well, so a retry
        // after a dropped connection cannot do harm.
        Assert.True(IsRepeatable(handler));
    }
}
