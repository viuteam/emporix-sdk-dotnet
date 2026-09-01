using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Viu.Emporix.AiServiceModels;

namespace Viu.Emporix;

/// <summary>What a <see cref="AiPatchOperation"/> does to the field it names.</summary>
public enum AiPatchOperationKind
{
    /// <summary>Adds the value at the path.</summary>
    ADD,

    /// <summary>Removes whatever is at the path.</summary>
    REMOVE,

    /// <summary>Overwrites whatever is at the path.</summary>
    REPLACE,
}

/// <summary>
/// One step of a partial update.
/// </summary>
/// <remarks>
/// The AI service takes its <c>PATCH</c> bodies as a list of these. The type is
/// the SDK's own rather than a generated one: the specification leaves the
/// operation object untitled, so the generator names it <c>Anonymous</c>, which
/// is no name to put in a signature.
/// </remarks>
public sealed class AiPatchOperation
{
    /// <summary>What to do.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AiPatchOperationKind>))]
    [JsonPropertyName("op")]
    public required AiPatchOperationKind Op { get; init; }

    /// <summary>Which field, as a path — <c>/name</c>, say.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>
    /// The new value. A string or an object; omitted for
    /// <see cref="AiPatchOperationKind.REMOVE"/>.
    /// </summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }
}

