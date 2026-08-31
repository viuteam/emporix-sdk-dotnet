using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Registers the Emporix SDK in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> used for the token endpoints.
    /// </summary>
    /// <remarks>
    /// Public so the client can be adjusted afterwards — to insert a proxy, say.
    /// </remarks>
    public const string TokenHttpClientName = "Viu.Emporix.Token";

    /// <summary>
    /// The name of the <see cref="HttpClient"/> used for API calls.
    /// </summary>
    /// <remarks>
    /// Public so the chain can be extended afterwards — to add your own tracing,
    /// for instance.
    /// </remarks>
    public const string ApiHttpClientName = "Viu.Emporix.Api";

    /// <summary>
    /// Registers options, startup validation, token supply and services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets tenant, credentials and fine-tuning.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// An incomplete configuration fails at application startup, not on the first
    /// API call.
    /// <para>
    /// The token supply is registered as <see cref="ITokenProvider"/>. Your own
    /// implementation, registered before this call, is left in place — that is
    /// how you attach an existing token supply.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddEmporix(options =>
    /// {
    ///     options.Tenant = "mytenant";
    ///     options.Credentials.Backend = new EmporixServiceCredentials
    ///     {
    ///         ClientId = "...",
    ///         Secret = "...",
    ///     };
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddEmporix(
        this IServiceCollection services,
        Action<EmporixOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<EmporixOptions>()
            .Configure(configure)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<EmporixOptions>, EmporixOptionsValidator>();

        services.AddHttpClient(TokenHttpClientName, static (provider, client) =>
            {
                EmporixOptions options = provider.GetRequiredService<IOptions<EmporixOptions>>().Value;
                client.BaseAddress = new Uri(options.Host);
                client.Timeout = options.Timeouts.Read;
            })
            // The token provider is a singleton — it has to hold the token cache
            // across requests, or it would have no effect. It therefore takes its
            // HttpClient exactly once, and a long-lived HttpClient only notices
            // DNS changes if its connections age out on their own. That is what
            // these two lines ensure.
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        services.TryAddSingleton<ITokenProvider>(static provider => new DefaultTokenProvider(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(TokenHttpClientName),
            provider.GetRequiredService<IOptions<EmporixOptions>>(),
            provider.GetRequiredService<ILogger<DefaultTokenProvider>>(),
            provider.GetService<TimeProvider>()));

        // A singleton: one shared renewal for all requests. Living on the handler
        // it would be per handler generation, and two renewals could overlap.
        services.TryAddSingleton(static provider => new CustomerTokenRefreshCoordinator(
            provider.GetService<ICustomerTokenRefresher>()));

        services.TryAddTransient<EmporixRetryHandler>();
        services.TryAddTransient(static provider => new EmporixAuthenticationHandler(
            provider.GetRequiredService<ITokenProvider>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>(),
            provider.GetRequiredService<ILogger<EmporixAuthenticationHandler>>(),
            provider.GetRequiredService<CustomerTokenRefreshCoordinator>()));

        services.AddHttpClient(ApiHttpClientName, static (provider, client) =>
            {
                EmporixOptions options = provider.GetRequiredService<IOptions<EmporixOptions>>().Value;
                client.BaseAddress = new Uri(options.Host);

                // The overall budget for the request, including reading the body.
                client.Timeout = options.Timeouts.Read;
            })
            .ConfigurePrimaryHttpMessageHandler(static provider =>
            {
                EmporixOptions options = provider.GetRequiredService<IOptions<EmporixOptions>>().Value;
                return new SocketsHttpHandler
                {
                    // The connect budget, separate from the overall one above.
                    ConnectTimeout = options.Timeouts.Connect,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                };
            })
            // Order matters: added first means further out. Retry sits on the
            // outside so a second attempt passes through authentication again
            // and receives a fresh token.
            .AddHttpMessageHandler<EmporixRetryHandler>()
            .AddHttpMessageHandler<EmporixAuthenticationHandler>();

        services.TryAddSingleton(static provider => new EmporixHttpClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(ApiHttpClientName),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        // The services are stateless and share the client — one singleton per
        // service is enough.
        services.TryAddSingleton(static provider => new ProductService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>(),
            provider.GetRequiredService<ILogger<ProductService>>()));

        services.TryAddSingleton(static provider => new CartService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        services.TryAddSingleton(static provider => new CategoryService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        services.TryAddSingleton(static provider => new BrandService(
            provider.GetRequiredService<EmporixHttpClient>()));

        services.TryAddSingleton(static provider => new LabelService(
            provider.GetRequiredService<EmporixHttpClient>()));

        services.TryAddSingleton(static provider => new CatalogService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        services.TryAddSingleton(static provider => new CustomerService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        services.TryAddSingleton(static provider => new PriceService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        services.TryAddSingleton(static provider => new AvailabilityService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        services.TryAddSingleton(static provider => new CheckoutService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        services.TryAddSingleton(static provider => new OrderService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        services.TryAddSingleton(static provider => new SalesOrderService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        services.TryAddSingleton(static provider => new MediaService(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>()));

        // The client bundles the services. Here it uses the parts from the
        // container and therefore releases nothing itself.
        services.TryAddSingleton(static provider => new EmporixClient(
            provider.GetRequiredService<EmporixHttpClient>(),
            provider.GetRequiredService<IOptions<EmporixOptions>>(),
            provider.GetRequiredService<ILoggerFactory>(),
            provider.GetRequiredService<ITokenProvider>()));

        return services;
    }
}
