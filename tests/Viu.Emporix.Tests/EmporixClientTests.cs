using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class EmporixClientTests
{
    private static EmporixOptions Valid() => new()
    {
        Tenant = "acme",
        Credentials = { Storefront = new EmporixStorefrontCredentials { ClientId = "public" } },
    };

    [Fact]
    public void A_standalone_client_needs_nothing_but_options()
    {
        // The no-container case: a script should get by with one line.
        using EmporixClient client = new(Valid());

        Assert.Equal("acme", client.Tenant);
        Assert.NotNull(client.Products);
    }

    [Fact]
    public void An_invalid_configuration_fails_at_construction()
    {
        // Without a container there is no startup check — the failure has to
        // land here, not on the first call.
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => new EmporixClient(new EmporixOptions { Tenant = "INVALID" }));

        Assert.Contains(exception.Failures, f => f.Contains("Tenant", StringComparison.Ordinal));
    }

    [Fact]
    public void Services_are_created_once_and_reused()
    {
        using EmporixClient client = new(Valid());

        Assert.Same(client.Products, client.Products);
        Assert.Same(client.Carts, client.Carts);
        Assert.Same(client.Categories, client.Categories);
    }

    [Fact]
    public void Every_service_is_reachable_from_the_client()
    {
        using EmporixClient client = new(Valid());

        Assert.NotNull(client.Products);
        Assert.NotNull(client.Carts);
        Assert.NotNull(client.Categories);
        Assert.NotNull(client.Brands);
        Assert.NotNull(client.Labels);
        Assert.NotNull(client.Catalogs);
    }

    [Fact]
    public void A_disposed_client_refuses_every_service()
    {
        EmporixClient client = new(Valid());
        client.Dispose();

        Assert.Throws<ObjectDisposedException>(() => client.Carts);
        Assert.Throws<ObjectDisposedException>(() => client.Categories);
    }

    [Fact]
    public void A_disposed_client_rejects_further_use()
    {
        EmporixClient client = new(Valid());
        client.Dispose();

        Assert.Throws<ObjectDisposedException>(() => client.Products);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        EmporixClient client = new(Valid());

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public void The_container_supplies_a_singleton_client()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddEmporix(options =>
        {
            options.Tenant = "acme";
            options.Credentials.Backend = new EmporixServiceCredentials { ClientId = "id", Secret = "s" };
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        EmporixClient first = provider.GetRequiredService<EmporixClient>();
        EmporixClient second = provider.GetRequiredService<EmporixClient>();

        Assert.Same(first, second);
        Assert.Equal("acme", first.Tenant);
    }

    [Fact]
    public void A_client_from_the_container_owns_nothing_and_survives_disposal()
    {
        // The connections belong to the container. Disposing the client must not
        // take them away — otherwise a single call would destroy the client for
        // everyone else.
        ServiceCollection services = new();
        services.AddLogging();
        services.AddEmporix(options => options.Tenant = "acme");

        using ServiceProvider provider = services.BuildServiceProvider();
        EmporixClient client = provider.GetRequiredService<EmporixClient>();

        client.Dispose();

        Assert.NotNull(provider.GetRequiredService<EmporixClient>().Products);
    }
}
