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
}
