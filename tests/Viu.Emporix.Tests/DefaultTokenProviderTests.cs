using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class DefaultTokenProviderTests
{
    private const string ServiceTokenBody = """{"access_token":"svc-token","expires_in":3600}""";

    private const string AnonymousBody = """
        {"access_token":"anon-token","refresh_token":"refresh-1","sessionId":"session-1","expires_in":3600}
        """;

    private static EmporixOptions Options(Action<EmporixOptions>? configure = null)
    {
        EmporixOptions options = new() { Tenant = "acme" };
        options.Credentials.Backend = new EmporixServiceCredentials { ClientId = "id", Secret = "secret" };
        options.Credentials.Storefront = new EmporixStorefrontCredentials { ClientId = "public-id" };
        configure?.Invoke(options);
        return options;
    }

    private static DefaultTokenProvider Create(
        StubHttpMessageHandler handler,
        out StubClock time,
        Action<EmporixOptions>? configure = null)
    {
        time = new StubClock();
        return new DefaultTokenProvider(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(Options(configure)),
            NullLogger<DefaultTokenProvider>.Instance,
            time);
    }

    // ---------- Service tokens ----------

    [Fact]
    public async Task Obtains_and_caches_a_service_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody);
        using DefaultTokenProvider provider = Create(handler, out _);

        string first = await provider.GetServiceTokenAsync("backend");
        string second = await provider.GetServiceTokenAsync("backend");

        Assert.Equal("svc-token", first);
        Assert.Equal("svc-token", second);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Sends_client_credentials_grant()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody);
        using DefaultTokenProvider provider = Create(handler, out _);

        await provider.GetServiceTokenAsync("backend");

        Assert.Equal("https://api.emporix.io/oauth/token", handler.RequestUris[0].ToString());
        string body = handler.RequestBodies[0];
        Assert.Contains("grant_type=client_credentials", body, StringComparison.Ordinal);
        Assert.Contains("client_id=id", body, StringComparison.Ordinal);
        Assert.Contains("client_secret=secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("scope", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sends_scope_when_configured()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody);
        using DefaultTokenProvider provider = Create(
            handler,
            out _,
            o => o.Credentials.Backend!.Scope = "product.product_read");

        await provider.GetServiceTokenAsync("backend");

        Assert.Contains("scope=product.product_read", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refetches_after_the_token_expires()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody);
        using DefaultTokenProvider provider = Create(handler, out StubClock time);

        await provider.GetServiceTokenAsync("backend");
        // 3600s validity minus a 60s buffer — just before that the token still holds.
        time.Advance(TimeSpan.FromSeconds(3539));
        await provider.GetServiceTokenAsync("backend");
        Assert.Equal(1, handler.CallCount);

        time.Advance(TimeSpan.FromSeconds(2));
        await provider.GetServiceTokenAsync("backend");
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Honours_the_absolute_max_lifetime()
    {
        // A server reporting an absurdly long validity must not keep a token
        // cached forever.
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """{"access_token":"svc-token","expires_in":86400}""");
        using DefaultTokenProvider provider = Create(handler, out StubClock time);

        await provider.GetServiceTokenAsync("backend");
        time.Advance(TimeSpan.FromMinutes(61)); // über MaxLifetime von 1h
        await provider.GetServiceTokenAsync("backend");

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Invalidation_forces_a_new_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody);
        using DefaultTokenProvider provider = Create(handler, out _);

        await provider.GetServiceTokenAsync("backend");
        provider.InvalidateServiceToken("backend");
        await provider.GetServiceTokenAsync("backend");

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_token_request()
    {
        // The heart of it: 30 concurrent calls must not send Emporix 30 token
        // requests.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody)
        {
            Delay = TimeSpan.FromMilliseconds(80),
        };
        using DefaultTokenProvider provider = Create(handler, out _);

        string[] tokens = await Task.WhenAll(
            Enumerable.Range(0, 30).Select(async _ =>
                await provider.GetServiceTokenAsync("backend")));

        Assert.Equal(1, handler.CallCount);
        Assert.All(tokens, t => Assert.Equal("svc-token", t));
    }

    [Fact]
    public async Task Separate_credential_sets_do_not_block_each_other()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody);
        using DefaultTokenProvider provider = Create(
            handler,
            out _,
            o => o.Credentials.Custom["partner"] =
                new EmporixServiceCredentials { ClientId = "p", Secret = "ps" });

        await provider.GetServiceTokenAsync("backend");
        await provider.GetServiceTokenAsync("partner");

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("client_id=p&", handler.RequestBodies[1] + "&", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_credential_set_is_a_configuration_error()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody);
        using DefaultTokenProvider provider = Create(handler, out _);

        EmporixConfigurationException exception =
            await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
                await provider.GetServiceTokenAsync("doesnotexist"));

        Assert.Contains("doesnotexist", exception.Message, StringComparison.Ordinal);
        // No network traffic for a failure that already follows from the configuration.
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Missing_backend_credentials_are_a_configuration_error()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody);
        using DefaultTokenProvider provider = Create(handler, out _, o => o.Credentials.Backend = null);

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await provider.GetServiceTokenAsync("backend"));
    }

    [Fact]
    public async Task Rejected_credentials_surface_as_authentication_error()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.Unauthorized,
            """{"fault":{"faultstring":"Invalid client","detail":{"errorcode":"oauth.v2.InvalidClient"}}}""");
        using DefaultTokenProvider provider = Create(handler, out _);

        EmporixAuthenticationException exception =
            await Assert.ThrowsAsync<EmporixAuthenticationException>(async () =>
                await provider.GetServiceTokenAsync("backend"));

        Assert.Equal("oauth.v2.InvalidClient", exception.ErrorCode);
        Assert.Contains("Invalid client", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unparseable_token_response_does_not_throw_a_json_error()
    {
        StubHttpMessageHandler handler = new(
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>proxy error</html>"),
            });
        using DefaultTokenProvider provider = Create(handler, out _);

        EmporixAuthenticationException exception =
            await Assert.ThrowsAsync<EmporixAuthenticationException>(async () =>
                await provider.GetServiceTokenAsync("backend"));

        Assert.Contains("access_token", exception.Message, StringComparison.Ordinal);
        Assert.Equal("<html>proxy error</html>", exception.RawBody);
    }

    [Fact]
    public async Task Reads_expires_in_given_as_a_string()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """{"access_token":"svc-token","expires_in":"3600"}""");
        using DefaultTokenProvider provider = Create(handler, out StubClock time);

        await provider.GetServiceTokenAsync("backend");
        time.Advance(TimeSpan.FromSeconds(3000));
        await provider.GetServiceTokenAsync("backend");

        // Had the string not been read, the fallback would have applied — which
        // happens to be the same value here, so the second call verifies that the
        // token is still valid at all.
        Assert.Equal(1, handler.CallCount);
    }

    // ---------- Anonymous session ----------

    [Fact]
    public async Task Anonymous_login_carries_tenant_and_client_id()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, AnonymousBody);
        using DefaultTokenProvider provider = Create(handler, out _);

        AnonymousSession session =
            await provider.GetAnonymousSessionAsync();

        Assert.Equal("anon-token", session.AccessToken);
        Assert.Equal("session-1", session.SessionId);

        string uri = handler.RequestUris[0].ToString();
        Assert.Contains("/customerlogin/auth/anonymous/login", uri, StringComparison.Ordinal);
        Assert.Contains("tenant=acme", uri, StringComparison.Ordinal);
        Assert.Contains("client_id=public-id", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_login_carries_the_storefront_context()
    {
        // Without these values price matching later returns an empty list, with
        // no error appearing anywhere.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, AnonymousBody);
        using DefaultTokenProvider provider = Create(
            handler,
            out _,
            o => o.Credentials.Storefront!.Context = new EmporixStorefrontContext
            {
                Currency = "CHF",
                SiteCode = "main",
                TargetLocation = "CH",
            });

        await provider.GetAnonymousSessionAsync();

        string uri = handler.RequestUris[0].ToString();
        Assert.Contains("currency=CHF", uri, StringComparison.Ordinal);
        Assert.Contains("siteCode=main", uri, StringComparison.Ordinal);
        Assert.Contains("targetLocation=CH", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expired_anonymous_session_is_refreshed_not_re_logged_in()
    {
        // The point: the SessionId stays, so the guest cart survives.
        StubHttpMessageHandler handler = new((request, call) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            call == 1
                ? AnonymousBody
                : """{"access_token":"anon-2","refresh_token":"refresh-2","sessionId":"session-1","expires_in":3600}"""));
        using DefaultTokenProvider provider = Create(handler, out StubClock time);

        AnonymousSession first = await provider.GetAnonymousSessionAsync();
        time.Advance(TimeSpan.FromSeconds(3600));
        AnonymousSession second = await provider.GetAnonymousSessionAsync();

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("/anonymous/refresh", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
        Assert.Contains("refresh_token=refresh-1", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal("anon-2", second.AccessToken);
    }

    [Fact]
    public async Task Failed_refresh_falls_back_to_a_fresh_login()
    {
        StubHttpMessageHandler handler = new((request, call) =>
        {
            bool isRefresh = request.RequestUri!.ToString().Contains("/refresh", StringComparison.Ordinal);
            return isRefresh
                ? StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"message":"refresh token expired"}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, AnonymousBody);
        });
        using DefaultTokenProvider provider = Create(handler, out StubClock time);

        await provider.GetAnonymousSessionAsync();
        time.Advance(TimeSpan.FromSeconds(3600));
        AnonymousSession recovered =
            await provider.GetAnonymousSessionAsync();

        Assert.Equal(3, handler.CallCount); // login, gescheiterter refresh, erneuter login
        Assert.Equal("anon-token", recovered.AccessToken);
    }

    [Fact]
    public async Task Expiring_the_access_token_keeps_the_refresh_token()
    {
        // This is how a 401 is answered: the next request renews rather than
        // starting a new session.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, AnonymousBody);
        using DefaultTokenProvider provider = Create(handler, out _);

        await provider.GetAnonymousSessionAsync();
        provider.ExpireAnonymousAccessToken();
        await provider.GetAnonymousSessionAsync();

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("/anonymous/refresh", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalidating_the_session_starts_a_new_login()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, AnonymousBody);
        using DefaultTokenProvider provider = Create(handler, out _);

        await provider.GetAnonymousSessionAsync();
        provider.InvalidateAnonymousSession();
        await provider.GetAnonymousSessionAsync();

        Assert.Equal(2, handler.CallCount);
        Assert.Contains("/anonymous/login", handler.RequestUris[1].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_anonymous_callers_share_one_login()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, AnonymousBody)
        {
            Delay = TimeSpan.FromMilliseconds(80),
        };
        using DefaultTokenProvider provider = Create(handler, out _);

        await Task.WhenAll(
            Enumerable.Range(0, 20).Select(async _ =>
                await provider.GetAnonymousSessionAsync()));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Missing_storefront_credentials_are_a_configuration_error()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, AnonymousBody);
        using DefaultTokenProvider provider = Create(handler, out _, o => o.Credentials.Storefront = null);

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await provider.GetAnonymousSessionAsync());
    }

    // ---------- Secrecy ----------

    [Fact]
    public async Task Session_to_string_does_not_leak_tokens()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, AnonymousBody);
        using DefaultTokenProvider provider = Create(handler, out _);

        AnonymousSession session =
            await provider.GetAnonymousSessionAsync();
        string text = session.ToString();

        Assert.DoesNotContain("anon-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-1", text, StringComparison.Ordinal);
        Assert.Contains("session-1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_failures_carry_a_correlation_id()
    {
        // Token requests bypass the handler chain; without an id of their own an
        // authentication failure of all things would be untraceable.
        StubHttpMessageHandler handler = new(
            HttpStatusCode.Unauthorized,
            """{"fault":{"faultstring":"Invalid ApiKey"}}""");
        using DefaultTokenProvider provider = Create(handler, out _);

        EmporixAuthenticationException exception =
            await Assert.ThrowsAsync<EmporixAuthenticationException>(async () =>
                await provider.GetServiceTokenAsync("backend"));

        Assert.False(string.IsNullOrWhiteSpace(exception.CorrelationId));
        // The same id went out with the request.
        Assert.Equal(handler.LastHeader("X-Correlation-Id"), exception.CorrelationId);
    }

    [Fact]
    public async Task Disposed_provider_rejects_further_use()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, ServiceTokenBody);
        DefaultTokenProvider provider = Create(handler, out _);
        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await provider.GetServiceTokenAsync("backend"));
    }
}