/// <summary>
/// Artificial intelligence — text generation, and agents that do things.
/// </summary>
/// <remarks>
/// <para>
/// Two halves. <see cref="GenerateTextAsync"/> and <see cref="CompleteAsync"/>
/// are one-shot generation. Everything under <c>agentic</c> is the agent
/// platform: agents configured from templates, the tools and MCP servers they
/// may reach, the credentials those need, and the record of what they did.
/// </para>
/// <para>
/// A conversation is held together by a session id. Pass one to
/// <see cref="ChatAsync"/> to continue an exchange; leave it out to start one,
/// and read the id off the response.
/// </para>
/// <para>
/// Emporix marks the agentic half as preview.
/// </para>
/// </remarks>
public sealed class AiService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}";

    /// <summary>Agents — what they are, and what they may reach.</summary>
    public AiAgentOperations Agents => new(_http, _tenant);

    /// <summary>The templates an agent can be built from.</summary>
    public AiTemplateOperations Templates => new(_http, _tenant);

    /// <summary>Native tools: Slack, Teams, and retrieval over your own data.</summary>
    public AiToolOperations Tools => new(_http, _tenant);

    /// <summary>The credentials tools and MCP servers authenticate with.</summary>
    public AiTokenOperations Tokens => new(_http, _tenant);

    /// <summary>OAuth clients, for tools that need a flow rather than a key.</summary>
    public AiOAuthOperations OAuths => new(_http, _tenant);

    /// <summary>MCP servers an agent may call out to.</summary>
    public AiMcpServerOperations McpServers => new(_http, _tenant);

    /// <summary>Conversations that have been held.</summary>
    public AiConversationOperations Conversations => new(_http, _tenant);

    /// <summary>Work started asynchronously — imports, exports, chats.</summary>
    public AiJobOperations Jobs => new(_http, _tenant);

    /// <summary>What the agents actually did, request by request.</summary>
    public AiLogOperations Logs => new(_http, _tenant);

    /// <summary>Generates text from a prompt.</summary>
    /// <param name="request">The prompt and its settings.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Not repeatable: generation is billed, and two identical requests are two
    /// charges even when the answers match.
    /// </remarks>
    public async Task<GenerationResponse?> GenerateTextAsync(
        TextGenerationRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/texts",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request, AiJsonContext.Default.TextGenerationRequest),
            },
            AiJsonContext.Default.GenerationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Completes a piece of text.</summary>
    /// <param name="request">What to complete.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Not repeatable, for the same reason as <see cref="GenerateTextAsync"/>.</remarks>
    public async Task<GenerationResponse?> CompleteAsync(
        CompletionRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/completions",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request, AiJsonContext.Default.CompletionRequest),
            },
            AiJsonContext.Default.GenerationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a message to an agent and waits for the answer.</summary>
    /// <param name="request">Which agent, and what to say to it.</param>
    /// <param name="sessionId">
    /// Continues an existing conversation. Omit to start one — the answer
    /// carries the new id.
    /// </param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The answers, plural: an agent that hands off to another produces one
    /// entry per agent that spoke.
    /// </returns>
    /// <remarks>
    /// Not repeatable. Blocks until the agent is done, which for a tool-using
    /// agent can be a while — <see cref="ChatStreamAsync"/> shows progress,
    /// <see cref="StartChatAsync"/> does not wait at all.
    /// </remarks>
    public async Task<IReadOnlyList<ChatResponse>> ChatAsync(
        AgenticRequest request,
        string? sessionId = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/agentic/chat",
                Auth = Defaults.Service(auth),
                Headers = SessionHeader(sessionId),
                Content = EmporixJsonContent.Create(
                    request, AiJsonContext.Default.AgenticRequest),
            },
            AiJsonContext.Default.ListChatResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Sends a message to an agent and follows the answer as it forms.</summary>
    /// <param name="request">Which agent, and what to say to it.</param>
    /// <param name="sessionId">Continues an existing conversation.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, unread. The caller owns it and must dispose it.</returns>
    /// <remarks>
    /// <para>
    /// Server-sent events. The response body is not read here; the caller parses
    /// it with the parser <c>net10.0</c> already ships:
    /// </para>
    /// <code>
    /// using HttpResponseMessage response = await client.Ai.ChatStreamAsync(request);
    /// await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    ///
    /// await foreach (SseItem&lt;string&gt; item in SseParser.Create(stream).EnumerateAsync(cancellationToken))
    /// {
    ///     Console.Write(item.Data);
    /// }
    /// </code>
    /// <para>
    /// Why not an <c>IAsyncEnumerable</c>: ADR-0007. Also note that
    /// <see cref="EmporixHttpClient.SendRawAsync"/> leaves error statuses alone —
    /// check <see cref="HttpResponseMessage.IsSuccessStatusCode"/> first.
    /// </para>
    /// </remarks>
    public Task<HttpResponseMessage> ChatStreamAsync(
        AgenticRequest request,
        string? sessionId = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<KeyValuePair<string, string>> headers = [new("Accept", "text/event-stream")];
        headers.AddRange(SessionHeader(sessionId));

        return _http.SendRawAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/agentic/chat-stream",
                Auth = Defaults.Service(auth),
                Headers = headers,
                Content = EmporixJsonContent.Create(
                    request, AiJsonContext.Default.AgenticRequest),
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>Sends a message to an agent without waiting for the answer.</summary>
    /// <param name="request">Which agent, and what to say to it.</param>
    /// <param name="sessionId">Continues an existing conversation.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The job to follow.</returns>
    /// <remarks>
    /// Not repeatable. Follow the job with <see cref="AiJobOperations.GetAsync"/>,
    /// wrapped in <see cref="EmporixPolling.WaitForAsync"/> if you want to wait.
    /// </remarks>
    public async Task<JobIdResponse?> StartChatAsync(
        AgenticRequest request,
        string? sessionId = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/agentic/chat-async",
                Auth = Defaults.Service(auth),
                Headers = SessionHeader(sessionId),
                Content = EmporixJsonContent.Create(
                    request, AiJsonContext.Default.AgenticRequest),
            },
            AiJsonContext.Default.JobIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Uploads a file for an agent to work with.</summary>
    /// <param name="agentId">Which agent.</param>
    /// <param name="content">The file's bytes.</param>
    /// <param name="fileName">The file name to send it under.</param>
    /// <param name="contentType">The media type. Falls back to <c>application/octet-stream</c>.</param>
    /// <param name="sessionId">Attaches it to an existing conversation.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The attachment's id, to be referenced from a later message.</returns>
    /// <remarks>Not repeatable: a retry uploads the file a second time.</remarks>
    public async Task<AttachmentResponse?> UploadAttachmentAsync(
        string agentId,
        ReadOnlyMemory<byte> content,
        string fileName,
        string? contentType = null,
        string? sessionId = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        MultipartFormDataContent form = [];
        ByteArrayContent file = new(content.ToArray());
        file.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(
                contentType ?? "application/octet-stream");
        form.Add(file, "attachment", fileName);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/agentic/{Uri.EscapeDataString(agentId)}/attachments",
                Auth = Defaults.Service(auth),
                Headers = SessionHeader(sessionId),
                Content = form,
            },
            AiJsonContext.Default.AttachmentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the language models available, by provider.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ProviderModelsResponse>> ListModelsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/agentic/models",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            AiJsonContext.Default.ListProviderModelsResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the commerce events an agent can be triggered by.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CommerceEventsResponse?> ListCommerceEventsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/agentic/commerce-events",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            AiJsonContext.Default.CommerceEventsResponse,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads how the agents are doing.</summary>
    /// <param name="agentId">Narrow to one agent. All of them when omitted.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<AgentAnalyticsResponse?> GetAnalyticsAsync(
        string? agentId = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        List<KeyValuePair<string, string?>> query = [];

        if (!string.IsNullOrWhiteSpace(agentId))
        {
            query.Add(new("agentId", agentId));
        }

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/agentic/analytics",
                Auth = Defaults.Service(auth),
                Query = query,
                Idempotent = true,
            },
            AiJsonContext.Default.AgentAnalyticsResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads how often agents ran, over time.</summary>
    /// <param name="agentIds">Which agents, comma-separated. Required by the API.</param>
    /// <param name="granularity">The bucket size — quarter, month or week.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ExecutionsResponse?> GetExecutionsAsync(
        string agentIds,
        Granularity? granularity = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentIds);

        List<KeyValuePair<string, string?>> query = [new("agentIds", agentIds)];

        if (granularity is not null)
        {
            query.Add(new("granularity", granularity.Value.ToString()));
        }

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/agentic/analytics/executions",
                Auth = Defaults.Service(auth),
                Query = query,
                Idempotent = true,
            },
            AiJsonContext.Default.ExecutionsResponse,
            cancellationToken).ConfigureAwait(false);
    }

    private static List<KeyValuePair<string, string>> SessionHeader(string? sessionId)
        => string.IsNullOrWhiteSpace(sessionId) ? [] : [new("session-id", sessionId)];
}

