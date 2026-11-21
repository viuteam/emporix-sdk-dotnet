using System.Globalization;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Webhooks — Emporix calling out when something happens.
/// </summary>
/// <remarks>
/// <para>
/// A configuration says where to deliver and which events to send. The
/// subscriptions are what a tenant is actually signed up to; the dashboard and
/// statistics belong to the delivery provider behind it.
/// </para>
/// <para>
/// A delivery target that keeps failing is switched off: by default after 100
/// failures in a row, Emporix stops calling it, sets <c>suspendedAt</c> on its
/// configuration or entry, and stores its events as
/// <see cref="FailedMessages"/> instead. Setting <c>active</c> back to
/// <see langword="true"/> through <see cref="ReplaceAsync"/> or
/// <see cref="UpdateAsync"/> switches it on again.
/// </para>
/// </remarks>
public sealed class WebhookService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal WebhookService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/webhook/{_tenant}/config";

    /// <summary>The events stored for a switched-off delivery target.</summary>
    public WebhookFailedMessageOperations FailedMessages => new(_http, _tenant);

    /// <summary>The jobs that restart failed messages.</summary>
    public WebhookJobOperations Jobs => new(_http, _tenant);

    /// <summary>Lists the webhook configurations.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<WebhookModels.WebhookConfigListItem>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
            },
            WebhookJsonContext.Default.ListWebhookConfigListItem,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a webhook configuration.</summary>
    /// <param name="code">The configuration code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookModels.WebhookConfig?> GetAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
            },
            WebhookJsonContext.Default.WebhookConfig,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a webhook configuration.</summary>
    /// <param name="configuration">Where to deliver, and what.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Deliveries start once this exists, so the endpoint should be ready to
    /// receive before the configuration is created rather than after.
    /// </remarks>
    public Task CreateAsync(
        WebhookModels.WebhookConfigCreation configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration,
                    WebhookJsonContext.Default.WebhookConfigCreation),
            },
            cancellationToken);
    }

    /// <summary>Replaces a webhook configuration.</summary>
    /// <param name="code">The configuration code.</param>
    /// <param name="configuration">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string code,
        WebhookModels.WebhookConfigUpdate configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(configuration);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration,
                    WebhookJsonContext.Default.WebhookConfigUpdate),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a webhook configuration.</summary>
    /// <param name="code">The configuration code.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string code,
        IEnumerable<WebhookModels.WebhookConfigPartialUpdate> changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(changes);

        List<WebhookModels.WebhookConfigPartialUpdate> body = [.. changes];
        ArgumentOutOfRangeException.ThrowIfZero(body.Count, nameof(changes));

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    body,
                    WebhookJsonContext.Default.ListWebhookConfigPartialUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a webhook configuration.</summary>
    /// <param name="code">The configuration code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Deliveries stop; anything already queued may still arrive.</remarks>
    public Task DeleteAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(code)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Reads which events the tenant is subscribed to.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookModels.WebhookSubscriptions?> GetSubscriptionsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/webhook/{_tenant}/event-subscriptions",
                Auth = Defaults.Service(auth),
            },
            WebhookJsonContext.Default.WebhookSubscriptions,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Changes which events the tenant is subscribed to.</summary>
    /// <param name="subscriptions">The subscriptions to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateSubscriptionsAsync(
        WebhookModels.WebhookSubscriptions subscriptions,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"/webhook/{_tenant}/event-subscriptions",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    subscriptions,
                    WebhookJsonContext.Default.WebhookSubscriptions),
            },
            cancellationToken);
    }

    /// <summary>Fetches a link into the delivery provider's dashboard.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, undisposed — the caller owns it.</returns>
    /// <remarks>
    /// The answer is a short-lived access link rather than data, and its shape
    /// belongs to the provider rather than to Emporix, so it comes back raw.
    /// </remarks>
    public Task<HttpResponseMessage> GetDashboardAccessAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _http.SendRawAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/webhook/{_tenant}/dashboard-access",
                Auth = Defaults.Service(auth),
            },
            cancellationToken: cancellationToken);

    /// <summary>Fetches delivery statistics.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The shape is the provider's and undocumented in the specification, so it
    /// comes back as raw JSON rather than a guessed model.
    /// </remarks>
    public async Task<System.Text.Json.JsonElement> GetStatisticsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/webhook/{_tenant}/statistics",
                Auth = Defaults.Service(auth),
            },
            WebhookJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
}

