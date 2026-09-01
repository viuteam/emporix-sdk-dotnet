using Microsoft.Extensions.Configuration;
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

    [Fact]
    public void A_configuration_section_binds_to_the_options()
    {
        // The overload exists so that nobody has to write the Bind line by hand,
        // and this pins that the nested shapes arrive too — the storefront
        // context in particular, whose absence turns price lookups into a silent
        // empty list rather than an error.
        ServiceProvider provider = new ServiceCollection()
            .AddEmporix(Section(new Dictionary<string, string?>
            {
                ["Emporix:Tenant"] = "acme",
                ["Emporix:Host"] = "https://api.example.test",
                ["Emporix:Credentials:Backend:ClientId"] = "backend-id",
                ["Emporix:Credentials:Backend:Secret"] = "backend-secret",
                ["Emporix:Credentials:Storefront:ClientId"] = "storefront-id",
                ["Emporix:Credentials:Storefront:Context:Currency"] = "CHF",
                ["Emporix:Credentials:Storefront:Context:SiteCode"] = "main",
                ["Emporix:Retry:MaxAttempts"] = "5",
            }))
            .BuildServiceProvider();

        EmporixOptions options = provider.GetRequiredService<IOptions<EmporixOptions>>().Value;

        Assert.Equal("acme", options.Tenant);
        Assert.Equal("https://api.example.test", options.Host);
        Assert.Equal("backend-id", options.Credentials.Backend?.ClientId);
        Assert.Equal("storefront-id", options.Credentials.Storefront?.ClientId);
        Assert.Equal("CHF", options.Credentials.Storefront?.Context?.Currency);
        Assert.Equal("main", options.Credentials.Storefront?.Context?.SiteCode);
        Assert.Equal(5, options.Retry.MaxAttempts);
    }

    [Fact]
    public void The_named_credential_sets_bind_even_though_the_dictionary_is_read_only()
    {
        // Credentials.Custom has no setter — the binder has to add into the
        // existing dictionary. Worth pinning: it is the one property whose shape
        // could stop binding without anything else noticing.
        ServiceProvider provider = new ServiceCollection()
            .AddEmporix(Section(new Dictionary<string, string?>
            {
                ["Emporix:Tenant"] = "acme",
                ["Emporix:Credentials:Custom:import-writer:ClientId"] = "writer",
                ["Emporix:Credentials:Custom:import-writer:Secret"] = "s",
                ["Emporix:Credentials:Custom:import-writer:Scope"] = "importtool.import_trigger",
            }))
            .BuildServiceProvider();

        EmporixOptions options = provider.GetRequiredService<IOptions<EmporixOptions>>().Value;

        Assert.True(options.Credentials.Custom.ContainsKey("import-writer"));
        Assert.Equal("importtool.import_trigger", options.Credentials.Custom["import-writer"].Scope);
    }

    [Fact]
    public void A_section_missing_the_tenant_fails_when_the_options_are_read()
    {
        // Same guarantee as the delegate overload: a deployment pointed at an
        // empty section does not start. Finding that out on the first API call,
        // in production, is the failure this prevents.
        ServiceProvider provider = new ServiceCollection()
            .AddEmporix(Section(new Dictionary<string, string?>
            {
                ["Emporix:Host"] = "https://api.emporix.io",
            }))
            .BuildServiceProvider();

        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<EmporixOptions>>().Value);

        Assert.Contains("Tenant", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_and_then_adjusting_in_code_applies_in_order()
    {
        // The documented way to take everything from configuration and still
        // override one value — a tenant that comes from somewhere else, say.
        ServiceProvider provider = new ServiceCollection()
            .AddEmporix(Section(new Dictionary<string, string?>
            {
                ["Emporix:Tenant"] = "fromconfiguration",
            }))
            .Configure<EmporixOptions>(options => options.Tenant = "fromcode")
            .BuildServiceProvider();

        // Hyphens on purpose absent: the validator allows lowercase letters and
        // digits only, and a tenant is not a place to discover that.
        Assert.Equal(
            "fromcode",
            provider.GetRequiredService<IOptions<EmporixOptions>>().Value.Tenant);
    }

    [Fact]
    public void AddEmporix_rejects_a_null_configuration()
    {
        IServiceCollection services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddEmporix(configuration: null!));
        Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddEmporix(Section([])));
    }

    private static IConfigurationSection Section(Dictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection("Emporix");
}
