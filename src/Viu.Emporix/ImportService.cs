using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.ImportServiceModels;

namespace Viu.Emporix;

/// <summary>
/// Imports — bringing data in from somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// A configuration describes a source and the streams inside it; a run is one
/// execution of that configuration. Most of the rest is about watching a run:
/// its progress, its errors, the records it wrote.
/// </para>
/// <para>
/// A run finishes in its own time. Poll <see cref="GetRunAsync"/> — with
/// <see cref="EmporixPolling.WaitForAsync"/> if a wait is what you want — or
/// follow <see cref="StreamEventsAsync"/> to watch it live.
/// </para>
/// <para>
/// Emporix marks this service as preview: parts of it may not be fully
/// operational yet.
/// </para>
/// </remarks>
public sealed class ImportService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal ImportService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/importtool/{_tenant}";

    /// <summary>Lists the import configurations.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ImportConfig>> ListConfigsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/configs",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.ListImportConfig,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one import configuration.</summary>
    /// <param name="id">The configuration id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ImportConfig?> GetConfigAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/configs/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.ImportConfig,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists a configuration's streams.</summary>
    /// <param name="configId">The configuration id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>A stream is one kind of record inside a source — products, prices, stock.</remarks>
    public async Task<IReadOnlyList<ImportStream>> ListStreamsAsync(
        string configId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/configs/{Uri.EscapeDataString(configId)}/streams",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.ListImportStream,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Fetches one stream.</summary>
    /// <param name="id">The stream id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ImportStream?> GetStreamAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/streams/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.ImportStream,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a configuration's schedule.</summary>
    /// <param name="configId">The configuration id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The schedule, or <see langword="null"/> when the configuration has none.</returns>
    public async Task<Schedule?> GetScheduleAsync(
        string configId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/configs/{Uri.EscapeDataString(configId)}/schedule",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.Schedule,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets a configuration's schedule.</summary>
    /// <param name="configId">The configuration id.</param>
    /// <param name="schedule">When the import should run by itself.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Schedule?> SetScheduleAsync(
        string configId,
        Schedule schedule,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);
        ArgumentNullException.ThrowIfNull(schedule);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/configs/{Uri.EscapeDataString(configId)}/schedule",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(schedule, ImportJsonContext.Default.Schedule),
                Idempotent = true,
            },
            ImportJsonContext.Default.Schedule,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts an import run.</summary>
    /// <param name="configId">The configuration id.</param>
    /// <param name="mode">A full import or only what changed. Emporix defaults to <c>DELTA</c>.</param>
    /// <param name="dryRun">Map and validate, but write nothing.</param>
    /// <param name="force">Rewrite every record, even unchanged ones.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The run, already running — this call does not wait for it.</returns>
    /// <remarks>
    /// <para>
    /// At most one run is active per configuration; starting a second answers
    /// <c>409</c>.
    /// </para>
    /// <para>
    /// Deliberately not repeatable. A retried start imports the same source
    /// twice, and the SDK cannot know whether the target tolerates that.
    /// </para>
    /// </remarks>
    public async Task<ImportRun?> StartRunAsync(
        string configId,
        BodyMode? mode = null,
        bool? dryRun = null,
        bool? force = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);

        Body body = new() { Mode = mode, DryRun = dryRun, Force = force };

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/configs/{Uri.EscapeDataString(configId)}/runs",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(body, ImportJsonContext.Default.Body),
            },
            ImportJsonContext.Default.ImportRun,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists a configuration's runs.</summary>
    /// <param name="configId">The configuration id.</param>
    /// <param name="page">The page, counting from zero.</param>
    /// <param name="size">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ImportRunPage?> ListRunsAsync(
        string configId,
        int page = 0,
        int size = 20,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/configs/{Uri.EscapeDataString(configId)}/runs",
                Auth = Defaults.Service(auth),
                Query = Paging(page, size),
                Idempotent = true,
            },
            ImportJsonContext.Default.ImportRunPage,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads how a run is going.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The call to poll:
    /// <code>
    /// RunDetail? run = await EmporixPolling.WaitForAsync(
    ///     poll: ct => client.Imports.GetRunAsync(runId, cancellationToken: ct),
    ///     isComplete: r => r?.Status is not (ImportRunStatus.RUNNING or ImportRunStatus.PENDING));
    /// </code>
    /// <see cref="StreamEventsAsync"/> follows the same run without polling.
    /// </remarks>
    public async Task<RunDetail?> GetRunAsync(
        string runId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/runs/{Uri.EscapeDataString(runId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.RunDetail,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Follows a run's progress as it happens.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, unread. The caller owns it and must dispose it.</returns>
    /// <remarks>
    /// <para>
    /// Server-sent events. The response comes back with its body unread, and the
    /// caller parses it with the parser <c>net10.0</c> already ships:
    /// </para>
    /// <code>
    /// using HttpResponseMessage response = await client.Imports.StreamEventsAsync(runId);
    /// await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    ///
    /// await foreach (SseItem&lt;string&gt; item in SseParser.Create(stream).EnumerateAsync(cancellationToken))
    /// {
    ///     Console.WriteLine(item.Data);
    /// }
    /// </code>
    /// <para>
    /// Why no <c>IAsyncEnumerable</c> here: ADR-0007. Whether a dropped stream is
    /// an error or an ending is the caller's decision, and it differs between a
    /// dashboard and a batch job.
    /// </para>
    /// <para>
    /// Note that <see cref="EmporixHttpClient.SendRawAsync"/> does not translate
    /// error statuses into exceptions — check
    /// <see cref="HttpResponseMessage.IsSuccessStatusCode"/> before reading.
    /// </para>
    /// </remarks>
    public Task<HttpResponseMessage> StreamEventsAsync(
        string runId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return _http.SendRawAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/runs/{Uri.EscapeDataString(runId)}/events",
                Auth = Defaults.Service(auth),
                Headers = [new("Accept", "text/event-stream")],
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>Stops a running import.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Whatever the run already wrote stays written: this stops the work, it does
    /// not undo it.
    /// </remarks>
    public async Task<CancelResult?> CancelRunAsync(
        string runId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/runs/{Uri.EscapeDataString(runId)}/cancel",
                Auth = Defaults.Service(auth),
            },
            ImportJsonContext.Default.CancelResult,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a failed run again.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Not repeatable, for the same reason starting one is not.</remarks>
    public async Task<ImportRun?> RetryRunAsync(
        string runId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/runs/{Uri.EscapeDataString(runId)}/retry",
                Auth = Defaults.Service(auth),
            },
            ImportJsonContext.Default.ImportRun,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the records a run could not import.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="page">The page, counting from zero.</param>
    /// <param name="size">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A run that reports success can still have rejected records. This is where
    /// they are.
    /// </remarks>
    public async Task<ErrorRecordPage?> ListRunErrorsAsync(
        string runId,
        int page = 0,
        int size = 20,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/runs/{Uri.EscapeDataString(runId)}/errors",
                Auth = Defaults.Service(auth),
                Query = Paging(page, size),
                Idempotent = true,
            },
            ImportJsonContext.Default.ErrorRecordPage,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the target types that currently hold imported records.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>These are the values <see cref="ListRecordsAsync"/> accepts as its type.</remarks>
    public async Task<IReadOnlyList<string>> ListDataTypesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/data/types",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.ListString,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Lists imported records of one type.</summary>
    /// <param name="type">The target type; one of <see cref="ListDataTypesAsync"/>. Required.</param>
    /// <param name="search">Free-text filter.</param>
    /// <param name="outcome">Keep only records that ended this way.</param>
    /// <param name="page">The page, counting from zero.</param>
    /// <param name="size">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ImportedRecordPage?> ListRecordsAsync(
        string type,
        string? search = null,
        Outcome? outcome = null,
        int page = 0,
        int size = 20,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        List<KeyValuePair<string, string?>> query = [new("type", type)];
        AddRecordFilters(query, search, outcome, page, size);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/data/records",
                Auth = Defaults.Service(auth),
                Query = query,
                Idempotent = true,
            },
            ImportJsonContext.Default.ImportedRecordPage,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the records one stream imported.</summary>
    /// <param name="streamId">The stream id.</param>
    /// <param name="search">Free-text filter.</param>
    /// <param name="outcome">Keep only records that ended this way.</param>
    /// <param name="page">The page, counting from zero.</param>
    /// <param name="size">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ImportedRecordPage?> ListStreamRecordsAsync(
        string streamId,
        string? search = null,
        Outcome? outcome = null,
        int page = 0,
        int size = 20,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        List<KeyValuePair<string, string?>> query = [];
        AddRecordFilters(query, search, outcome, page, size);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/data/streams/{Uri.EscapeDataString(streamId)}/records",
                Auth = Defaults.Service(auth),
                Query = query,
                Idempotent = true,
            },
            ImportJsonContext.Default.ImportedRecordPage,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads import statistics.</summary>
    /// <param name="configId">Narrow to one configuration.</param>
    /// <param name="configIds">Narrow to several configurations, comma-separated.</param>
    /// <param name="streamId">Narrow to one stream.</param>
    /// <param name="from">The start of the window.</param>
    /// <param name="to">The end of the window.</param>
    /// <param name="granularity">How finely to bucket the series. Emporix defaults to <c>DAY</c>.</param>
    /// <param name="sections">Which sections to compute, comma-separated.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ImportStats?> GetStatisticsAsync(
        string? configId = null,
        string? configIds = null,
        string? streamId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        Granularity? granularity = null,
        string? sections = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        List<KeyValuePair<string, string?>> query = [];

        if (!string.IsNullOrWhiteSpace(configId))
        {
            query.Add(new("configId", configId));
        }

        if (!string.IsNullOrWhiteSpace(configIds))
        {
            query.Add(new("configIds", configIds));
        }

        if (!string.IsNullOrWhiteSpace(streamId))
        {
            query.Add(new("streamId", streamId));
        }

        if (from is not null)
        {
            query.Add(new("from", from.Value.ToString("O", CultureInfo.InvariantCulture)));
        }

        if (to is not null)
        {
            query.Add(new("to", to.Value.ToString("O", CultureInfo.InvariantCulture)));
        }

        if (granularity is not null)
        {
            query.Add(new("granularity", granularity.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(sections))
        {
            query.Add(new("sections", sections));
        }

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/stats",
                Auth = Defaults.Service(auth),
                Query = query,
                Idempotent = true,
            },
            ImportJsonContext.Default.ImportStats,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the dashboard's job groups.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<JobGroup>> ListJobGroupsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/dashboard/job-groups",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.ListJobGroup,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Reads the thresholds above which a run counts as unhealthy.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<HealthSettings?> GetHealthThresholdsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/settings/health-thresholds",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.HealthSettings,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads the import tool's licence.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ImportLicense?> GetLicenseAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/license",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.ImportLicense,
            cancellationToken).ConfigureAwait(false);

    private static void AddRecordFilters(
        List<KeyValuePair<string, string?>> query,
        string? search,
        Outcome? outcome,
        int page,
        int size)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add(new("search", search));
        }

        if (outcome is not null)
        {
            query.Add(new("outcome", outcome.Value.ToString()));
        }

        query.AddRange(Paging(page, size));
    }

    // The import tool counts pages from zero and spells the parameters `page`
    // and `size` — unlike most of the API, which uses `pageNumber`/`pageSize`
    // counting from one.
    private static List<KeyValuePair<string, string?>> Paging(int page, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        return
        [
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("size", size.ToString(CultureInfo.InvariantCulture)),
        ];
    }
}
