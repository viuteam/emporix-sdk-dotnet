using System.Net;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class EmporixHttpClientTests
{
    private static EmporixHttpClient Create(StubHttpMessageHandler handler, string host = "https://api.emporix.io")
        => new(
            new HttpClient(handler),
            Options.Create(new EmporixOptions { Tenant = "acme", Host = host }));

    private static EmporixRequest Get(
        string path = "/product/acme/products",
        IReadOnlyList<KeyValuePair<string, string?>>? query = null)
        => new()
        {
            Method = HttpMethod.Get,
            Path = path,
            Auth = AuthContext.Service(),
            Query = query,
        };

    // ---------- Address building ----------

    [Fact]
    public async Task Builds_the_address_from_host_and_path()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"p1"}""");
        EmporixHttpClient client = Create(handler);

        await client.SendAsync(Get(), TestJsonContext.Default.TestProduct);

        Assert.Equal("https://api.emporix.io/product/acme/products", handler.RequestUris[0].ToString());
    }

    [Fact]
    public async Task Appends_query_parameters()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        EmporixHttpClient client = Create(handler);

        await client.SendAsync(
            Get(query: [new("pageNumber", "2"), new("pageSize", "50")]),
            TestJsonContext.Default.ListTestProduct);

        Assert.Equal(
            "https://api.emporix.io/product/acme/products?pageNumber=2&pageSize=50",
            handler.RequestUris[0].ToString());
    }

    [Fact]
    public async Task Omits_query_parameters_without_a_value()
    {
        // An unset optional filter must not arrive as an empty value — Emporix
        // would then filter on the empty string rather than not at all.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        EmporixHttpClient client = Create(handler);

        await client.SendAsync(
            Get(query: [new("q", null), new("pageSize", "50"), new("sort", null)]),
            TestJsonContext.Default.ListTestProduct);

        Assert.Equal(
            "https://api.emporix.io/product/acme/products?pageSize=50",
            handler.RequestUris[0].ToString());
    }

    [Fact]
    public async Task Escapes_query_values()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        EmporixHttpClient client = Create(handler);

        await client.SendAsync(
            Get(query: [new("q", "name:(~Kaffee & Tee)")]),
            TestJsonContext.Default.ListTestProduct);

        // AbsoluteUri, not ToString(): the latter returns the decoded display
        // form and would hide the very encoding being checked.
        string uri = handler.RequestUris[0].AbsoluteUri;
        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
        Assert.Contains("%26", uri, StringComparison.Ordinal); // das kaufmännische Und
    }

    [Fact]
    public async Task Honours_a_custom_host()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"p1"}""");
        EmporixHttpClient client = Create(handler, "https://api.stage.emporix.io");

        await client.SendAsync(Get(), TestJsonContext.Default.TestProduct);

        Assert.StartsWith(
            "https://api.stage.emporix.io/",
            handler.RequestUris[0].ToString(),
            StringComparison.Ordinal);
    }

    // ---------- Correlation ----------

    [Fact]
    public async Task Sends_a_correlation_id()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"p1"}""");
        EmporixHttpClient client = Create(handler);

        await client.SendAsync(Get(), TestJsonContext.Default.TestProduct);

        Assert.False(string.IsNullOrWhiteSpace(handler.LastHeader("X-Correlation-Id")));
    }

    [Fact]
    public async Task Failures_carry_the_correlation_id_that_was_sent()
    {
        // The whole point: the id from the error must be findable in the server
        // logs.
        StubHttpMessageHandler handler = new(HttpStatusCode.NotFound, """{"message":"gone"}""");
        EmporixHttpClient client = Create(handler);

        EmporixNotFoundException exception = await Assert.ThrowsAsync<EmporixNotFoundException>(
            async () => await client.SendAsync(Get(), TestJsonContext.Default.TestProduct));

        Assert.Equal(handler.LastHeader("X-Correlation-Id"), exception.CorrelationId);
    }

    // ---------- Interpreting the response ----------

    [Fact]
    public async Task Deserializes_the_response()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"p1","name":"coffee"}""");
        EmporixHttpClient client = Create(handler);

        TestProduct? product = await client.SendAsync(Get(), TestJsonContext.Default.TestProduct);

        Assert.NotNull(product);
        Assert.Equal("p1", product.Id);
        Assert.Equal("coffee", product.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_success_body_yields_no_value(string body)
    {
        // Plenty of Emporix endpoints answer successfully with no body.
        StubHttpMessageHandler handler = new(
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        EmporixHttpClient client = Create(handler);

        Assert.Null(await client.SendAsync(Get(), TestJsonContext.Default.TestProduct));
    }

    [Fact]
    public async Task An_unreadable_success_body_is_reported_as_an_api_error()
    {
        // Unlike an error response, an unreadable body is a genuine problem here —
        // but a JsonException would be the wrong answer.
        StubHttpMessageHandler handler = new(
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>kein json</html>"),
            });
        EmporixHttpClient client = Create(handler);

        EmporixApiException exception = await Assert.ThrowsAsync<EmporixApiException>(
            async () => await client.SendAsync(Get(), TestJsonContext.Default.TestProduct));

        Assert.Equal("<html>kein json</html>", exception.RawBody);
    }

    [Fact]
    public async Task Error_status_becomes_the_matching_exception()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.Forbidden,
            """{"message":"no","details":["missing scope: product.product_manage"]}""");
        EmporixHttpClient client = Create(handler);

        EmporixInsufficientScopeException exception =
            await Assert.ThrowsAsync<EmporixInsufficientScopeException>(
                async () => await client.SendAsync(Get(), TestJsonContext.Default.TestProduct));

        Assert.Equal("product.product_manage", exception.RequiredScope);
    }

    [Fact]
    public async Task Rate_limit_passes_retry_after_to_the_caller()
    {
        StubHttpMessageHandler handler = new((_, _) =>
        {
            HttpResponseMessage response = StubHttpMessageHandler.Json(
                HttpStatusCode.TooManyRequests,
                """{"message":"slow down"}""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds(45));
            return response;
        });
        EmporixHttpClient client = Create(handler);

        EmporixRateLimitException exception = await Assert.ThrowsAsync<EmporixRateLimitException>(
            async () => await client.SendAsync(Get(), TestJsonContext.Default.TestProduct));

        // Forwarded uncapped — the cap applies only to the SDK's own retries.
        Assert.Equal(TimeSpan.FromSeconds(45), exception.RetryAfter);
    }

    // ---------- Transport failures ----------

    [Fact]
    public async Task A_network_failure_becomes_a_network_exception()
    {
        StubHttpMessageHandler handler = new((_, _) => throw new HttpRequestException("DNS failed"));
        EmporixHttpClient client = Create(handler);

        EmporixNetworkException exception = await Assert.ThrowsAsync<EmporixNetworkException>(
            async () => await client.SendAsync(Get(), TestJsonContext.Default.TestProduct));

        Assert.NotNull(exception.CorrelationId);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task A_timeout_becomes_a_timeout_exception()
    {
        StubHttpMessageHandler handler = new((_, _) => throw new TaskCanceledException("too slow"));
        EmporixHttpClient client = Create(handler);

        EmporixTimeoutException exception = await Assert.ThrowsAsync<EmporixTimeoutException>(
            async () => await client.SendAsync(Get(), TestJsonContext.Default.TestProduct));

        Assert.Equal(TimeSpan.FromSeconds(60), exception.Timeout);
    }

    [Fact]
    public async Task A_caller_cancellation_is_not_disguised_as_a_timeout()
    {
        // Important distinction: whoever cancels should see a cancellation, not
        // be led to believe the server was slow.
        StubHttpMessageHandler handler = new((_, _) => throw new TaskCanceledException("abgebrochen"));
        EmporixHttpClient client = Create(handler);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.SendAsync(Get(), TestJsonContext.Default.TestProduct, cts.Token));
    }

    // ---------- Passing to the handler chain ----------

    [Fact]
    public async Task Passes_the_auth_context_and_idempotency_flag_along()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"p1"}""");
        EmporixHttpClient client = Create(handler);

        EmporixRequest request = new()
        {
            Method = HttpMethod.Post,
            Path = "/product/acme/products/search",
            Auth = AuthContext.Anonymous(),
            Idempotent = true,
        };

        await client.SendAsync(request, TestJsonContext.Default.TestProduct);

        HttpRequestMessage seen = handler.LastRequest!;
        Assert.True(seen.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth));
        Assert.Equal(AuthKind.Anonymous, auth.Kind);
        Assert.True(seen.Options.TryGetValue(EmporixRequestOptions.Idempotent, out bool idempotent));
        Assert.True(idempotent);
    }

    [Fact]
    public async Task Raw_responses_are_returned_untouched()
    {
        // For file downloads: here the caller decides what counts as a failure.
        StubHttpMessageHandler handler = new(HttpStatusCode.NotFound, "not here");
        EmporixHttpClient client = Create(handler);

        using HttpResponseMessage response = await client.SendRawAsync(Get());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