/// <summary>
/// The query parameters the AI service takes on almost every list.
/// </summary>
/// <remarks>
/// Nine resources share this set, so it lives in one place. All of it is
/// optional, and an unset value is simply not sent.
/// </remarks>
public sealed class AiListOptions
{
    /// <summary>A standard <c>q</c> filter. Ignored by the <c>search</c> variants, which carry it in the body.</summary>
    public string? Query { get; init; }

    /// <summary>The page number.</summary>
    public int? PageNumber { get; init; }

    /// <summary>The page size.</summary>
    public int? PageSize { get; init; }

    /// <summary>Properties to sort by, separated by colons.</summary>
    public string? Sort { get; init; }

    /// <summary>Which fields to return. Everything when unset.</summary>
    public string? Fields { get; init; }

    /// <summary>
    /// Which references to resolve into whole objects rather than ids —
    /// <c>oauth</c>, <c>mcpServers</c>, <c>nativeTools</c>, <c>token</c>.
    /// </summary>
    public string? Expand { get; init; }

    /// <summary>Ask for the total count in the <c>X-Total-Count</c> header.</summary>
    public bool? TotalCount { get; init; }

    /// <summary>
    /// Which language to return localized fields in.
    /// </summary>
    /// <remarks>
    /// Left unset by default on purpose. Elsewhere in Emporix this header
    /// changes a localized field from an object to a bare string, and the
    /// generated types here expect the object.
    /// </remarks>
    public string? AcceptLanguage { get; init; }

