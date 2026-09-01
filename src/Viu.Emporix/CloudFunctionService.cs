using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Cloud functions — code a tenant deployed, invoked by name.
/// </summary>
/// <remarks>
/// <para>
/// The one service in the API with no specification: the request and response
/// shapes belong to whoever wrote the function, so Emporix vendors no schema and
/// this package ships no generated types for it.
/// </para>
/// <para>
/// Because the SDK serialises without reflection, the caller supplies the type
/// information — see <see href="../../docs/adr/0009-cloud-functions.md">ADR-0009</see>:
/// </para>
/// <code>
/// MyResponse? result = await client.CloudFunctions.InvokeAsync(
///     "price-check",
///     request,
///     MyJsonContext.Default.MyRequest,
///     MyJsonContext.Default.MyResponse);
/// </code>
/// <para>
/// What the SDK still contributes: the tenant in the address, a token on the
/// request, retry, error translation and a correlation id. A caller who drops to
/// <see cref="HttpClient"/> gives all of that up.
/// </para>
/// <para>
/// Anonymous by default, matching the Node SDK — a cloud function is often a
/// public endpoint. Pass a context for anything else.
/// </para>
/// </remarks>
public sealed class CloudFunctionService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CloudFunctionService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/cloud-functions/{_tenant}/functions";

    /// <summary>Invokes a function with types the caller supplies.</summary>
    /// <typeparam name="TRequest">The request shape.</typeparam>
    /// <typeparam name="TResponse">The response shape.</typeparam>
    /// <param name="functionId">Which function.</param>
    /// <param name="request">The body. Nothing is sent when this is <see langword="null"/>.</param>
    /// <param name="requestTypeInfo">How to serialise <paramref name="request"/>.</param>
    /// <param name="responseTypeInfo">How to read the answer.</param>
    /// <param name="path">A sub-path the function exposes. The leading slash is optional.</param>
    /// <param name="method">The verb. <c>POST</c> when omitted.</param>
    /// <param name="query">Query-string parameters.</param>
    /// <param name="headers">Extra request headers.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Never marked repeatable. A cloud function is arbitrary code and the SDK
    /// cannot know whether running it twice is safe — this is the one place
    /// where guessing would be a guess about someone else's side effects.
    /// </remarks>
    public async Task<TResponse?> InvokeAsync<TRequest, TResponse>(
        string functionId,
        TRequest? request,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        string? path = null,
        HttpMethod? method = null,
        IEnumerable<KeyValuePair<string, string?>>? query = null,
        IEnumerable<KeyValuePair<string, string>>? headers = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionId);
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);

        return await _http.SendAsync(
            Build(
                functionId,
                path,
                method,
                query,
                headers,
                auth,
                request is null ? null : EmporixJsonContent.Create(request, requestTypeInfo)),
            responseTypeInfo,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Invokes a function whose shape is only known at run time.</summary>
    /// <param name="functionId">Which function.</param>
    /// <param name="request">The body as JSON. Nothing is sent when this is <see langword="null"/>.</param>
    /// <param name="path">A sub-path the function exposes.</param>
    /// <param name="method">The verb. <c>POST</c> when omitted.</param>
    /// <param name="query">Query-string parameters.</param>
    /// <param name="headers">Extra request headers.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// For a function whose contract is decided by configuration rather than at
    /// compile time. Not repeatable, like the typed form.
    /// </remarks>
    public async Task<JsonElement> InvokeJsonAsync(
        string functionId,
        JsonElement? request = null,
        string? path = null,
        HttpMethod? method = null,
        IEnumerable<KeyValuePair<string, string?>>? query = null,
        IEnumerable<KeyValuePair<string, string>>? headers = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionId);

        return await _http.SendAsync(
            Build(
                functionId,
                path,
                method,
                query,
                headers,
                auth,
                request is null
                    ? null
                    : EmporixJsonContent.Create(
                        request.Value, CloudFunctionJsonContext.Default.JsonElement)),
            CloudFunctionJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Invokes a function and hands back the response untouched.</summary>
    /// <param name="functionId">Which function.</param>
    /// <param name="content">The body, already built.</param>
    /// <param name="path">A sub-path the function exposes.</param>
    /// <param name="method">The verb. <c>POST</c> when omitted.</param>
    /// <param name="query">Query-string parameters.</param>
    /// <param name="headers">Extra request headers.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, unread. The caller owns it and must dispose it.</returns>
    /// <remarks>
    /// For a function that answers with something other than JSON — a file, a
    /// redirect, a bare string. Error statuses are <em>not</em> turned into
    /// exceptions here; check
    /// <see cref="HttpResponseMessage.IsSuccessStatusCode"/> yourself.
    /// </remarks>
    public Task<HttpResponseMessage> InvokeRawAsync(
        string functionId,
        HttpContent? content = null,
        string? path = null,
        HttpMethod? method = null,
        IEnumerable<KeyValuePair<string, string?>>? query = null,
        IEnumerable<KeyValuePair<string, string>>? headers = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionId);

        return _http.SendRawAsync(
            Build(functionId, path, method, query, headers, auth, content),
            cancellationToken: cancellationToken);
    }

    private EmporixRequest Build(
        string functionId,
        string? path,
        HttpMethod? method,
        IEnumerable<KeyValuePair<string, string?>>? query,
        IEnumerable<KeyValuePair<string, string>>? headers,
        AuthContext auth,
        HttpContent? content)
    {
        string suffix = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : "/" + string.Join(
                '/',
                path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));

        return new EmporixRequest
        {
            Method = method ?? HttpMethod.Post,
            Path = $"{BasePath}/{Uri.EscapeDataString(functionId)}{suffix}",
            Auth = Defaults.Anonymous(auth),
            Query = query is null ? null : [.. query],
            Headers = headers is null ? null : [.. headers],
            Content = content,
        };
    }
}