/// <summary>
/// The events stored for a delivery target Emporix switched off.
/// </summary>
/// <remarks>
/// <para>
/// Reached through <see cref="WebhookService.FailedMessages"/>. Emporix keeps
/// them for 14 days on production and one day on preprod. Each can be sent
/// again, restarted as part of a selection, or deleted.
/// </para>
/// <para>
/// Sending again goes to the target's current configuration and counts towards
/// its failure limit, so switch the target back on first: a message whose
/// configuration or entry is not active is not sent.
/// </para>
/// </remarks>
public sealed class WebhookFailedMessageOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal WebhookFailedMessageOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/webhook/{_tenant}/failed-messages";

    /// <summary>Fetches one page of failed messages, without their payloads.</summary>
    /// <param name="query">
    /// An optional Emporix filter over any field but the payload, for example
    /// <c>configCode:orders</c>. Date values go in quotation marks.
    /// </param>
    /// <param name="sort">
    /// Fields to sort by, such as <c>failedAt:asc</c>. Emporix sorts newest first
    /// when omitted.
    /// </param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size, at most 2,000.</param>
    /// <param name="includeTotalCount">Ask Emporix for the total number of matches.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>A list never carries the payload; <see cref="GetAsync"/> does.</remarks>
    public async Task<PaginatedItems<WebhookModels.FailedMessage>> ListAsync(
        string? query = null,
        string? sort = null,
        int pageNumber = 1,
        int pageSize = 60,
        bool includeTotalCount = false,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query =
                [
                    new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
                    new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                    new("q", query),
                    new("sort", sort),
                ],
                Headers = includeTotalCount ? [new("X-Total-Count", "true")] : null,
                Idempotent = true,
            },
            WebhookJsonContext.Default.ListFailedMessage,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a failed message, payload included.</summary>
    /// <param name="failedMessageId">The failed message id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No failed message exists with this id.</exception>
    public async Task<WebhookModels.FailedMessage?> GetAsync(
        string failedMessageId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failedMessageId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(failedMessageId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            WebhookJsonContext.Default.FailedMessage,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends one failed message again and removes it from the store.</summary>
    /// <param name="failedMessageId">The failed message id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// The delivery happens after the call returns. Emporix answers <c>409</c>
    /// when the message's configuration or entry is not active.
    /// </para>
    /// <para>
    /// Not repeatable: it hands an event to someone else's endpoint.
    /// </para>
    /// </remarks>
    public Task ResendAsync(
        string failedMessageId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failedMessageId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(failedMessageId)}/resend",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Starts a job that sends the selected failed messages again.</summary>
    /// <param name="selection">
    /// Which messages, every criterion combined with AND. Omitted or empty, it
    /// selects every stored message.
    /// </param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The job's id, to follow with <see cref="WebhookJobOperations.GetAsync"/>.</returns>
    /// <remarks>
    /// <para>
    /// The job takes only the messages stored before it started, and skips and
    /// keeps any whose configuration or entry is not active. One restart job runs
    /// per tenant at a time; starting another answers <c>409</c>.
    /// </para>
    /// <para>
    /// Not repeatable, for the same reason <see cref="ResendAsync"/> is not.
    /// </para>
    /// </remarks>
    public async Task<WebhookModels.JobId?> RestartAsync(
        WebhookModels.FailedMessageSelection? selection = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/restart",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    selection ?? new WebhookModels.FailedMessageSelection(),
                    WebhookJsonContext.Default.FailedMessageSelection),
            },
            WebhookJsonContext.Default.JobId,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes a failed message without sending it.</summary>
    /// <param name="failedMessageId">The failed message id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string failedMessageId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failedMessageId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(failedMessageId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Deletes every failed message the selection matches, without sending them.</summary>
    /// <param name="selection">Which messages, every criterion combined with AND.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// A selection with no criterion is refused here. Emporix reads it as every
    /// stored message, which is <see cref="DeleteAllAsync"/> and should be asked
    /// for by name rather than reached through a filter that came out empty.
    /// </para>
    /// <para>
    /// Not repeatable: messages that arrive between two attempts and match the
    /// selection would go as well.
    /// </para>
    /// </remarks>
    public Task DeleteMatchingAsync(
        WebhookModels.FailedMessageSelection selection,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (SelectsEverything(selection))
        {
            throw new ArgumentException(
                "The selection has no criterion, which Emporix reads as every stored message. "
                + "Use DeleteAllAsync to delete them all.",
                nameof(selection));
        }

        return DeleteSelectionAsync(selection, auth, cancellationToken);
    }

    /// <summary>Deletes every stored failed message, without sending any.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The events are lost; nothing undoes this.</remarks>
    public Task DeleteAllAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => DeleteSelectionAsync(new WebhookModels.FailedMessageSelection(), auth, cancellationToken);

    private Task DeleteSelectionAsync(
        WebhookModels.FailedMessageSelection selection,
        AuthContext auth,
        CancellationToken cancellationToken)
        => _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/delete",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    selection,
                    WebhookJsonContext.Default.FailedMessageSelection),
            },
            cancellationToken);

    // Blank strings count as absent: a filter value that came out empty must
    // not turn a delete into a delete of everything.
    private static bool SelectsEverything(WebhookModels.FailedMessageSelection selection)
        => (selection.Ids is null || selection.Ids.Count == 0)
            && selection.TargetType is null
            && string.IsNullOrWhiteSpace(selection.ConfigCode)
            && string.IsNullOrWhiteSpace(selection.EntryId)
            && string.IsNullOrWhiteSpace(selection.EventType);
}

