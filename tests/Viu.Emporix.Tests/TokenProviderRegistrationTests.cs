using Microsoft.Extensions.DependencyInjection;

namespace Viu.Emporix.Tests;

public class TokenProviderRegistrationTests
{
    private static ServiceProvider Build(Action<IServiceCollection>? extra = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        extra?.Invoke(services);
        services.AddEmporix(options =>
        {
            options.Tenant = "acme";
            options.Credentials.Backend = new EmporixServiceCredentials { ClientId = "id", Secret = "s" };
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_the_default_token_provider()
    {
        using ServiceProvider provider = Build();

        Assert.IsType<DefaultTokenProvider>(provider.GetRequiredService<ITokenProvider>());
    }

    [Fact]
    public void Token_provider_is_a_singleton()
    {
        // Crucial: a token cache in a short-lived object would have no effect,
        // and every request would obtain a fresh token.
        using ServiceProvider provider = Build();

        ITokenProvider first = provider.GetRequiredService<ITokenProvider>();
        ITokenProvider second = provider.GetRequiredService<ITokenProvider>();

        Assert.Same(first, second);
    }

    [Fact]
    public void Own_token_provider_registered_earlier_wins()
    {
        using ServiceProvider provider = Build(services =>
            services.AddSingleton<ITokenProvider, CustomTokenProvider>());

        Assert.IsType<CustomTokenProvider>(provider.GetRequiredService<ITokenProvider>());
    }

    [Fact]
    public void Configures_the_token_http_client_from_the_options()
    {
        using ServiceProvider provider = Build();

        HttpClient client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionExtensions.TokenHttpClientName);

        Assert.Equal(new Uri("https://api.emporix.io"), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(60), client.Timeout);
    }

    private sealed class CustomTokenProvider : ITokenProvider
    {
        public ValueTask<string> GetServiceTokenAsync(string credentialSet, CancellationToken cancellationToken = default)
            => ValueTask.FromResult("external");

        public ValueTask<AnonymousSession> GetAnonymousSessionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void InvalidateServiceToken(string credentialSet)
        {
        }

        public void ExpireAnonymousAccessToken()
        {
        }

        public void InvalidateAnonymousSession()
        {
        }
    }
}
