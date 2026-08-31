using System.Globalization;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Returns — the other half of an order.
/// </summary>
/// <remarks>
/// A return belongs to an order and, through it, to a customer. Emporix answers
/// a customer's read with the customer view and an employee's with a wider one;
/// the SDK reads the wider type either way, so the employee-only fields are
/// simply absent rather than needing a second method.
/// </remarks>
public sealed class ReturnService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal ReturnService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/return/{_tenant}/returns";

    /// <summary>Fetches a return.</summary>
    /// <param name="returnId">The return id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No such return.</exception>
    public async Task<ReturnsModels.FullEmployeeReturn?> GetAsync(
        string returnId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(returnId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(returnId)}",
                Auth = Defaults.Service(auth),
            },
            ReturnJsonContext.Default.FullEmployeeReturn,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of returns.</summary>
    /// <param name="query">An optional Emporix filter.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// With a customer's own token this lists that customer's returns; with a
    /// service token, the tenant's. Emporix decides from the token, so there is
    /// no customer parameter.
    /// </remarks>
    public async Task<PaginatedItems<ReturnsModels.FullEmployeeReturn>> ListAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            new("q", query),
        ];

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = parameters,
            },
            ReturnJsonContext.Default.ListFullEmployeeReturn,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Registers a return.</summary>
    /// <param name="request">What is coming back.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Deliberately not repeatable: a retried create is a second return against
    /// the same order.
    /// </remarks>
    public async Task<ReturnsModels.ReturnId?> CreateAsync(
        ReturnsModels.BasicEmployeeReturn request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    ReturnJsonContext.Default.BasicEmployeeReturn),
            },
            ReturnJsonContext.Default.ReturnId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a return.</summary>
    /// <param name="returnId">The return id.</param>
    /// <param name="request">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string returnId,
        ReturnsModels.UpdateEmployeeReturn request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(returnId);
        ArgumentNullException.ThrowIfNull(request);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(returnId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    ReturnJsonContext.Default.UpdateEmployeeReturn),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a return.</summary>
    /// <param name="returnId">The return id.</param>
    /// <param name="operations">The JSON Patch operations to apply.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// JSON Patch, so the operation names are lowercase — the specification says
    /// uppercase and the API rejects that, which the spec patch for the approval
    /// service documents for the same reason.
    /// </remarks>
    public Task UpdateAsync(
        string returnId,
        IEnumerable<ReturnsModels.PatchOperation> operations,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(returnId);
        ArgumentNullException.ThrowIfNull(operations);

        List<ReturnsModels.PatchOperation> body = [.. operations];
        ArgumentOutOfRangeException.ThrowIfZero(body.Count, nameof(operations));

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(returnId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    body,
                    ReturnJsonContext.Default.ListPatchOperation),
            },
            cancellationToken);
    }

    /// <summary>Deletes a return.</summary>
    /// <param name="returnId">The return id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string returnId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(returnId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(returnId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Invoice generation, as a background job.
/// </summary>
/// <remarks>
/// Invoicing is asynchronous: a request starts a job and answers with its id.
/// Nothing here waits for it — poll <see cref="GetJobAsync"/>, or react to the
/// webhook if the tenant has one configured.
/// </remarks>
public sealed class InvoiceService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal InvoiceService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/invoice/{_tenant}/jobs/invoices";

    /// <summary>Starts an invoicing job.</summary>
    /// <param name="request">Which orders to invoice.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Deliberately not repeatable: a retried start is a second job over the
    /// same orders, and an order invoiced twice is a real problem.
    /// </remarks>
    public async Task<InvoiceModels.JobCreationResponse?> CreateJobAsync(
        InvoiceModels.JobRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    InvoiceJsonContext.Default.JobRequest),
            },
            InvoiceJsonContext.Default.JobCreationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads how far an invoicing job has got.</summary>
    /// <param name="jobId">The job id from <see cref="CreateJobAsync"/>.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<InvoiceModels.JobStatusResponse?> GetJobAsync(
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
            },
            InvoiceJsonContext.Default.JobStatusResponse,
            cancellationToken).ConfigureAwait(false);
    }
}