/// <summary>
/// The jobs that restart failed messages.
/// </summary>
/// <remarks>
/// Reached through <see cref="WebhookService.Jobs"/>. A job comes from
/// <see cref="WebhookFailedMessageOperations.RestartAsync"/>, and this is where
/// its progress is read.
/// </remarks>
public sealed class WebhookJobOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal WebhookJobOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/webhook/{_tenant}/jobs";

    /// <summary>Fetches one page of the tenant's jobs.</summary>
    /// <param name="sort">Fields to sort by. Emporix returns the newest first when omitted.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size, at most 2,000.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<WebhookModels.Job>> ListAsync(
        string? sort = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query =
                [
                    new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
                    new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                    new("sort", sort),
                ],
                Idempotent = true,
            },
            WebhookJsonContext.Default.ListJob,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads how a job is going.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No job exists with this id.</exception>
    /// <remarks>
    /// The call to poll:
    /// <code>
    /// Job? job = await EmporixPolling.WaitForAsync(
    ///     poll: ct => client.Webhooks.Jobs.GetAsync(jobId, cancellationToken: ct),
    ///     isComplete: j => j?.Status is not JobStatus.In_progress);
    /// </code>
    /// </remarks>
    public async Task<WebhookModels.Job?> GetAsync(
        string jobId,
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
                Idempotent = true,
            },
            WebhookJsonContext.Default.Job,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Units of measure, and converting between them.
/// </summary>
/// <remarks>
/// Products are sold by piece, kilogram or litre; this defines those units and
/// the factors that relate them. <see cref="ConvertAsync"/> is what turns «2 kg»
/// into «2000 g» when a price is quoted per gram.
/// </remarks>
public sealed class UnitService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal UnitService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/unit-handling/{_tenant}/units";

    /// <summary>Lists the units.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<UnitHandlingServiceModels.Unit>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Anonymous(auth),
            },
            UnitJsonContext.Default.ListUnit,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a unit.</summary>
    /// <param name="unitCode">The unit code, for example <c>kg</c>.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<UnitHandlingServiceModels.Unit?> GetAsync(
        string unitCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitCode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(unitCode)}",
                Auth = Defaults.Anonymous(auth),
            },
            UnitJsonContext.Default.Unit,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a unit.</summary>
    /// <param name="unit">The unit to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        UnitHandlingServiceModels.BaseUnit unit,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(unit, UnitJsonContext.Default.BaseUnit),
            },
            cancellationToken);
    }

    /// <summary>Replaces a unit.</summary>
    /// <param name="unitCode">The unit code.</param>
    /// <param name="unit">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string unitCode,
        UnitHandlingServiceModels.UpdateUnit unit,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitCode);
        ArgumentNullException.ThrowIfNull(unit);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(unitCode)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(unit, UnitJsonContext.Default.UpdateUnit),
            },
            cancellationToken);
    }

    /// <summary>Deletes a unit.</summary>
    /// <param name="unitCode">The unit code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Products sold in it keep a unit code that no longer resolves.</remarks>
    public Task DeleteAsync(
        string unitCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitCode);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(unitCode)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Deletes every unit.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Every product sold by measure loses the unit it is priced in, so this is
    /// a setup call rather than an operational one.
    /// </remarks>
    public Task DeleteAllAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = BasePath,
                Auth = Defaults.Service(auth),
            },
            cancellationToken);

    /// <summary>Lists the unit types.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Weight, volume, length — what a unit can measure.</remarks>
    public async Task<System.Text.Json.JsonElement> ListTypesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/unit-handling/{_tenant}/types",
                Auth = Defaults.Anonymous(auth),
            },
            UnitJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads the factor between two units.</summary>
    /// <param name="request">Which units.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A <c>PUT</c> that computes rather than changes — Emporix models this as a
    /// command, which is why the verb looks wrong for a read. Declared
    /// repeatable accordingly.
    /// </remarks>
    public async Task<UnitHandlingServiceModels.ConversionFactorResponse?> GetConversionFactorAsync(
        UnitHandlingServiceModels.ConversionFactorPayload request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/conversion-factor-commands",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    UnitJsonContext.Default.ConversionFactorPayload),
                Idempotent = true,
            },
            UnitJsonContext.Default.ConversionFactorResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Converts a quantity from one unit to another.</summary>
    /// <param name="request">What to convert.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Also a command-shaped read, and repeatable for the same reason.</remarks>
    public async Task<UnitHandlingServiceModels.ConversionResponse?> ConvertAsync(
        UnitHandlingServiceModels.ConversionPayload request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/convert-unit-commands",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    UnitJsonContext.Default.ConversionPayload),
                Idempotent = true,
            },
            UnitJsonContext.Default.ConversionResponse,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Sequential identifiers — order numbers and the like.