    internal List<KeyValuePair<string, string?>> ToQuery(bool includeQ = true)
    {
        List<KeyValuePair<string, string?>> query = [];

        if (includeQ && !string.IsNullOrWhiteSpace(Query))
        {
            query.Add(new("q", Query));
        }

        if (PageNumber is not null)
        {
            query.Add(new("pageNumber", PageNumber.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (PageSize is not null)
        {
            query.Add(new("pageSize", PageSize.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(Sort))
        {
            query.Add(new("sort", Sort));
        }

        if (!string.IsNullOrWhiteSpace(Fields))
        {
            query.Add(new("fields", Fields));
        }

        if (!string.IsNullOrWhiteSpace(Expand))
        {
            query.Add(new("expand", Expand));
        }

        return query;
    }

    internal List<KeyValuePair<string, string>> ToHeaders()
    {
        List<KeyValuePair<string, string>> headers = [];

        if (TotalCount is not null)
        {
            headers.Add(new("X-Total-Count", TotalCount.Value ? "true" : "false"));
        }

        if (!string.IsNullOrWhiteSpace(AcceptLanguage))
        {
            headers.Add(new("Accept-Language", AcceptLanguage));
        }

        return headers;
    }
}

/// <summary>
/// Agents — what they are, and what they may reach.
/// </summary>
/// <remarks>
/// An agent is a prompt, a model, a set of tools and MCP servers it may use, and
/// the triggers that start it. Build one from a template with
/// <see cref="AiTemplateOperations.CreateAgentAsync"/>, then shape it here.
/// </remarks>
public sealed class AiAgentOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiAgentOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}/agentic/agents";

    /// <summary>Lists the agents.</summary>
    /// <param name="options">Paging, sorting, filtering and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AgentResponse>> ListAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.ListAgentResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the agents, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging, sorting and expansion. Its <c>Query</c> is ignored here.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A <c>POST</c> that reads: for filters too long to fit in an address. It
    /// is marked repeatable for that reason — it changes nothing.
    /// </remarks>
    public async Task<IReadOnlyList<AgentResponse>> SearchAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.ListAgentResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches one agent.</summary>
    /// <param name="agentId">Which agent.</param>
    /// <param name="options">Field selection and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<AgentResponse?> GetAsync(
        string agentId,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(agentId)}",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.AgentResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates or replaces an agent at a known id.</summary>
    /// <param name="agentId">Which agent.</param>
    /// <param name="agent">The agent, whole.</param>
    /// <param name="contentLanguage">Which language the localized fields in <paramref name="agent"/> are in.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id when the agent was created, <see langword="null"/> when it was replaced.</returns>
    public async Task<string?> ReplaceAsync(
        string agentId,
        AgentRequest agent,
        string? contentLanguage = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(agent);

        AiIdResponse? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(agentId)}",
                Auth = Defaults.Service(auth),
                Headers = AiOperations.ContentLanguage(contentLanguage),
                Content = EmporixJsonContent.Create(agent, AiJsonContext.Default.AgentRequest),
                Idempotent = true,
            },
            AiJsonContext.Default.AiIdResponse,
            cancellationToken).ConfigureAwait(false);

        return response?.Id;
    }

    /// <summary>Changes parts of an agent.</summary>
    /// <param name="agentId">Which agent.</param>
    /// <param name="operations">What to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task PatchAsync(
        string agentId,
        IEnumerable<AiPatchOperation> operations,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(operations);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(agentId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    [.. operations], AiJsonContext.Default.ListAiPatchOperation),
            },
            cancellationToken);
    }

    /// <summary>Deletes an agent.</summary>
    /// <param name="agentId">Which agent.</param>
    /// <param name="force">Delete it even though something still references it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string agentId,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(agentId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Exports agents so they can be moved to another tenant.</summary>
    /// <param name="request">Which agents.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The exported data and a checksum over it.</returns>
    /// <remarks>
    /// A <c>POST</c> that only reads, so it is marked repeatable.
    /// </remarks>
    public async Task<ExportResponse?> ExportAsync(
        ExportRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/export",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request, AiJsonContext.Default.ExportRequest),
                Idempotent = true,
            },
            AiJsonContext.Default.ExportResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Imports agents that were exported elsewhere.</summary>
    /// <param name="data">The exported data, with the checksum it came with.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What was imported, and the job that did it.</returns>
    /// <remarks>Not repeatable: importing twice creates the agents twice.</remarks>
    public async Task<ImportResponse?> ImportAsync(
        DataWithChecksum data,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/import",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    data, AiJsonContext.Default.DataWithChecksum),
            },
            AiJsonContext.Default.ImportResponse,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The templates an agent can be built from.
/// </summary>
/// <remarks>
/// Read-only, apart from the one call that turns a template into an agent.
/// </remarks>
public sealed class AiTemplateOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiTemplateOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}/agentic/templates";

    /// <summary>Lists the templates.</summary>
    /// <param name="options">Paging, sorting, filtering and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AgentTemplateResponse>> ListAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.ListAgentTemplateResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the templates, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging, sorting and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AgentTemplateResponse>> SearchAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.ListAgentTemplateResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Builds an agent from a template.</summary>
    /// <param name="templateId">Which template.</param>
    /// <param name="agent">What to change about the template while doing so.</param>
    /// <param name="contentLanguage">Which language the localized fields in <paramref name="agent"/> are in.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new agent's id.</returns>
    /// <remarks>Not repeatable: each call produces another agent.</remarks>
    public async Task<string?> CreateAgentAsync(
        string templateId,
        AgentFromTemplateRequest agent,
        string? contentLanguage = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(agent);

        AiIdResponse? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(templateId)}/agents",
                Auth = Defaults.Service(auth),
                Headers = AiOperations.ContentLanguage(contentLanguage),
                Content = EmporixJsonContent.Create(
                    agent, AiJsonContext.Default.AgentFromTemplateRequest),
            },
            AiJsonContext.Default.AiIdResponse,
            cancellationToken).ConfigureAwait(false);

        return response?.Id;
    }
}

