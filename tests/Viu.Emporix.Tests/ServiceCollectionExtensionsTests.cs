using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEmporix_resolves_configured_options()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddEmporix(options =>
            {
                options.Tenant = "acme";
                options.Credentials.Backend = new EmporixServiceCredentials
                {
                    ClientId = "id",
                    Secret = "secret",
                };
            })
            .BuildServiceProvider();

        EmporixOptions options = provider.GetRequiredService<IOptions<EmporixOptions>>().Value;

        Assert.Equal("acme", options.Tenant);
        Assert.Equal("id", options.Credentials.Backend!.ClientId);
    }

    [Fact]
    public void AddEmporix_rejects_invalid_configuration()
    {
        // The validator has to take effect through the DI registration, not only
        // when called directly.
        ServiceProvider provider = new ServiceCollection()
            .AddEmporix(options => options.Tenant = "INVALID")
            .BuildServiceProvider();

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<EmporixOptions>>().Value);

        Assert.Contains(exception.Failures, f => f.Contains("Tenant", StringComparison.Ordinal));
    }

    [Fact]
    public void AddEmporix_rejects_null_arguments()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddEmporix(configure: null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddEmporix(_ => { }));
    }

    [Fact]
    public void AddEmporix_is_chainable()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Same(services, services.AddEmporix(options => options.Tenant = "acme"));
    }

    [Fact]
    public void The_wave_five_services_are_registered_and_reachable_from_the_client()
    {
        // Wiring a service means touching three files — the facade, the client
        // property and the container registration. Forgetting the third
        // compiles, passes every other test, and fails at run time in whichever
        // application resolves it first.
        ServiceProvider provider = new ServiceCollection()
            .AddEmporix(options => options.Tenant = "acme")
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ImportService>());
        Assert.NotNull(provider.GetRequiredService<IndexingService>());
        Assert.NotNull(provider.GetRequiredService<PickPackService>());
        Assert.NotNull(provider.GetRequiredService<ShoppingListService>());
        Assert.NotNull(provider.GetRequiredService<RewardPointsService>());
        Assert.NotNull(provider.GetRequiredService<AiService>());
        Assert.NotNull(provider.GetRequiredService<RagIndexerService>());
        Assert.NotNull(provider.GetRequiredService<CloudFunctionService>());
        Assert.NotNull(provider.GetRequiredService<AuditLogService>());

        EmporixClient client = provider.GetRequiredService<EmporixClient>();

        Assert.NotNull(client.Imports);
        Assert.NotNull(client.Indexing);
        Assert.NotNull(client.PickPack);
        Assert.NotNull(client.ShoppingLists);
        Assert.NotNull(client.RewardPoints);
        Assert.NotNull(client.Ai);
        Assert.NotNull(client.RagIndexer);
        Assert.NotNull(client.CloudFunctions);
        Assert.NotNull(client.AuditLogs);

        // The nested AI groups are built on demand; a wrong tenant or a missing
        // constructor argument would only show here.
        Assert.NotNull(client.Ai.Agents);
        Assert.NotNull(client.Ai.Tools);
        Assert.NotNull(client.Ai.Tokens);
        Assert.NotNull(client.Ai.OAuths);
        Assert.NotNull(client.Ai.McpServers);
        Assert.NotNull(client.Ai.Templates);
        Assert.NotNull(client.Ai.Conversations);
        Assert.NotNull(client.Ai.Jobs);
        Assert.NotNull(client.Ai.Logs);
    }
}
