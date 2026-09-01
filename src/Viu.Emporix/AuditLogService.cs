using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.AuditLogsChangelogModels;

namespace Viu.Emporix;

/// <summary>
/// The audit log — who changed what, when, and from which value to which.
/// </summary>
/// <remarks>
/// <para>
/// Emporix calls this the changelog service. It covers platform entities across
/// the tenant: orders, customers, companies, products, segments, groups,
/// coupons, and any custom entity defined through <see cref="SchemaService"/>.
/// </para>
/// <para>
/// Service accounts only. It needs <c>changelog.changelog_read</c> on the client
/// credentials, there is no customer or anonymous variant, and the entries name
/// people — so this belongs on a server, never in something a browser downloads.
/// </para>
/// <para>
/// Emporix marks the service as preview: nearly every field of an entry is
/// optional, and the contract may change.
/// </para>
/// </remarks>
public sealed class AuditLogService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AuditLogService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    /// <summary>Lists changes across the tenant, most recent first.</summary>
    /// <param name="query">
    /// A standard <c>q</c> filter over <c>entity</c>, <c>entityId</c>,
    /// <c>type</c>, <c>actor</c>, <c>occurredAt</c> and <c>related.*</c>.
    /// </param>
    /// <param name="page">The page, counting from 1.</param>
    /// <param name="size">The page size, at most 100.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The page, with the numbers the server actually used rather than the ones
    /// that were asked for.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>An unfiltered call is not «everything ever».</b> With no conjunctive
    /// <c>occurredAt</c> lower bound in <paramref name="query"/>, Emporix
    /// silently applies a trailing 30-day window. Widen it with an explicit
    /// top-level or <c>AND</c>-ed range — one that appears only inside an
    /// <c>OR</c> arm does not lift the default.
    /// </para>
    /// <para>
    /// <b><c>entityId</c> needs <c>entity</c>.</b> Filtering by id alone answers
    /// <c>400</c>, not an empty page. There is no path-based history endpoint
    /// either, so scoping to one document means naming both:
    /// </para>
    /// <code>
    /// // The history of one order.
    /// var page = await client.AuditLogs.ListAsync($"entity:order entityId:{orderId}");
    ///
    /// // Everything one person changed in June, across every entity.
    /// var june = await client.AuditLogs.ListAsync(
    ///     "actor:\"Jane Doe\" occurredAt:(&gt;\"2026-06-01T00:00:00.000Z\" AND &lt;\"2026-07-01T00:00:00.000Z\")",
    ///     size: 100);
    /// </code>
    /// </remarks>
    public async Task<ChangelogHistoryResponse?> ListAsync(
        string? query = null,
        int page = 1,
        int size = 20,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 100);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("size", size.ToString(CultureInfo.InvariantCulture)),
        ];

        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters.Add(new("q", query));
        }

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/changelog/{_tenant}/changelogs",
                Auth = Defaults.Service(auth),
                Query = parameters,
                Idempotent = true,
            },
            AuditLogJsonContext.Default.ChangelogHistoryResponse,
            cancellationToken).ConfigureAwait(false);
    }
}