/// <summary>
/// Native tools: Slack, Teams, and retrieval over your own data.
/// </summary>
/// <remarks>
/// <para>
/// Four kinds, and the API models them as a union: a tool is a Slack tool, a
/// Teams tool, retrieval over a custom store, or retrieval over Emporix's own
/// data. Which one a given tool is shows in its <c>type</c> field.
/// </para>
/// <para>
/// Writing is typed, one method per kind. Reading is not: the specification
/// declares the response as a <c>oneOf</c> with no discriminator the generator
/// can use, and picking one of the four would quietly drop the configuration of
/// the other three. So reads hand back the JSON — read <c>type</c>, then
/// deserialise into the matching generated type.
/// </para>
/// </remarks>
public sealed class AiToolOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiToolOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}/agentic/tools";

    /// <summary>Lists the tools.</summary>
    /// <param name="options">Paging, sorting, filtering and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A JSON array. Each element's <c>type</c> says which kind it is.</returns>
    public async Task<JsonElement> ListAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Lists the tools, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging, sorting and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<JsonElement> SearchAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one tool.</summary>
    /// <param name="toolId">Which tool.</param>
    /// <param name="options">Field selection and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The tool as JSON. Its <c>type</c> says which kind it is.</returns>
    public async Task<JsonElement> GetAsync(
        string toolId,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(toolId)}",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates or replaces a Slack tool.</summary>
    /// <param name="toolId">Which tool.</param>
    /// <param name="tool">The tool, whole.</param>
    /// <param name="force">Replace it even though something still references it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id when the tool was created, <see langword="null"/> when it was replaced.</returns>
    public Task<string?> ReplaceSlackAsync(
        string toolId,
        SlackNativeToolRequest tool,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ReplaceAsync(
            toolId,
            EmporixJsonContent.Create(tool, AiJsonContext.Default.SlackNativeToolRequest),
            force, auth, cancellationToken);

    /// <summary>Creates or replaces a Teams tool.</summary>
    /// <param name="toolId">Which tool.</param>
    /// <param name="tool">The tool, whole.</param>
    /// <param name="force">Replace it even though something still references it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id when the tool was created, <see langword="null"/> when it was replaced.</returns>
    /// <remarks>Emporix marks the Teams tool as preview.</remarks>
    public Task<string?> ReplaceTeamsAsync(
        string toolId,
        TeamsNativeToolRequest tool,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ReplaceAsync(
            toolId,
            EmporixJsonContent.Create(tool, AiJsonContext.Default.TeamsNativeToolRequest),
            force, auth, cancellationToken);

    /// <summary>Creates or replaces a retrieval tool over your own store.</summary>
    /// <param name="toolId">Which tool.</param>
    /// <param name="tool">The tool, whole.</param>
    /// <param name="force">Replace it even though something still references it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id when the tool was created, <see langword="null"/> when it was replaced.</returns>
    public Task<string?> ReplaceRagCustomAsync(
        string toolId,
        RagCustomNativeToolRequest tool,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ReplaceAsync(
            toolId,
            EmporixJsonContent.Create(tool, AiJsonContext.Default.RagCustomNativeToolRequest),
            force, auth, cancellationToken);

    /// <summary>Creates or replaces a retrieval tool over Emporix's own data.</summary>
    /// <param name="toolId">Which tool.</param>
    /// <param name="tool">The tool, whole.</param>
    /// <param name="force">Replace it even though something still references it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id when the tool was created, <see langword="null"/> when it was replaced.</returns>
    /// <remarks>
    /// What this tool retrieves over is built by the RAG indexer — see
    /// <see cref="RagIndexerService"/>.
    /// </remarks>
    public Task<string?> ReplaceRagEmporixAsync(
        string toolId,
        RagEmporixNativeToolRequest tool,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ReplaceAsync(
            toolId,
            EmporixJsonContent.Create(tool, AiJsonContext.Default.RagEmporixNativeToolRequest),
            force, auth, cancellationToken);

    /// <summary>Changes parts of a tool.</summary>
    /// <param name="toolId">Which tool.</param>
    /// <param name="operations">What to change.</param>
    /// <param name="force">Change it even though something still references it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task PatchAsync(
        string toolId,
        IEnumerable<AiPatchOperation> operations,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        ArgumentNullException.ThrowIfNull(operations);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(toolId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Content = EmporixJsonContent.Create(
                    [.. operations], AiJsonContext.Default.ListAiPatchOperation),
            },
            cancellationToken);
    }

    /// <summary>Deletes a tool.</summary>
    /// <param name="toolId">Which tool.</param>
    /// <param name="force">Delete it even though an agent still uses it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string toolId,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(toolId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Idempotent = true,
            },
            cancellationToken);
    }

    private async Task<string?> ReplaceAsync(
        string toolId,
        HttpContent content,
        bool? force,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);

        AiIdResponse? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(toolId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Content = content,
                Idempotent = true,
            },
            AiJsonContext.Default.AiIdResponse,
            cancellationToken).ConfigureAwait(false);

        return response?.Id;
    }
}

