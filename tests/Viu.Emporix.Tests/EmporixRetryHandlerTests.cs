using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class EmporixRetryHandlerTests
{
    private readonly List<TimeSpan> _delays = [];

    private HttpClient Build(StubHttpMessageHandler inner, Action<EmporixOptions>? configure = null)
    {
        EmporixOptions options = new() { Tenant = "acme" };
        configure?.Invoke(options);

        EmporixRetryHandler handler = new(
            Options.Create(options),
            NullLogger<EmporixRetryHandler>.Instance,
            (delay, _) =>
            {
                lock (_delays)
                {
                    _delays.Add(delay);
                }

                return Task.CompletedTask;
            })
        {
            InnerHandler = inner,
        };

        return new HttpClient(handler) { BaseAddress = new Uri("https://api.emporix.io") };
    }

    private static HttpRequestMessage Request(
        HttpMethod method,
        bool? idempotent = null,
        HttpContent? content = null)
    {
        HttpRequestMessage request = new(method, "/product/acme/products") { Content = content };
        if (idempotent is { } value)
        {
            request.Options.Set(EmporixRequestOptions.Idempotent, value);
        }

        return request;
    }

    private static StubHttpMessageHandler AlwaysFailing(HttpStatusCode status = HttpStatusCode.ServiceUnavailable)
        => new(status, """{"message":"broken"}""");

    // ---------- What gets retried ----------

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Idempotent_methods_are_retried(string method)
    {
        StubHttpMessageHandler inner = AlwaysFailing();
        using HttpClient client = Build(inner);

        using HttpResponseMessage response = await client.SendAsync(Request(new HttpMethod(method)));

        Assert.Equal(3, inner.CallCount); // MaxAttempts = 3
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public async Task Non_idempotent_methods_are_not_retried(string method)
    {
        // The most important test in this file: a 5xx can arrive after the server
        // already created the order. A second attempt would create it again.
        StubHttpMessageHandler inner = AlwaysFailing();
        using HttpClient client = Build(inner);

        using HttpResponseMessage response = await client.SendAsync(Request(new HttpMethod(method)));

        Assert.Equal(1, inner.CallCount);
        Assert.Empty(_delays);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public async Task Non_idempotent_methods_are_retried_when_the_call_opts_in(string method)
    {
        StubHttpMessageHandler inner = AlwaysFailing();
        using HttpClient client = Build(inner);

        await client.SendAsync(Request(new HttpMethod(method), idempotent: true));

        Assert.Equal(3, inner.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Server_errors_and_rate_limits_are_retried(HttpStatusCode status)
    {
        StubHttpMessageHandler inner = AlwaysFailing(status);
        using HttpClient client = Build(inner);

        await client.SendAsync(Request(HttpMethod.Get));

        Assert.Equal(3, inner.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Client_errors_are_not_retried(HttpStatusCode status)
    {
        StubHttpMessageHandler inner = AlwaysFailing(status);
        using HttpClient client = Build(inner);

        await client.SendAsync(Request(HttpMethod.Get));

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Stops_retrying_once_the_call_succeeds()
    {
        StubHttpMessageHandler inner = new((_, call) => StubHttpMessageHandler.Json(
            call < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            """{"ok":true}"""));
        using HttpClient client = Build(inner);

        using HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Get));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.CallCount);
        Assert.Equal(2, _delays.Count);
    }

    [Fact]
    public async Task A_single_attempt_disables_retrying()
    {
        StubHttpMessageHandler inner = AlwaysFailing();
        using HttpClient client = Build(inner, o => o.Retry.MaxAttempts = 1);

        await client.SendAsync(Request(HttpMethod.Get));

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Honours_a_raised_attempt_limit()
    {
        StubHttpMessageHandler inner = AlwaysFailing();
        using HttpClient client = Build(inner, o => o.Retry.MaxAttempts = 5);

        await client.SendAsync(Request(HttpMethod.Get));

        Assert.Equal(5, inner.CallCount);
    }

    // ---------- How long it waits ----------

    [Fact]
    public async Task Backs_off_exponentially_with_jitter()
    {
        StubHttpMessageHandler inner = AlwaysFailing();
        using HttpClient client = Build(inner, o => o.Retry.MaxAttempts = 4);

        await client.SendAsync(Request(HttpMethod.Get));

        Assert.Equal(3, _delays.Count);
        Assert.InRange(_delays[0].TotalMilliseconds, 1000, 1100);
        Assert.InRange(_delays[1].TotalMilliseconds, 2000, 2100);
        Assert.InRange(_delays[2].TotalMilliseconds, 4000, 4100);
    }

    [Fact]
    public async Task Backoff_never_exceeds_the_configured_ceiling()
    {
        StubHttpMessageHandler inner = AlwaysFailing();
        using HttpClient client = Build(inner, o =>
        {
            o.Retry.MaxAttempts = 6;
            o.Retry.MaxBackoff = TimeSpan.FromSeconds(3);
        });

        await client.SendAsync(Request(HttpMethod.Get));

        Assert.All(_delays, d => Assert.InRange(d.TotalMilliseconds, 0, 3100));
    }

    [Fact]
    public async Task Retry_after_in_seconds_wins_over_the_exponential_backoff()
    {
        StubHttpMessageHandler inner = new((_, _) =>
        {
            HttpResponseMessage response = StubHttpMessageHandler.Json(
                HttpStatusCode.TooManyRequests,
                """{"message":"slow down"}""");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return response;
        });
        using HttpClient client = Build(inner);

        await client.SendAsync(Request(HttpMethod.Get));

        Assert.All(_delays, d => Assert.Equal(TimeSpan.FromSeconds(2), d));
    }

    [Fact]
    public async Task An_outlandish_retry_after_is_capped()
    {
        // A server asking for a day must not stall a call for a day. The uncapped
        // value reaches the caller later through EmporixRateLimitException.RetryAfter.
        StubHttpMessageHandler inner = new((_, _) =>
        {
            HttpResponseMessage response = StubHttpMessageHandler.Json(
                HttpStatusCode.TooManyRequests,
                """{"message":"try tomorrow"}""");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(1));
            return response;
        });
        using HttpClient client = Build(inner);

        await client.SendAsync(Request(HttpMethod.Get));

        Assert.All(_delays, d => Assert.Equal(TimeSpan.FromSeconds(8), d));
    }

    // ---------- Request bodies ----------

    [Fact]
    public async Task Request_body_is_resent_on_every_attempt()
    {
        StubHttpMessageHandler inner = AlwaysFailing();
        using HttpClient client = Build(inner);

        using StringContent content = new("""{"name":"coffee"}""", System.Text.Encoding.UTF8, "application/json");
        await client.SendAsync(Request(HttpMethod.Put, content: content));

        Assert.Equal(3, inner.CallCount);
        Assert.All(inner.RequestBodies, b => Assert.Equal("""{"name":"coffee"}""", b));
    }

    [Fact]
    public async Task A_body_too_large_to_buffer_is_not_retried()
    {
        // A stream of unknown length cannot be sent again. Better no retry than
        // half a one.
        StubHttpMessageHandler inner = AlwaysFailing();
        using HttpClient client = Build(inner);

        using MemoryStream stream = new(new byte[64]);
        using StreamContent content = new(stream);
        content.Headers.ContentLength = null;

        await client.SendAsync(Request(HttpMethod.Put, content: content));

        Assert.Equal(1, inner.CallCount);
    }
}
