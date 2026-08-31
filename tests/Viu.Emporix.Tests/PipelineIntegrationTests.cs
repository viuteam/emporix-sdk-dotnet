using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Viu.Emporix.Tests;

/// <summary>
/// Checks how the chain wired by <c>AddEmporix</c> works together: retry on the
/// outside, authentication inside, interpretation in the client.
/// </summary>
public class PipelineIntegrationTests
{
    private static ServiceProvider Build(
        StubHttpMessageHandler api,
        StubHttpMessageHandler token,
        Action<IServiceCollection>? extra = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        extra?.Invoke(services);

        services.AddEmporix(options =>
        {
            options.Tenant = "acme";
            options.Credentials.Backend = new EmporixServiceCredentials { ClientId = "id", Secret = "s" };
            options.Credentials.Storefront = new EmporixStorefrontCredentials { ClientId = "public" };
            // Keep the waits short: the retry path is under test, not the
            // patience of the suite.
            options.Retry.MaxBackoff = TimeSpan.FromMilliseconds(1);
        });

        // Replace the real endpoints with doubles — the handler chain above them
        // stays the wired one.
        services.AddHttpClient(ServiceCollectionExtensions.ApiHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => api);
        services.AddHttpClient(ServiceCollectionExtensions.TokenHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => token);

        return services.BuildServiceProvider();
    }

    private static StubHttpMessageHandler TokenEndpoint()
        => new(HttpStatusCode.OK, """{"access_token":"svc-token","expires_in":3600}""");

    private static EmporixRequest Get() => new()
    {
        Method = HttpMethod.Get,
        Path = "/product/acme/products",
        Auth = AuthContext.Service(),
    };

    [Fact]
    public async Task Resolves_the_client_and_completes_a_call()
    {
        StubHttpMessageHandler api = new(HttpStatusCode.OK, """{"id":"p1","name":"coffee"}""");
        using ServiceProvider provider = Build(api, TokenEndpoint());

        EmporixHttpClient client = provider.GetRequiredService<EmporixHttpClient>();
        TestProduct? product = await client.SendAsync(Get(), TestJsonContext.Default.TestProduct);

        Assert.Equal("coffee", product?.Name);
        // The token came from the token supply, not out of nowhere.
        Assert.Equal("Bearer svc-token", api.LastAuthorizationHeader);
        Assert.Equal("acme", api.LastHeader("Emporix-Tenant"));
    }

    [Fact]
    public async Task Retry_runs_outside_authentication_so_the_second_try_gets_a_fresh_token()
    {
        // This is the reason for the order: after a 503 the second attempt must
        // pass through authentication again.
        StubHttpMessageHandler api = new((_, call) => StubHttpMessageHandler.Json(
            call == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            """{"id":"p1"}"""));
        using ServiceProvider provider = Build(api, TokenEndpoint());

        EmporixHttpClient client = provider.GetRequiredService<EmporixHttpClient>();
        TestProduct? product = await client.SendAsync(Get(), TestJsonContext.Default.TestProduct);

        Assert.Equal("p1", product?.Id);
        Assert.Equal(2, api.CallCount);
        Assert.Equal("Bearer svc-token", api.HeaderAt(1, "Authorization"));
    }

    [Fact]
    public async Task A_persistent_server_error_surfaces_as_a_server_exception()
    {
        StubHttpMessageHandler api = new(HttpStatusCode.ServiceUnavailable, """{"message":"maintenance"}""");
        using ServiceProvider provider = Build(api, TokenEndpoint());

        EmporixHttpClient client = provider.GetRequiredService<EmporixHttpClient>();

        EmporixServerException exception = await Assert.ThrowsAsync<EmporixServerException>(
            async () => await client.SendAsync(Get(), TestJsonContext.Default.TestProduct));

        Assert.Equal(3, api.CallCount); // attempts exhausted
        Assert.NotNull(exception.CorrelationId);
        Assert.Contains("maintenance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_401_is_answered_with_one_fresh_token_and_one_retry()
    {
        StubHttpMessageHandler api = new((_, call) => StubHttpMessageHandler.Json(
            call == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
            """{"id":"p1"}"""));
        StubHttpMessageHandler token = TokenEndpoint();
        using ServiceProvider provider = Build(api, token);

        EmporixHttpClient client = provider.GetRequiredService<EmporixHttpClient>();
        await client.SendAsync(Get(), TestJsonContext.Default.TestProduct);

        Assert.Equal(2, api.CallCount);
        // Obtained twice: once at the start, once after the token was discarded.
        Assert.Equal(2, token.CallCount);
    }

    [Fact]
    public async Task A_post_is_not_retried_even_through_the_full_chain()
    {
        // The promise must hold through the entire wired chain, not just in the
        // isolated handler test.
        StubHttpMessageHandler api = new(HttpStatusCode.ServiceUnavailable, """{"message":"broken"}""");
        using ServiceProvider provider = Build(api, TokenEndpoint());

        EmporixHttpClient client = provider.GetRequiredService<EmporixHttpClient>();

        await Assert.ThrowsAsync<EmporixServerException>(async () => await client.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = "/order/acme/orders",
                Auth = AuthContext.Service(),
            },
            TestJsonContext.Default.TestProduct));

        Assert.Equal(1, api.CallCount);
    }

    [Fact]
    public async Task A_registered_customer_token_refresher_is_picked_up()
    {
        StubHttpMessageHandler api = new((request, _) =>
            request.Headers.Authorization?.Parameter == "old"
                ? StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, """{"message":"expired"}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"id":"p1"}"""));
        FakeCustomerTokenRefresher refresher = new("new");

        using ServiceProvider provider = Build(
            api,
            TokenEndpoint(),
            services => services.AddSingleton<ICustomerTokenRefresher>(refresher));

        EmporixHttpClient client = provider.GetRequiredService<EmporixHttpClient>();
        await client.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = "/customer/acme/me",
                Auth = AuthContext.Customer("old"),
            },
            TestJsonContext.Default.TestProduct);

        Assert.Equal(1, refresher.Calls);
        Assert.Equal("Bearer new", api.LastAuthorizationHeader);
    }
}
