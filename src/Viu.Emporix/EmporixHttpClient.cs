using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Executes calls against the Emporix API: builds the address, sends, interprets
/// the response and translates failures into the SDK's exceptions.
/// </summary>
/// <remarks>
/// Tokens, retries and the reaction to a 401 belong to the handler chain. This
/// type is only concerned with what the response means.
/// </remarks>
internal sealed class EmporixHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly EmporixOptions _options;

    public EmporixHttpClient(HttpClient httpClient, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options.Value;
    }

    /// <summary>Executes a call and returns the parsed response body.</summary>
    /// <exception cref="EmporixApiException">Emporix responded with an error status.</exception>
    /// <exception cref="EmporixTimeoutException">The time limit was exceeded.</exception>
    /// <exception cref="EmporixNetworkException">The connection failed.</exception>
    public async Task<T?> SendAsync<T>(
        EmporixRequest request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(typeInfo);

        (string body, _, string correlationId) =
            await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        return Deserialize(body, typeInfo, request, correlationId);
    }

    /// <summary>
    /// Executes a call and returns the response body as text.
    /// </summary>
    /// <remarks>
    /// For responses whose shape is known only at read time — a login response,
    /// say, where Emporix uses two different field spellings. Status handling
    /// and failure translation are the same as everywhere; only the parsing is
    /// left to the caller.
    /// </remarks>
    public async Task<string> SendForBodyAsync(
        EmporixRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        (string body, _, _) = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        return body;
    }

    /// <summary>Executes a call whose response body is not needed.</summary>
    /// <exception cref="EmporixApiException">Emporix responded with an error status.</exception>
    public async Task SendAsync(EmporixRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a call that returns one page of a list and interprets the
    /// pagination headers.
    /// </summary>
    /// <param name="request">The call; the page parameters must already be in the query.</param>
    /// <param name="typeInfo">Type information for the list.</param>
    /// <param name="pageNumber">The requested page number.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<T>> SendPageAsync<T>(
        EmporixRequest request,
        JsonTypeInfo<List<T>> typeInfo,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(typeInfo);

        (string body, HttpResponseHeaders headers, string correlationId) =
            await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        // For a list an empty body is an empty page, not a failure.
        List<T> items = Deserialize(body, typeInfo, request, correlationId) ?? [];

        int? totalCount = ReadNonNegativeInt(headers, "X-Total-Count");
        string? nextCursor = ReadHeader(headers, "X-Next-Cursor");
        string? previousCursor = ReadHeader(headers, "X-Prev-Cursor");

        return new PaginatedItems<T>(
            items,
            pageNumber,
            pageSize,
            DetermineHasNextPage(items.Count, pageNumber, pageSize, totalCount, nextCursor),
            totalCount,
            nextCursor,
            previousCursor);
    }

    /// <summary>
    /// Executes a call and returns the response uninterpreted.
    /// </summary>
    /// <remarks>
    /// For responses that are not JSON — file downloads, say, or responses where
    /// only the redirect location matters. The caller owns the response and must
    /// dispose it. Error statuses are <em>not</em> translated into exceptions
    /// here: the caller decides what counts as a failure.
    /// </remarks>
    public async Task<HttpResponseMessage> SendRawAsync(
        EmporixRequest request,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpRequestMessage message = BuildRequest(request, out string correlationId);

        try
        {
            return await _httpClient.SendAsync(message, completionOption, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            throw ToTransportException(exception, request, correlationId);
        }
    }

    /// <summary>
    /// Sends the request, checks the status and returns body and headers.
    /// </summary>
    private async Task<(string Body, HttpResponseHeaders Headers, string CorrelationId)> ExecuteAsync(
        EmporixRequest request,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = BuildRequest(request, out string correlationId);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            throw ToTransportException(exception, request, correlationId);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return (body, response.Headers, correlationId);
            }

            EmporixApiException failure = EmporixErrorParser.CreateException(
                response.StatusCode,
                Describe(request),
                body,
                response.Headers.RetryAfter?.Delta);

            // The correlation id is the thread that makes a call findable across
            // system boundaries — it belongs on every failure.
            failure.CorrelationId = correlationId;

            throw failure;
        }
    }

    private HttpRequestMessage BuildRequest(EmporixRequest request, out string correlationId)
    {
        HttpRequestMessage message = new(request.Method, BuildUri(request))
        {
            Content = request.Content,
        };

        message.Options.Set(EmporixRequestOptions.Auth, request.Auth);

        if (request.Idempotent)
        {
            message.Options.Set(EmporixRequestOptions.Idempotent, true);
        }

        if (request.Headers is not null)
        {
            foreach ((string name, string value) in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Join an ongoing trace when there is one, otherwise mint an id. That
        // way a call stays attributable even where no tracing is set up.
        correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        message.Headers.TryAddWithoutValidation(
            EmporixRequestOptions.CorrelationIdHeader,
            correlationId);

        return message;
    }

    private Uri BuildUri(EmporixRequest request)
    {
        StringBuilder builder = new(request.Path);

        if (request.Query is { Count: > 0 })
        {
            bool first = true;
            foreach ((string name, string? value) in request.Query)
            {
                // Omit parameters without a value: an unset optional filter must
                // not reach the server as an empty value.
                if (value is null)
                {
                    continue;
                }

                builder.Append(first ? '?' : '&');
                builder.Append(Uri.EscapeDataString(name));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(value));
                first = false;
            }
        }

        return new Uri(new Uri(_options.Host), builder.ToString());
    }

    private static T? Deserialize<T>(
        string body,
        JsonTypeInfo<T> typeInfo,
        EmporixRequest request,
        string correlationId)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            // Plenty of Emporix endpoints answer successfully with no body.
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize(body, typeInfo);
        }
        catch (JsonException exception)
        {
            // A success status with an unreadable body is a genuine failure —
            // unlike an error response, where an unreadable body merely means
            // the error details are missing.
            EmporixApiException failure = new(
                $"{Describe(request)}: the response could not be parsed. {exception.Message}",
                System.Net.HttpStatusCode.OK,
                rawBody: body);

            // A parse failure is the hardest kind to chase down, so it needs the
            // correlation id most: without it there is nothing to match against
            // what Emporix logged.
            failure.CorrelationId = correlationId;

            throw failure;
        }
    }

    /// <summary>
    /// Derives from the response whether another page exists, in three steps,
    /// most precise first.
    /// </summary>
    private static bool DetermineHasNextPage(
        int itemCount,
        int pageNumber,
        int pageSize,
        int? totalCount,
        string? nextCursor)
    {
        // 1. A cursor says so outright. Its absence says nothing: hardly any
        //    endpoint works with cursors at all.
        if (nextCursor is { Length: > 0 })
        {
            return true;
        }

        // 2. The total count, when the call asked for it.
        if (totalCount is { } total)
        {
            return (long)pageNumber * pageSize < total;
        }

        // 3. The guess: a full page suggests another one. When the total is
        //    exactly a multiple of the page size, this costs one extra empty
        //    request.
        return itemCount >= pageSize && pageSize > 0;
    }

    private static string? ReadHeader(HttpResponseHeaders headers, string name)
        => headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    /// <summary>
    /// Reads a header as a non-negative integer, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// An unusable value must not enter the page arithmetic: treated as «not
    /// stated» the next step applies, whereas read as a number it would quietly
    /// declare every page the last one.
    /// </remarks>
    private static int? ReadNonNegativeInt(HttpResponseHeaders headers, string name)
    {
        string? raw = ReadHeader(headers, name);

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value >= 0
                ? value
                : null;
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException
            || (exception is TaskCanceledException or OperationCanceledException
                && !cancellationToken.IsCancellationRequested);

    private EmporixTransportException ToTransportException(
        Exception exception,
        EmporixRequest request,
        string correlationId)
    {
        // A cancellation the caller did not ask for is the time limit.
        EmporixTransportException failure = exception is TaskCanceledException or OperationCanceledException
            ? new EmporixTimeoutException(
                $"{Describe(request)} exceeded its time limit of {_options.Timeouts.Read}.",
                _options.Timeouts.Read,
                exception)
            : new EmporixNetworkException(
                $"{Describe(request)} failed: {exception.Message}",
                exception);

        failure.CorrelationId = correlationId;
        return failure;
    }

    private static string Describe(EmporixRequest request) => $"{request.Method.Method} {request.Path}";
}
