using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.PickPackModels;

namespace Viu.Emporix;

/// <summary>
/// Pick and pack — the warehouse side of an order.
/// </summary>
/// <remarks>
/// <para>
/// Orders are worked through by delivery date: fetch the cycles, fetch that
/// day's packlist, assign a packer, report what was picked, then finish.
/// </para>
/// <para>
/// Several calls need a second credential besides the token — a
/// <c>saas-token</c> header that identifies the packer, issued by Emporix and
/// signed. It is required, so it is a plain parameter rather than something
/// hidden in options: a call that changes an order has to say who changed it.
/// </para>
/// </remarks>
public sealed class PickPackService
{
    private const string SaasTokenHeader = "saas-token";

    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal PickPackService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/pick-pack/{_tenant}";

    /// <summary>Lists the delivery dates that have orders.</summary>
    /// <param name="siteCode">Which site. Required.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The dates, as the API spells them.</returns>
    /// <remarks>Where a packing day starts: pick a date, then call <see cref="ListOrdersAsync"/>.</remarks>
    public async Task<IReadOnlyList<string>> ListOrderCyclesAsync(
        string siteCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/orderCycles",
                Auth = Defaults.Service(auth),
                Query = [new("siteCode", siteCode)],
                Idempotent = true,
            },
            PickPackJsonContext.Default.ListString,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches the packlist for one delivery date.</summary>
    /// <param name="siteCode">Which site. Required.</param>
    /// <param name="deliveryDate">The date, as <c>YYYY-MM-DD</c>. Required.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<OrderList>> ListOrdersAsync(
        string siteCode,
        string deliveryDate,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryDate);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/orders",
                Auth = Defaults.Service(auth),
                Query = [new("siteCode", siteCode), new("deliveryDate", deliveryDate)],
                Idempotent = true,
            },
            PickPackJsonContext.Default.ListOrderList,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches one order in full.</summary>
    /// <param name="orderId">Which order.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Everything a packer needs: entries, customer, delivery, packaging.</remarks>
    public async Task<Order?> GetOrderAsync(
        string orderId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/orders/{Uri.EscapeDataString(orderId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            PickPackJsonContext.Default.Order,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes an order's packing status.</summary>
    /// <param name="orderId">Which order.</param>
    /// <param name="status">The status to move it to.</param>
    /// <param name="saasToken">The packer's <c>saas-token</c>. Required by the API.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The API's confirmation message, if it sent one.</returns>
    public async Task<string?> UpdateOrderStatusAsync(
        string orderId,
        OrderStatusChange status,
        string saasToken,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(saasToken);

        Response? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/orders/{Uri.EscapeDataString(orderId)}",
                Auth = Defaults.Service(auth),
                Headers = [new(SaasTokenHeader, saasToken)],
                Content = EmporixJsonContent.Create(
                    status, PickPackJsonContext.Default.OrderStatusChange),
            },
            PickPackJsonContext.Default.Response,
            cancellationToken).ConfigureAwait(false);

        return response?.Message;
    }

    /// <summary>Closes an order for packing.</summary>
    /// <param name="orderId">Which order.</param>
    /// <param name="saasToken">The packer's <c>saas-token</c>. Required by the API.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The API's confirmation message, if it sent one.</returns>
    /// <remarks>
    /// Not repeatable. Finishing an order is what releases it to delivery, and
    /// the SDK has no way to tell a repeat from a second, deliberate attempt.
    /// </remarks>
    public async Task<string?> FinishOrderAsync(
        string orderId,
        string saasToken,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(saasToken);

        Response? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/orders/{Uri.EscapeDataString(orderId)}/finish",
                Auth = Defaults.Service(auth),
                Headers = [new(SaasTokenHeader, saasToken)],
            },
            PickPackJsonContext.Default.Response,
            cancellationToken).ConfigureAwait(false);

        return response?.Message;
    }

    /// <summary>Puts a packer on an order.</summary>
    /// <param name="orderId">Which order.</param>
    /// <param name="assignee">Who is packing it.</param>
    /// <param name="force">
    /// Replace an existing assignee. Emporix defaults this to <see langword="true"/>;
    /// pass <see langword="false"/> to be told about a clash instead of overwriting it.
    /// </param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The API's confirmation message, if it sent one.</returns>
    public async Task<string?> AddAssigneeAsync(
        string orderId,
        Assignee assignee,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(assignee);

        List<KeyValuePair<string, string?>> query = [];

        if (force is not null)
        {
            query.Add(new("force", force.Value ? "true" : "false"));
        }

        Response? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/orders/{Uri.EscapeDataString(orderId)}/assignees",
                Auth = Defaults.Service(auth),
                Query = query,
                Content = EmporixJsonContent.Create(
                    assignee, PickPackJsonContext.Default.Assignee),
            },
            PickPackJsonContext.Default.Response,
            cancellationToken).ConfigureAwait(false);

        return response?.Message;
    }

    /// <summary>Takes a packer off an order.</summary>
    /// <param name="orderId">Which order.</param>
    /// <param name="assigneeId">Which packer.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RemoveAssigneeAsync(
        string orderId,
        string assigneeId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assigneeId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path =
                    $"{BasePath}/orders/{Uri.EscapeDataString(orderId)}/assignees/{Uri.EscapeDataString(assigneeId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Records how many of each entry went into packaging.</summary>
    /// <param name="orderId">Which order.</param>
    /// <param name="packaging">One entry per line packed.</param>
    /// <param name="saasToken">The packer's <c>saas-token</c>. Required by the API.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The API's confirmation message, if it sent one.</returns>
    /// <remarks>
    /// A <c>PUT</c> of the whole set: what is not in the list is not packed.
    /// </remarks>
    public async Task<string?> UpdatePackagingAsync(
        string orderId,
        IEnumerable<PackagingProductsChange> packaging,
        string saasToken,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(packaging);
        ArgumentException.ThrowIfNullOrWhiteSpace(saasToken);

        Response? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/orders/{Uri.EscapeDataString(orderId)}/packaging",
                Auth = Defaults.Service(auth),
                Headers = [new(SaasTokenHeader, saasToken)],
                Content = EmporixJsonContent.Create(
                    [.. packaging], PickPackJsonContext.Default.ListPackagingProductsChange),
                Idempotent = true,
            },
            PickPackJsonContext.Default.Response,
            cancellationToken).ConfigureAwait(false);

        return response?.Message;
    }

    /// <summary>Reports a picking or packing event.</summary>
    /// <param name="entryEvent">What happened to which order entry.</param>
    /// <param name="saasToken">The packer's <c>saas-token</c>. Required by the API.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The API's confirmation message, if it sent one.</returns>
    /// <remarks>
    /// Carries its own <c>eventId</c>, and a repeat of one already seen answers
    /// <c>409</c> — so a scanner that loses its connection can send the same
    /// event again without double-counting. That is Emporix's deduplication, not
    /// the SDK's retry: the call is still not marked repeatable, because a
    /// <c>409</c> is a failure to the caller.
    /// </remarks>
    public async Task<string?> CreateEventAsync(
        OrderEntryEventCreate entryEvent,
        string saasToken,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(saasToken);

        Response? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/events",
                Auth = Defaults.Service(auth),
                Headers = [new(SaasTokenHeader, saasToken)],
                Content = EmporixJsonContent.Create(
                    entryEvent, PickPackJsonContext.Default.OrderEntryEventCreate),
            },
            PickPackJsonContext.Default.Response,
            cancellationToken).ConfigureAwait(false);

        return response?.Message;
    }

    /// <summary>Lists events after a point in time.</summary>
    /// <param name="timestamp">Only events after this, as <c>yyyy-MM-dd'T'HH:mm:ssZ</c>.</param>
    /// <param name="pageNumber">The page number.</param>
    /// <param name="pageSize">The page size, at most 20000.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>How a warehouse display catches up after being offline.</remarks>
    public async Task<IReadOnlyList<OrderEntryEventResponse>> ListEventsAsync(
        string? timestamp = null,
        int? pageNumber = null,
        int? pageSize = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(pageNumber.Value);
        }

        if (pageSize is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(pageSize.Value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize.Value, 20000);
        }

        List<KeyValuePair<string, string?>> query = [];

        if (!string.IsNullOrWhiteSpace(timestamp))
        {
            query.Add(new("timestamp", timestamp));
        }

        if (pageNumber is not null)
        {
            query.Add(new("pageNumber", pageNumber.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (pageSize is not null)
        {
            query.Add(new("pageSize", pageSize.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/events",
                Auth = Defaults.Service(auth),
                Query = query,
                Idempotent = true,
            },
            PickPackJsonContext.Default.ListOrderEntryEventResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Starts a recalculation job over some orders.</summary>
    /// <param name="job">Which orders, and what kind of recalculation.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The job's id.</returns>
    /// <remarks>
    /// Not repeatable, and Emporix answers <c>409</c> while an equivalent job is
    /// already running. Follow it with
    /// <see cref="EmporixPolling.WaitForAsync"/> over
    /// <see cref="GetRecalculationJobAsync"/>.
    /// </remarks>
    public async Task<string?> StartRecalculationJobAsync(
        RecalculationJobCreation job,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        Response4? response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/jobs/recalculations",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    job, PickPackJsonContext.Default.RecalculationJobCreation),
            },
            PickPackJsonContext.Default.Response4,
            cancellationToken).ConfigureAwait(false);

        return response?.JobId;
    }

    /// <summary>Reads how a recalculation job is going.</summary>
    /// <param name="jobId">Which job.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The call to poll. Note that this one spells the field <c>JobStatus</c>
    /// where the other job endpoints say <c>Status</c> — which is why there is a
    /// polling helper and not a job abstraction (ADR-0008).
    /// </remarks>
    public async Task<RecalculationJob?> GetRecalculationJobAsync(
        string jobId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/jobs/recalculations/{Uri.EscapeDataString(jobId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            PickPackJsonContext.Default.RecalculationJob,
            cancellationToken).ConfigureAwait(false);
    }
}
