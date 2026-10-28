using System.Globalization;
using System.Net.Http.Headers;
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

    /// <summary>Reads the order in which a configuration's streams run.</summary>
    /// <param name="configId">The configuration id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Stream names in run order, and each stream's prerequisites. Read it rather
    /// than working it out from the stream definitions: mapping transformations
    /// add dependencies no single stream shows, so a computed order can differ
    /// from the one a run uses.
    /// </remarks>
    public async Task<StreamOrder?> GetStreamOrderAsync(
        string configId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/configs/{Uri.EscapeDataString(configId)}/stream-order",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.StreamOrder,
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

    /// <summary>Removes a configuration's schedule.</summary>
    /// <param name="configId">The configuration id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The configuration stays; only the automatic run goes. Deleting a schedule
    /// that is not there answers <c>204</c> as well, which is why this is
    /// repeatable.
    /// </remarks>
    public Task DeleteScheduleAsync(
        string configId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/configs/{Uri.EscapeDataString(configId)}/schedule",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Starts an import run.</summary>
    /// <param name="configId">The configuration id.</param>
    /// <param name="mode">A full import or only what changed. Emporix defaults to <c>DELTA</c>.</param>
    /// <param name="dryRun">Map and validate, but write nothing.</param>
    /// <param name="force">Rewrite every record, even unchanged ones.</param>
    /// <param name="streamIds">
    /// Runs only these streams, still in the computed order — stream ids, not
    /// names. Every stream runs when omitted.
    /// </param>
    /// <param name="mappings">
    /// Which mappings a dry run uses: the published ones, which is what Emporix
    /// assumes, or the drafts. A real run ignores it and always uses the
    /// published mappings.
    /// </param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The run, <c>QUEUED</c> — this call does not wait for it to start, let alone
    /// finish. <c>MappingVersions</c> is not set yet: the versions are fixed when
    /// the run begins executing, so read them from <see cref="GetRunAsync"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A configuration has at most one run executing and one queued: while it has
    /// a run <c>QUEUED</c>, <c>RUNNING</c> or <c>RESUMABLE</c>, starting another
    /// answers <c>409</c>. The tenant's <c>maxConcurrentImports</c> no longer
    /// refuses a run; the run waits, with <c>QueueWaitingFor</c> set to
    /// <c>TENANT_LIMIT</c>.
    /// </para>
    /// <para>
    /// A run executes published mappings only. A stream whose mappings were
    /// never published does not run: it reports <c>ABORTED</c> and the run
    /// finishes <c>PARTIAL</c>. When <paramref name="streamIds"/> names such a
    /// stream, or every enabled stream is in that state, the whole run is
    /// refused — and this call still succeeds, because the refusal arrives later
    /// as a run that finishes <c>ABORTED</c> with a message naming the streams.
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
        IEnumerable<Guid>? streamIds = null,
        BodyMappings? mappings = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);

        List<Guid>? streams = streamIds is null ? null : [.. streamIds];

        // Emporix rejects an empty list rather than reading it as «all streams».
        if (streams is not null)
        {
            ArgumentOutOfRangeException.ThrowIfZero(streams.Count, nameof(streamIds));
        }

        // Mappings is set even when null: the generated default is «published»,
        // and a body should carry only what the caller chose.
        Body body = new()
        {
            Mode = mode,
            DryRun = dryRun,
            Force = force,
            StreamIds = streams,
            Mappings = mappings,
        };

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
    ///     isComplete: r => r?.Run?.Status is not
    ///         (ImportRunStatus.QUEUED or ImportRunStatus.RUNNING or ImportRunStatus.RESUMABLE));
    /// </code>
    /// <c>RESUMABLE</c> is not final: an interrupted run picks itself up again.
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

    /// <summary>Stops a queued or running import.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="force">
    /// Stop the run at once and unblock its configuration, rather than letting it
    /// wind down. Emporix assumes <see langword="false"/>.
    /// </param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// A <c>QUEUED</c> run has not started and is cancelled at once, with or
    /// without <paramref name="force"/>. A <c>RESUMABLE</c> run — interrupted,
    /// and waiting to resume on its own — stops only with
    /// <paramref name="force"/>; without it Emporix answers <c>409</c>.
    /// </para>
    /// <para>
    /// Whatever the run already wrote stays written: this stops the work, it does
    /// not undo it.
    /// </para>
    /// </remarks>
    public async Task<CancelResult?> CancelRunAsync(
        string runId,
        bool? force = null,
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
                Query = force is null ? null : [new("force", force.Value ? "true" : "false")],
            },
            ImportJsonContext.Default.CancelResult,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a failed run again.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new run, <c>QUEUED</c> — this call does not wait for it.</returns>
    /// <remarks>
    /// Refused with <c>409</c> like <see cref="StartRunAsync"/>, while the
    /// configuration has a run <c>QUEUED</c>, <c>RUNNING</c> or <c>RESUMABLE</c>.
    /// Not repeatable, for the same reason starting one is not.
    /// </remarks>
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

    /// <summary>Lists the rows behind a run's repeated-key and unresolved-parent counters.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="streamId">Only the rows recorded for this stream.</param>
    /// <param name="kind">Only the rows of this kind.</param>
    /// <param name="limit">At most this many rows. Emporix returns up to 500 when omitted.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// Neither counter is a failure, which is why these rows are kept apart from
    /// <see cref="ListRunErrorsAsync"/>.
    /// </para>
    /// <para>
    /// A capped sample, not the whole set: <see cref="DiagnosticPage.Recorded"/>
    /// says how many rows were stored and <see cref="DiagnosticPage.SampleTruncated"/>
    /// whether a stream reached the cap. The run's own counters give the total;
    /// the rows returned here do not measure the size of the problem.
    /// </para>
    /// </remarks>
    public async Task<DiagnosticPage?> ListRunDiagnosticsAsync(
        string runId,
        Guid? streamId = null,
        DiagnosticRecordKind? kind = null,
        int? limit = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/runs/{Uri.EscapeDataString(runId)}/diagnostics",
                Auth = Defaults.Service(auth),
                Query = DiagnosticsQuery(streamId, kind, limit),
                Idempotent = true,
            },
            ImportJsonContext.Default.DiagnosticPage,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Downloads the same diagnostic rows as a CSV file.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="streamId">Only the rows recorded for this stream.</param>
    /// <param name="kind">Only the rows of this kind.</param>
    /// <param name="limit">At most this many rows. Emporix includes up to 50,000 when omitted.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, unread. The caller owns it and must dispose it.</returns>
    /// <remarks>
    /// <para>
    /// For a spreadsheet or a ticket. Comment lines above the header say whether
    /// the limit or the recording cap cut the rows short, so the file keeps that
    /// context when it is passed on, and <c>Content-Disposition</c> suggests a
    /// file name. Emporix quotes every value and prefixes one that begins with
    /// <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> with an apostrophe, so that a
    /// spreadsheet does not run source data as a formula.
    /// </para>
    /// <para>
    /// <see cref="EmporixHttpClient.SendRawAsync"/> does not translate error
    /// statuses into exceptions — check
    /// <see cref="HttpResponseMessage.IsSuccessStatusCode"/> before reading.
    /// </para>
    /// </remarks>
    public Task<HttpResponseMessage> DownloadRunDiagnosticsCsvAsync(
        string runId,
        Guid? streamId = null,
        DiagnosticRecordKind? kind = null,
        int? limit = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return _http.SendRawAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/runs/{Uri.EscapeDataString(runId)}/diagnostics/csv",
                Auth = Defaults.Service(auth),
                Query = DiagnosticsQuery(streamId, kind, limit),
                Headers = [new("Accept", "text/csv")],
            },
            cancellationToken: cancellationToken);
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

    /// <summary>Reads a file-based stream's queue: what it still has to import, and what it already did.</summary>
    /// <param name="streamId">The stream id.</param>
    /// <param name="historyLimit">
    /// How many of the most recently handled files to return. Emporix returns 100
    /// when omitted and treats a value outside 1 to 500 as the nearest bound.
    /// </param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <see cref="FileQueue.Pending"/> is in load order and complete up to 2,000
    /// files; beyond that <see cref="FileQueue.PendingTruncated"/> is set,
    /// <see cref="FileQueue.PendingTotal"/> has the real count, and the order
    /// cannot be changed. <see cref="FileQueue.History"/> is newest first.
    /// </remarks>
    public async Task<FileQueue?> ListStreamFilesAsync(
        string streamId,
        int? historyLimit = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/streams/{Uri.EscapeDataString(streamId)}/files",
                Auth = Defaults.Service(auth),
                Query = historyLimit is null
                    ? null
                    : [new("historyLimit", historyLimit.Value.ToString(CultureInfo.InvariantCulture))],
                Idempotent = true,
            },
            ImportJsonContext.Default.FileQueue,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Queues a delimited file — CSV or one of its variants — for a file-based stream's next run.</summary>
    /// <param name="streamId">The stream id.</param>
    /// <param name="content">The file's bytes.</param>
    /// <param name="fileName">
    /// The file's name. Emporix displays it and infers the compression from it.
    /// </param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The queued file, <c>PENDING</c>. Nothing is imported until a run reads it.</returns>
    /// <remarks>
    /// <para>
    /// Emporix recognises a file by the SHA-256 of its content, whatever its
    /// name, and answers <c>409</c> when the stream already imported or queued
    /// those bytes; a file that previously failed is accepted again. It also
    /// answers <c>409</c> when the stream does not read files, or when a run in
    /// flight uses bytes this upload would replace. <c>413</c> means the file is
    /// over the limit <see cref="GetStreamFileLimitsAsync"/> reports, <c>503</c>
    /// that uploading is not configured in this environment.
    /// </para>
    /// <para>
    /// Not repeatable: an upload that timed out may have been queued, and sending
    /// it again would then answer <c>409</c>. Read
    /// <see cref="ListStreamFilesAsync"/> instead.
    /// </para>
    /// </remarks>
    public async Task<ImportFile?> UploadStreamFileAsync(
        string streamId,
        Stream content,
        string fileName,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using MultipartFormDataContent form = [];
        StreamContent file = new(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/streams/{Uri.EscapeDataString(streamId)}/files",
                Auth = Defaults.Service(auth),
                Content = form,
            },
            ImportJsonContext.Default.ImportFile,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the largest file this environment accepts, and whether uploading is available at all.</summary>
    /// <param name="streamId">The stream id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// When <see cref="FileUploadLimits.GatewayLimited"/> is set, a larger file is
    /// refused by the API gateway with its own <c>413</c> body before it reaches
    /// the service. Publish such a file at an HTTP(S) URL and set it as the
    /// connection's file URL instead.
    /// </remarks>
    public async Task<FileUploadLimits?> GetStreamFileLimitsAsync(
        string streamId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/streams/{Uri.EscapeDataString(streamId)}/files/limits",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            ImportJsonContext.Default.FileUploadLimits,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets the order in which a stream's waiting files are read.</summary>
    /// <param name="streamId">The stream id.</param>
    /// <param name="fileIds">Every waiting file's id, in the order they must be imported.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The queue in its new order.</returns>
    /// <remarks>
    /// For an incremental feed the order is the data: a later file's values win
    /// on a key an earlier one also carries. The list must be exactly the waiting
    /// set, not one file to move — anything else answers <c>400</c>. <c>409</c>
    /// means the job has a run in flight, or the queue is too long to reorder.
    /// </remarks>
    public async Task<IReadOnlyList<ImportFile>> SetStreamFileOrderAsync(
        string streamId,
        IEnumerable<Guid> fileIds,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(fileIds);

        List<Guid> order = [.. fileIds];

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/streams/{Uri.EscapeDataString(streamId)}/files/order",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(order, ImportJsonContext.Default.ListGuid),
                Idempotent = true,
            },
            ImportJsonContext.Default.ListImportFile,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Removes a waiting file from a stream's queue and deletes its stored bytes.</summary>
    /// <param name="streamId">The stream id.</param>
    /// <param name="fileId">The file id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Only a waiting file can go: one a run has claimed or already imported
    /// answers <c>409</c>, and so does any file while the job has a run in flight.
    /// An imported file stays in the history on purpose — removing it would make
    /// the same bytes importable again.
    /// </remarks>
    public Task DeleteStreamFileAsync(
        string streamId,
        string fileId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/streams/{Uri.EscapeDataString(streamId)}/files/{Uri.EscapeDataString(fileId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            cancellationToken);
    }

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

    private static List<KeyValuePair<string, string?>> DiagnosticsQuery(
        Guid? streamId,
        DiagnosticRecordKind? kind,
        int? limit)
    {
        List<KeyValuePair<string, string?>> query = [];

        if (streamId is not null)
        {
            query.Add(new("streamId", streamId.Value.ToString()));
        }

        if (kind is not null)
        {
            query.Add(new("kind", kind.Value.ToString()));
        }

        if (limit is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(limit.Value, 1, nameof(limit));
            query.Add(new("limit", limit.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return query;
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
