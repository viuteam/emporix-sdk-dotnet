using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.ApprovalServiceModels;

namespace Viu.Emporix;

/// <summary>
/// Approvals — a B2B cart or quote waiting for somebody to say yes.
/// </summary>
/// <remarks>
/// <para>
/// A buyer with a spending limit does not place an order; they raise an approval
/// and someone above them decides. <see cref="IsPermittedAsync"/> is the call a
/// checkout makes to find out which of the two is about to happen.
/// </para>
/// <para>
/// The JSON Patch operations this service accepts are lowercase. The
/// specification says uppercase and the API rejects that — repaired in the
/// generation pipeline rather than worked around here.
/// </para>
/// </remarks>
public sealed class ApprovalService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal ApprovalService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/approval/{_tenant}/approvals";

    /// <summary>Fetches an approval.</summary>
    /// <param name="approvalId">The approval id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<GetApprovalResponse?> GetAsync(
        string approvalId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(approvalId)}",
                Auth = Defaults.Service(auth),
            },
            ApprovalJsonContext.Default.GetApprovalResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of approvals.</summary>
    /// <param name="query">An optional Emporix filter, for example by status.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// An approver's queue is this list filtered to what they may decide;
    /// Emporix applies that from the token rather than from a parameter.
    /// </remarks>
    public async Task<PaginatedItems<GetApprovalResponse>> ListAsync(
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
            ApprovalJsonContext.Default.ListGetApprovalResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Raises an approval for a cart.</summary>
    /// <param name="request">Which cart, and who is asking.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Not repeatable: a retry puts the same cart in the approver's queue twice.
    /// </remarks>
    public async Task<CreatedResource?> CreateForCartAsync(
        CreateCartApprovalRequest request,
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
                    ApprovalJsonContext.Default.CreateCartApprovalRequest),
            },
            ApprovalJsonContext.Default.CreatedResource,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Raises an approval for a quote.</summary>
    /// <param name="request">Which quote, and who is asking.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Same endpoint as the cart form; the body decides which it is.</remarks>
    public async Task<CreatedResource?> CreateForQuoteAsync(
        CreateQuoteApprovalRequest request,
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
                    ApprovalJsonContext.Default.CreateQuoteApprovalRequest),
            },
            ApprovalJsonContext.Default.CreatedResource,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Decides an approval.</summary>
    /// <param name="approvalId">The approval id.</param>
    /// <param name="decision">What to change — normally the status.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixForbiddenException">
    /// The caller is not entitled to decide this one.
    /// </exception>
    /// <remarks>
    /// Approving releases the cart or quote to be ordered. Whether the caller
    /// may is Emporix's decision, taken from the token and the entity's
    /// hierarchy.
    /// </remarks>
    public Task DecideAsync(
        string approvalId,
        UpdateApprovalRequest decision,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        ArgumentNullException.ThrowIfNull(decision);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(approvalId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    decision,
                    ApprovalJsonContext.Default.UpdateApprovalRequest),
            },
            cancellationToken);
    }

    /// <summary>Withdraws an approval.</summary>
    /// <param name="approvalId">The approval id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string approvalId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(approvalId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Asks whether this buyer may order without an approval.</summary>
    /// <param name="request">The cart or quote in question.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The question a B2B checkout asks before offering «place order» or
    /// «request approval». It changes nothing, so it is declared repeatable
    /// despite being a <c>POST</c>.
    /// </remarks>
    public async Task<ApprovalPermittedResponse?> IsPermittedAsync(
        ApprovalPermittedRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/approval/{_tenant}/approval/permitted",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    ApprovalJsonContext.Default.ApprovalPermittedRequest),
                Idempotent = true,
            },
            ApprovalJsonContext.Default.ApprovalPermittedResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Finds who could approve something.</summary>
    /// <param name="request">The cart or quote in question.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// For showing a buyer who they are waiting on. Also a read behind a
    /// <c>POST</c>, so it is repeatable.
    /// </remarks>
    public async Task<IReadOnlyList<User>> FindApproversAsync(
        SearchUsersRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/approval/{_tenant}/search/users",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    ApprovalJsonContext.Default.SearchUsersRequest),
                Idempotent = true,
            },
            ApprovalJsonContext.Default.ListUser,
            cancellationToken).ConfigureAwait(false) ?? [];
    }
}