/// <summary>
/// The credentials tools and MCP servers authenticate with.
/// </summary>
/// <remarks>
/// A token's value goes in and never comes back out: reads return the id and
/// the name, not the secret.
/// </remarks>
public sealed class AiTokenOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiTokenOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}/agentic/tokens";

    /// <summary>Lists the tokens.</summary>
    /// <param name="options">Paging, sorting and filtering.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<TokenResponse>> ListAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.ListTokenResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the tokens, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging and sorting.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<TokenResponse>> SearchAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.ListTokenResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches one token's description.</summary>
    /// <param name="tokenId">Which token.</param>
    /// <param name="options">Field selection.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Not the secret — that is write-only.</remarks>
    public async Task<TokenResponse?> GetAsync(
        string tokenId,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(tokenId)}",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.TokenResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates or replaces a token.</summary>
    /// <param name="tokenId">Which token.</param>
    /// <param name="token">Its name and its secret.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id when the token was created, <see langword="null"/> when it was replaced.</returns>
    public async Task<string?> ReplaceAsync(
        string tokenId,
        TokenRequest token,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        ArgumentNullException.ThrowIfNull(token);

        AiIdResponse? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(tokenId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(token, AiJsonContext.Default.TokenRequest),
                Idempotent = true,
            },
            AiJsonContext.Default.AiIdResponse,
            cancellationToken).ConfigureAwait(false);

        return response?.Id;
    }

    /// <summary>Changes parts of a token.</summary>
    /// <param name="tokenId">Which token.</param>
    /// <param name="operations">What to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task PatchAsync(
        string tokenId,
        IEnumerable<AiPatchOperation> operations,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        ArgumentNullException.ThrowIfNull(operations);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(tokenId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    [.. operations], AiJsonContext.Default.ListAiPatchOperation),
            },
            cancellationToken);
    }

    /// <summary>Deletes a token.</summary>
    /// <param name="tokenId">Which token.</param>
    /// <param name="force">Delete it even though a tool still uses it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string tokenId,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(tokenId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Idempotent = true,
            },
            cancellationToken);
    }
}

/// <summary>
/// OAuth clients, for tools that need a flow rather than a key.
/// </summary>
public sealed class AiOAuthOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiOAuthOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}/agentic/oauths";

    /// <summary>Lists the OAuth clients.</summary>
    /// <param name="options">Paging, sorting, filtering and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<OAuthResponse>> ListAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.ListOAuthResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the OAuth clients, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging, sorting and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<OAuthResponse>> SearchAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.ListOAuthResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches one OAuth client.</summary>
    /// <param name="oauthId">Which client.</param>
    /// <param name="options">Field selection and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<OAuthResponse?> GetAsync(
        string oauthId,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oauthId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(oauthId)}",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.OAuthResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates or replaces an OAuth client.</summary>
    /// <param name="oauthId">Which client.</param>
    /// <param name="oauth">The client, whole.</param>
    /// <param name="force">Replace it even though something still references it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id when the client was created, <see langword="null"/> when it was replaced.</returns>
    public async Task<string?> ReplaceAsync(
        string oauthId,
        OAuthRequest oauth,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oauthId);
        ArgumentNullException.ThrowIfNull(oauth);

        AiIdResponse? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(oauthId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Content = EmporixJsonContent.Create(oauth, AiJsonContext.Default.OAuthRequest),
                Idempotent = true,
            },
            AiJsonContext.Default.AiIdResponse,
            cancellationToken).ConfigureAwait(false);

        return response?.Id;
    }

    /// <summary>Changes parts of an OAuth client.</summary>
    /// <param name="oauthId">Which client.</param>
    /// <param name="operations">What to change.</param>
    /// <param name="force">Change it even though something still references it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task PatchAsync(
        string oauthId,
        IEnumerable<AiPatchOperation> operations,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oauthId);
        ArgumentNullException.ThrowIfNull(operations);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(oauthId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Content = EmporixJsonContent.Create(
                    [.. operations], AiJsonContext.Default.ListAiPatchOperation),
            },
            cancellationToken);
    }

    /// <summary>Deletes an OAuth client.</summary>
    /// <param name="oauthId">Which client.</param>
    /// <param name="force">Delete it even though something still references it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string oauthId,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oauthId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(oauthId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Idempotent = true,
            },
            cancellationToken);
    }
}

