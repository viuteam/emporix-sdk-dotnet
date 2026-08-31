using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class EmporixAuthenticationHandlerTests
{
    private static HttpClient Build(
        StubHttpMessageHandler inner,
        FakeTokenProvider tokenProvider,
        ICustomerTokenRefresher? refresher = null,
        Action<EmporixOptions>? configure = null)
    {
        EmporixOptions options = new() { Tenant = "acme" };
        configure?.Invoke(options);

        EmporixAuthenticationHandler handler = new(
            tokenProvider,
            Options.Create(options),
            NullLogger<EmporixAuthenticationHandler>.Instance,
            refresher is null ? null : new CustomerTokenRefreshCoordinator(refresher))
        {
            InnerHandler = inner,
        };

        return new HttpClient(handler) { BaseAddress = new Uri("https://api.emporix.io") };
    }

    private static HttpRequestMessage Request(
        AuthContext auth,
        HttpMethod? method = null,
        HttpContent? content = null)
    {
        HttpRequestMessage request = new(method ?? HttpMethod.Get, "/product/acme/products")
        {
            Content = content,
        };
        request.Options.Set(EmporixRequestOptions.Auth, auth);
        return request;
    }

    private static StubHttpMessageHandler Ok()
        => new(HttpStatusCode.OK, """{"ok":true}""");

    [Fact]
    public async Task Applies_the_service_token_as_a_bearer_header()
    {
        StubHttpMessageHandler inner = Ok();
        FakeTokenProvider tokens = new();
        using HttpClient client = Build(inner, tokens);

        using HttpResponseMessage response = await client.SendAsync(Request(AuthContext.Service()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer service-1", inner.LastAuthorizationHeader);
    }

    [Fact]
    public async Task Applies_a_caller_supplied_customer_token_unchanged()
    {
        StubHttpMessageHandler inner = Ok();
        FakeTokenProvider tokens = new();
        using HttpClient client = Build(inner, tokens);

        await client.SendAsync(Request(AuthContext.Customer("customer-token")));

        Assert.Equal("Bearer customer-token", inner.LastAuthorizationHeader);
        // The SDK must not obtain a token of its own for someone else's token.
        Assert.Equal(3, tokens.ServiceTokens.Count);
    }

    [Fact]
    public async Task Sends_the_tenant_header_on_every_request()
    {
        // Emporix validates dashboard and IAM tokens against this header and
        // answers 401 without it — even though the tenant is in the path too.
        StubHttpMessageHandler inner = Ok();
        using HttpClient client = Build(inner, new FakeTokenProvider());

        await client.SendAsync(Request(AuthContext.Service()));

        Assert.Equal("acme", inner.LastHeader("Emporix-Tenant"));
    }

    [Fact]
    public async Task Sends_accept_language_when_configured()
    {
        StubHttpMessageHandler inner = Ok();
        using HttpClient client = Build(
            inner,
            new FakeTokenProvider(),
            configure: o => o.Credentials.Storefront = new EmporixStorefrontCredentials
            {
                ClientId = "public",
                Context = new EmporixStorefrontContext { Language = "de-CH" },
            });

        await client.SendAsync(Request(AuthContext.Anonymous()));

        Assert.Equal("de-CH", inner.LastHeader("Accept-Language"));
    }

    [Fact]
    public async Task Missing_auth_context_is_a_configuration_error()
    {
        StubHttpMessageHandler inner = Ok();
        using HttpClient client = Build(inner, new FakeTokenProvider());
        using HttpRequestMessage request = new(HttpMethod.Get, "/product/acme/products");

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await client.SendAsync(request));

        Assert.Equal(0, inner.CallCount);
    }

    // ---------- 401 behaviour ----------

    [Fact]
    public async Task Service_401_invalidates_and_retries_once()
    {
        StubHttpMessageHandler inner = new((_, call) => StubHttpMessageHandler.Json(
            call == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
            """{"ok":true}"""));
        FakeTokenProvider tokens = new();
        using HttpClient client = Build(inner, tokens);

        using HttpResponseMessage response = await client.SendAsync(Request(AuthContext.Service()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
        Assert.Equal(1, tokens.InvalidateServiceCalls);
        Assert.Equal("Bearer service-2", inner.LastAuthorizationHeader);
    }

    [Fact]
    public async Task Repeated_401_is_not_retried_endlessly()
    {
        StubHttpMessageHandler inner = new(HttpStatusCode.Unauthorized, """{"message":"nope"}""");
        using HttpClient client = Build(inner, new FakeTokenProvider());

        using HttpResponseMessage response = await client.SendAsync(Request(AuthContext.Service()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Anonymous_401_expires_the_access_token_but_keeps_the_session()
    {
        // Crucial: ExpireAnonymousAccessToken, not InvalidateAnonymousSession.
        // Otherwise the guest would get a new SessionId and lose the cart.
        StubHttpMessageHandler inner = new((_, call) => StubHttpMessageHandler.Json(
            call == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
            """{"ok":true}"""));
        FakeTokenProvider tokens = new();
        using HttpClient client = Build(inner, tokens);

        using HttpResponseMessage response = await client.SendAsync(Request(AuthContext.Anonymous()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, tokens.ExpireAnonymousCalls);
        Assert.Equal(0, tokens.InvalidateAnonymousCalls);
        Assert.Equal("Bearer anon-2", inner.LastAuthorizationHeader);
    }

    [Fact]
    public async Task Customer_401_propagates_without_a_refresher()
    {
        StubHttpMessageHandler inner = new(HttpStatusCode.Unauthorized, """{"message":"expired"}""");
        using HttpClient client = Build(inner, new FakeTokenProvider());

        using HttpResponseMessage response =
            await client.SendAsync(Request(AuthContext.Customer("old")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Customer_401_is_refreshed_once_when_a_refresher_is_registered()
    {
        StubHttpMessageHandler inner = new((_, call) => StubHttpMessageHandler.Json(
            call == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
            """{"ok":true}"""));
        FakeCustomerTokenRefresher refresher = new("new");
        using HttpClient client = Build(inner, new FakeTokenProvider(), refresher);

        using HttpResponseMessage response =
            await client.SendAsync(Request(AuthContext.Customer("old")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, refresher.Calls);
        Assert.Equal("old", refresher.SeenExpiredTokens[0]);
        Assert.Equal("Bearer new", inner.LastAuthorizationHeader);
    }

    [Fact]
    public async Task Customer_401_propagates_when_the_refresher_gives_up()
    {
        StubHttpMessageHandler inner = new(HttpStatusCode.Unauthorized, """{"message":"expired"}""");
        FakeCustomerTokenRefresher refresher = new(null);
        using HttpClient client = Build(inner, new FakeTokenProvider(), refresher);

        using HttpResponseMessage response =
            await client.SendAsync(Request(AuthContext.Customer("old")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, refresher.Calls);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Raw_401_is_never_refreshed()
    {
        // A raw token belongs to the caller; the SDK knows nothing of its origin.
        StubHttpMessageHandler inner = new(HttpStatusCode.Unauthorized, """{"message":"nope"}""");
        FakeCustomerTokenRefresher refresher = new("new");
        using HttpClient client = Build(inner, new FakeTokenProvider(), refresher);

        using HttpResponseMessage response = await client.SendAsync(Request(AuthContext.Raw("external")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, refresher.Calls);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Request_body_survives_the_retry_after_401()
    {
        // A body that has been sent cannot be read again — without buffering the
        // second attempt would arrive with no body.
        StubHttpMessageHandler inner = new((_, call) => StubHttpMessageHandler.Json(
            call == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
            """{"ok":true}"""));
        using HttpClient client = Build(inner, new FakeTokenProvider());

        using StringContent content = new("""{"name":"coffee"}""", System.Text.Encoding.UTF8, "application/json");
        await client.SendAsync(Request(AuthContext.Service(), HttpMethod.Post, content));

        Assert.Equal(2, inner.CallCount);
        Assert.Equal("""{"name":"coffee"}""", inner.RequestBodies[0]);
        Assert.Equal("""{"name":"coffee"}""", inner.RequestBodies[1]);
    }

    [Fact]
    public async Task Concurrent_customer_401s_trigger_only_one_refresh()
    {
        // Emporix rotates the refresh token on every renewal: two concurrent
        // renewals would invalidate each other.
        StubHttpMessageHandler inner = new((request, _) =>
            request.Headers.Authorization?.Parameter == "old"
                ? StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, """{"message":"expired"}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"ok":true}"""))
        {
            Delay = TimeSpan.FromMilliseconds(30),
        };
        FakeCustomerTokenRefresher refresher = new("new");
        using HttpClient client = Build(inner, new FakeTokenProvider(), refresher);

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ =>
                client.SendAsync(Request(AuthContext.Customer("old")))));

        Assert.All(responses, r =>
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            r.Dispose();
        });
        Assert.Equal(1, refresher.Calls);
    }
}
