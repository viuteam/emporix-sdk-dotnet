using System.Net;

namespace Viu.Emporix.Tests;

/// <summary>
/// Answers requests from a supplied function and records what arrived.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;
    private int _callCount;

    public StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        => _respond = respond;

    /// <summary>Answers every request the same way.</summary>
    public StubHttpMessageHandler(HttpStatusCode status, string body)
        : this((_, _) => Json(status, body))
    {
    }

    /// <summary>How many requests were made.</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>The addresses of all requests, in arrival order.</summary>
    public IReadOnlyList<Uri> RequestUris => _requestUris;

    /// <summary>The methods of all requests, in arrival order.</summary>
    public IReadOnlyList<HttpMethod> RequestMethods => _requestMethods;

    /// <summary>The bodies of all requests, in arrival order.</summary>
    public IReadOnlyList<string> RequestBodies => _requestBodies;

    /// <summary>The most recent request.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>The Authorization header of the most recent request.</summary>
    public string? LastAuthorizationHeader
    {
        get
        {
            lock (_recordLock)
            {
                return _headers.Count == 0 ? null : LookUp(_headers[^1], "Authorization");
            }
        }
    }

    /// <summary>A header of the most recent request.</summary>
    public string? LastHeader(string name)
    {
        lock (_recordLock)
        {
            return _headers.Count == 0 ? null : LookUp(_headers[^1], name);
        }
    }

    /// <summary>A header of the request at the given index.</summary>
    public string? HeaderAt(int index, string name)
    {
        lock (_recordLock)
        {
            return LookUp(_headers[index], name);
        }
    }

    private static string? LookUp(Dictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out string? value) ? value : null;

    /// <summary>Delays every response to make concurrency observable.</summary>
    public TimeSpan Delay { get; set; }

    private readonly List<Uri> _requestUris = [];
    private readonly List<HttpMethod> _requestMethods = [];
    private readonly List<string> _requestBodies = [];
    private readonly List<Dictionary<string, string>> _headers = [];
    private readonly Lock _recordLock = new();

    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        int call = Interlocked.Increment(ref _callCount);

        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, IEnumerable<string> values) in request.Headers)
        {
            headers[name] = string.Join(", ", values);
        }

        lock (_recordLock)
        {
            _requestUris.Add(request.RequestUri!);
            _requestMethods.Add(request.Method);
            _requestBodies.Add(body);
            _headers.Add(headers);
            LastRequest = request;
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }

        return _respond(request, call);
    }
}

/// <summary>
/// A controllable clock. Enough for the SDK's expiry arithmetic and avoids a
/// dependency on a test-time package.
/// </summary>
internal sealed class StubClock : TimeProvider
{
    // Deliberately only a clock: it does not override CreateTimer, so anything
    // that waits would wait for real. Tests of waiting use Microsoft's
    // FakeTimeProvider, which drives timers on virtual time.
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan amount) => Now += amount;
}

/// <summary>
/// A controllable <see cref="ITokenProvider"/>: hands out predetermined tokens
/// and records what was invalidated from outside.
/// </summary>
internal sealed class FakeTokenProvider : ITokenProvider
{
    /// <summary>The tokens handed out in turn.</summary>
    public Queue<string> ServiceTokens { get; } = new(["service-1", "service-2", "service-3"]);

    /// <summary>The anonymous access tokens handed out in turn.</summary>
    public Queue<string> AnonymousTokens { get; } = new(["anon-1", "anon-2", "anon-3"]);

    public int InvalidateServiceCalls { get; private set; }

    public int ExpireAnonymousCalls { get; private set; }

    public int InvalidateAnonymousCalls { get; private set; }

    public ValueTask<string> GetServiceTokenAsync(string credentialSet, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ServiceTokens.Count > 0 ? ServiceTokens.Dequeue() : "service-exhausted");

    public ValueTask<AnonymousSession> GetAnonymousSessionAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AnonymousSession(
            AnonymousTokens.Count > 0 ? AnonymousTokens.Dequeue() : "anon-exhausted",
            "refresh",
            "session-1",
            DateTimeOffset.MaxValue));

    public void InvalidateServiceToken(string credentialSet) => InvalidateServiceCalls++;

    public void ExpireAnonymousAccessToken() => ExpireAnonymousCalls++;

    public void InvalidateAnonymousSession() => InvalidateAnonymousCalls++;
}

/// <summary>A token refresher with a predetermined answer.</summary>
internal sealed class FakeCustomerTokenRefresher : ICustomerTokenRefresher
{
    private readonly string? _result;

    public FakeCustomerTokenRefresher(string? result) => _result = result;

    public int Calls { get; private set; }

    public List<string> SeenExpiredTokens { get; } = [];

    public ValueTask<string?> RefreshAsync(string expiredToken, CancellationToken cancellationToken = default)
    {
        Calls++;
        lock (SeenExpiredTokens)
        {
            SeenExpiredTokens.Add(expiredToken);
        }

        return ValueTask.FromResult(_result);
    }
}