/// <summary>
/// MCP servers an agent may call out to.
/// </summary>
/// <remarks>
/// Two kinds: one Emporix hosts and keeps in step with a tool list you give it,
/// and one you run yourself and merely point at. As with the tools, writing is
/// typed per kind and reading hands back JSON — the response is a <c>oneOf</c>
/// and picking a side would drop the other's fields.
/// </remarks>
public sealed class AiMcpServerOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiMcpServerOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}/agentic/mcp-servers";

    /// <summary>Lists the MCP servers.</summary>
    /// <param name="options">Paging, sorting, filtering and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A JSON array. Each element's <c>type</c> says which kind it is.</returns>
    public async Task<JsonElement> ListAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Lists the MCP servers, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging, sorting and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<JsonElement> SearchAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one MCP server.</summary>
    /// <param name="mcpServerId">Which server.</param>
    /// <param name="options">Field selection and expansion.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The server as JSON. Its <c>type</c> says which kind it is.</returns>
    public async Task<JsonElement> GetAsync(
        string mcpServerId,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpServerId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(mcpServerId)}",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates or replaces a server you run yourself.</summary>
    /// <param name="mcpServerId">Which server.</param>
    /// <param name="server">Where it is and how to reach it.</param>
    /// <param name="force">Replace it even though an agent still uses it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id when the server was created, <see langword="null"/> when it was replaced.</returns>
    public Task<string?> ReplaceCustomAsync(
        string mcpServerId,
        CustomMcpServerRequest server,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ReplaceAsync(
            mcpServerId,
            EmporixJsonContent.Create(server, AiJsonContext.Default.CustomMcpServerRequest),
            force, auth, cancellationToken);

    /// <summary>Creates or replaces a server Emporix hosts for you.</summary>
    /// <param name="mcpServerId">Which server.</param>
    /// <param name="server">The tools it should expose.</param>
    /// <param name="force">Replace it even though an agent still uses it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id when the server was created, <see langword="null"/> when it was replaced.</returns>
    public Task<string?> ReplaceDynamicAsync(
        string mcpServerId,
        DynamicMcpServerRequest server,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ReplaceAsync(
            mcpServerId,
            EmporixJsonContent.Create(server, AiJsonContext.Default.DynamicMcpServerRequest),
            force, auth, cancellationToken);

    /// <summary>Changes parts of an MCP server.</summary>
    /// <param name="mcpServerId">Which server.</param>
    /// <param name="operations">What to change.</param>
    /// <param name="force">Change it even though an agent still uses it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task PatchAsync(
        string mcpServerId,
        IEnumerable<AiPatchOperation> operations,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpServerId);
        ArgumentNullException.ThrowIfNull(operations);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(mcpServerId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Content = EmporixJsonContent.Create(
                    [.. operations], AiJsonContext.Default.ListAiPatchOperation),
            },
            cancellationToken);
    }

    /// <summary>Deletes an MCP server.</summary>
    /// <param name="mcpServerId">Which server.</param>
    /// <param name="force">Delete it even though an agent still uses it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string mcpServerId,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpServerId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(mcpServerId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Idempotent = true,
            },
            cancellationToken);
    }

    private async Task<string?> ReplaceAsync(
        string mcpServerId,
        HttpContent content,
        bool? force,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpServerId);

        AiIdResponse? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(mcpServerId)}",
                Auth = Defaults.Service(auth),
                Query = AiOperations.Force(force),
                Content = content,
                Idempotent = true,
            },
            AiJsonContext.Default.AiIdResponse,
            cancellationToken).ConfigureAwait(false);

        return response?.Id;
    }
}