/// </summary>
/// <remarks>
/// <para>
/// A schema defines the shape of a number; asking for the next one advances a
/// counter. <b>Nothing here is repeatable:</b> a retried request consumes a
/// second identifier, which leaves a gap in a sequence somebody is probably
/// relying on being gapless.
/// </para>
/// <para>
/// Only one schema per type is active at a time, which is what
/// <see cref="ActivateAsync"/> decides.
/// </para>
/// </remarks>
public sealed class SequentialIdService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal SequentialIdService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/sequential-id/{_tenant}/schemas";

    /// <summary>Lists the schemas.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<SequentialIdModels.SequenceSchema>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
            },
            SequentialIdJsonContext.Default.ListSequenceSchema,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a schema.</summary>
    /// <param name="schemaId">The schema id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SequentialIdModels.SequenceSchema?> GetAsync(
        string schemaId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(schemaId)}",
                Auth = Defaults.Service(auth),
            },
            SequentialIdJsonContext.Default.SequenceSchema,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches the active schema for one type.</summary>
    /// <param name="schemaType">The type, for example an order number.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SequentialIdModels.SequenceSchema?> GetActiveAsync(
        string schemaType,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaType);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/types/{Uri.EscapeDataString(schemaType)}",
                Auth = Defaults.Service(auth),
            },
            SequentialIdJsonContext.Default.SequenceSchema,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a schema.</summary>
    /// <param name="schema">The schema to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Creating does not activate — call <see cref="ActivateAsync"/> for that.</remarks>
    public Task CreateAsync(
        SequentialIdModels.SequenceSchemaCreate schema,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    schema,
                    SequentialIdJsonContext.Default.SequenceSchemaCreate),
            },
            cancellationToken);
    }

    /// <summary>Makes a schema the active one for its type.</summary>
    /// <param name="schemaId">The schema id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The previously active schema for that type stops being used. Numbers
    /// already issued keep their old shape, so a sequence can change format
    /// mid-stream.
    /// </remarks>
    public Task ActivateAsync(
        string schemaId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(schemaId)}/setActive",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Deletes a schema.</summary>
    /// <param name="schemaId">The schema id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string schemaId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(schemaId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Takes the next identifier of a type.</summary>
    /// <param name="schemaType">The type.</param>
    /// <param name="request">Values for the schema's placeholders.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <b>Consumes a number.</b> Deliberately not repeatable: a retry burns a
    /// second identifier and leaves a gap in a sequence that is often expected
    /// to have none.
    /// </remarks>
    public async Task<SequentialIdModels.NextIdResponse?> NextAsync(
        string schemaType,
        SequentialIdModels.NextIdCommandRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaType);
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/types/{Uri.EscapeDataString(schemaType)}/nextId",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    SequentialIdJsonContext.Default.NextIdCommandRequest),
            },
            SequentialIdJsonContext.Default.NextIdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Takes the next identifier for several schemas at once.</summary>
    /// <param name="request">Which schemas, and their placeholder values.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Consumes a number from each. Note the address: this one is not
    /// tenant-scoped, which is Emporix's shape rather than an omission here.
    /// </remarks>
    public async Task<SequentialIdModels.SchemaBatchNextIdResponse?> NextManyAsync(
        SequentialIdModels.SchemaBatchNextIdRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = "/sequential-id/sequenceSchemaBatch/nextIds",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    SequentialIdJsonContext.Default.SchemaBatchNextIdRequest),
            },
            SequentialIdJsonContext.Default.SchemaBatchNextIdResponse,
            cancellationToken).ConfigureAwait(false);
    }
}