/// <summary>
/// Conversations that have been held.
/// </summary>
/// <remarks>Read-only. A conversation is created by chatting, not by a call here.</remarks>
public sealed class AiConversationOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiConversationOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}/agentic/conversations";

    /// <summary>Lists the conversations.</summary>
    /// <param name="options">Paging, sorting and filtering.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ConversationResponse>> ListAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.ListConversationResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the conversations, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging and sorting.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ConversationResponse>> SearchAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.ListConversationResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }
}

/// <summary>
/// Work the AI service started and has not finished.
/// </summary>
/// <remarks>
/// Chats sent asynchronously, and agent imports and exports, all end up here.
/// </remarks>
public sealed class AiJobOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiJobOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}/jobs";

    /// <summary>Lists the jobs.</summary>
    /// <param name="options">Paging, sorting and filtering.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<Job>> ListAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.ListJob,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the jobs, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging and sorting.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<Job>> SearchAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.ListJob,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Reads how a job is going.</summary>
    /// <param name="jobId">Which job.</param>
    /// <param name="options">Field selection.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The call to poll:
    /// <code>
    /// Job? job = await EmporixPolling.WaitForAsync(
    ///     poll: ct => client.Ai.Jobs.GetAsync(jobId, cancellationToken: ct),
    ///     isComplete: j => j?.Status is not (JobStatus.PENDING or JobStatus.IN_PROGRESS));
    /// </code>
    /// </remarks>
    public async Task<Job?> GetAsync(
        string jobId,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(jobId)}",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.Job,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a job record.</summary>
    /// <param name="jobId">Which job.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Deletes the record of the work, not the work's effects.</remarks>
    public Task DeleteAsync(
        string jobId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(jobId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            cancellationToken);
    }
}

/// <summary>
/// What the agents actually did.
/// </summary>
/// <remarks>
/// Two levels. A request is one exchange — a message in, an answer out, and
/// every tool call between. A session groups the requests of one conversation.
/// </remarks>
public sealed class AiLogOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AiLogOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/ai-service/{_tenant}/agentic/logs";

    /// <summary>Lists the logged requests.</summary>
    /// <param name="options">Paging, sorting and filtering.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AgentRequestResponse>> ListRequestsAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/requests",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.ListAgentRequestResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the logged requests, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging and sorting.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AgentRequestResponse>> SearchRequestsAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/requests/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.ListAgentRequestResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches one logged request.</summary>
    /// <param name="requestId">Which request.</param>
    /// <param name="options">Field selection.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Where to look when an agent did something surprising.</remarks>
    public async Task<AgentRequestResponse?> GetRequestAsync(
        string requestId,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/requests/{Uri.EscapeDataString(requestId)}",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.AgentRequestResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the logged sessions.</summary>
    /// <param name="options">Paging, sorting and filtering.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AgentSessionResponse>> ListSessionsAsync(
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/sessions",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery() ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.ListAgentSessionResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists the logged sessions, with the filter in the body.</summary>
    /// <param name="query">The <c>q</c> filter.</param>
    /// <param name="options">Paging and sorting.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AgentSessionResponse>> SearchSessionsAsync(
        string query,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/sessions/search",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Content = EmporixJsonContent.Create(
                    new QParamSearchBody { Q = query }, AiJsonContext.Default.QParamSearchBody),
                Idempotent = true,
            },
            AiJsonContext.Default.ListAgentSessionResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches one logged session.</summary>
    /// <param name="sessionId">Which session.</param>
    /// <param name="options">Field selection.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<AgentSessionResponse?> GetSessionAsync(
        string sessionId,
        AiListOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/sessions/{Uri.EscapeDataString(sessionId)}",
                Auth = Defaults.Service(auth),
                Query = options?.ToQuery(includeQ: false) ?? [],
                Headers = options?.ToHeaders() ?? [],
                Idempotent = true,
            },
            AiJsonContext.Default.AgentSessionResponse,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Bits the AI operation groups share.</summary>
internal static class AiOperations
{
    /// <summary>The <c>force</c> flag, sent only when the caller set it.</summary>
    public static List<KeyValuePair<string, string?>> Force(bool? force)
        => force is null ? [] : [new("force", force.Value ? "true" : "false")];

    /// <summary>The <c>Content-Language</c> header, sent only when the caller set it.</summary>
    public static List<KeyValuePair<string, string>> ContentLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) ? [] : [new("Content-Language", language)];
}
