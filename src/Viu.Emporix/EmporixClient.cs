using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// The entry point to the Emporix API.
/// </summary>
/// <remarks>
/// <para>
/// One instance safely serves many concurrent users: what a call is authorised
/// with lives in the <see cref="AuthContext"/> it receives, never on the client.
/// In an application with dependency injection the client therefore belongs
/// registered as a singleton — <see cref="ServiceCollectionExtensions.AddEmporix"/>
/// does that for you.
/// </para>
/// <para>
/// Outside a container — in a script or command-line tool — the public
/// constructor builds everything itself. A client created that way owns its
/// connections and should be disposed when done.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using EmporixClient client = new(new EmporixOptions
/// {
///     Tenant = "mytenant",
///     Credentials = { Storefront = new EmporixStorefrontCredentials { ClientId = "..." } },
/// });
///
/// await foreach (var product in client.Products.ListAllAsync())
/// {
///     Console.WriteLine(product.Code);
/// }
/// </code>
/// </example>
public sealed class EmporixClient : IDisposable
{
    private readonly EmporixHttpClient _http;
    private readonly IOptions<EmporixOptions> _options;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>What this client built itself and therefore has to release.</summary>
    private readonly IDisposable[] _owned;

    private ProductService? _products;
    private CartService? _carts;
    private CategoryService? _categories;
    private BrandService? _brands;
    private LabelService? _labels;
    private CatalogService? _catalogs;
    private CustomerService? _customers;
    private PriceService? _prices;
    private AvailabilityService? _availability;
    private CheckoutService? _checkout;
    private OrderService? _orders;
    private MediaService? _media;
    private bool _disposed;

    /// <summary>
    /// Creates a standalone client that builds its own connections.
    /// </summary>
    /// <param name="options">Tenant, credentials and fine-tuning.</param>
    /// <param name="loggerFactory">
    /// Where to log. Nothing is logged when omitted.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="OptionsValidationException">The configuration is incomplete or invalid.</exception>
    /// <remarks>
    /// For applications with dependency injection,
    /// <see cref="ServiceCollectionExtensions.AddEmporix"/> is the better route:
    /// there the container manages the connections.
    /// </remarks>
    public EmporixClient(EmporixOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Validate right away. Without a container there is no startup check that
        // would find the mistake earlier.
        ValidateOptionsResult validation = new EmporixOptionsValidator().Validate(name: null, options);

        if (validation.Failed)
        {
            throw new OptionsValidationException(
                nameof(EmporixOptions),
                typeof(EmporixOptions),
                validation.Failures);
        }

        _options = Options.Create(options);
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

        // A separate route for the token endpoints: token requests must not pass
        // through the layers they themselves supply.
        SocketsHttpHandler tokenHandler = new()
        {
            ConnectTimeout = options.Timeouts.Connect,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        HttpClient tokenClient = new(tokenHandler) { Timeout = options.Timeouts.Read };

        DefaultTokenProvider tokenProvider = new(
            tokenClient,
            _options,
            _loggerFactory.CreateLogger<DefaultTokenProvider>());

        // The chain for API calls: retry on the outside, so a second attempt
        // passes through authentication again.
        SocketsHttpHandler apiHandler = new()
        {
            ConnectTimeout = options.Timeouts.Connect,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        EmporixAuthenticationHandler authentication = new(
            tokenProvider,
            _options,
            _loggerFactory.CreateLogger<EmporixAuthenticationHandler>(),
            new CustomerTokenRefreshCoordinator(refresher: null))
        {
            InnerHandler = apiHandler,
        };

        EmporixRetryHandler retry = new(
            _options,
            _loggerFactory.CreateLogger<EmporixRetryHandler>())
        {
            InnerHandler = authentication,
        };

        HttpClient apiClient = new(retry) { Timeout = options.Timeouts.Read };

        _http = new EmporixHttpClient(apiClient, _options);
        TokenProvider = tokenProvider;

        // Disposing apiClient releases the whole chain with it.
        _owned = [apiClient, tokenClient, tokenProvider];
    }

    /// <summary>Creates a client from parts that were already built.</summary>
    internal EmporixClient(
        EmporixHttpClient http,
        IOptions<EmporixOptions> options,
        ILoggerFactory loggerFactory,
        ITokenProvider tokenProvider)
    {
        _http = http;
        _options = options;
        _loggerFactory = loggerFactory;
        TokenProvider = tokenProvider;

        // Everything came from outside and is released there too.
        _owned = [];
    }

    /// <summary>The tenant this client works against.</summary>
    public string Tenant => _options.Value.Tenant;

    /// <summary>
    /// This client's token supply.
    /// </summary>
    /// <remarks>
    /// Exposed so an anonymous session can be discarded deliberately — after a
    /// currency change, for instance, which needs a freshly bound token.
    /// </remarks>
    public ITokenProvider TokenProvider { get; }

    /// <summary>The product catalog.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public ProductService Products
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Created on first use and without a lock: the services are
            // stateless, so two instances created concurrently cost one
            // allocation and behave identically.
            return _products ??= new ProductService(
                _http,
                _options,
                _loggerFactory.CreateLogger<ProductService>());
        }
    }

    /// <summary>Shopping carts.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public CartService Carts
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _carts ??= new CartService(_http, _options);
        }
    }

    /// <summary>The category tree.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public CategoryService Categories
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _categories ??= new CategoryService(_http, _options);
        }
    }

    /// <summary>Product brands.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public BrandService Brands
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _brands ??= new BrandService(_http);
        }
    }

    /// <summary>Product labels.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public LabelService Labels
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _labels ??= new LabelService(_http);
        }
    }

    /// <summary>Product catalogs.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public CatalogService Catalogs
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _catalogs ??= new CatalogService(_http, _options);
        }
    }

    /// <summary>Customer accounts and sessions.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public CustomerService Customers
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _customers ??= new CustomerService(_http, _options);
        }
    }

    /// <summary>Prices, including context-aware price matching.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public PriceService Prices
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _prices ??= new PriceService(_http, _options);
        }
    }

    /// <summary>Product availability per site.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public AvailabilityService Availability
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _availability ??= new AvailabilityService(_http, _options);
        }
    }

    /// <summary>Turning a cart into an order.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public CheckoutService Checkout
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _checkout ??= new CheckoutService(_http, _options);
        }
    }

    /// <summary>Customer orders.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public OrderService Orders
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _orders ??= new OrderService(_http, _options);
        }
    }

    /// <summary>Media assets.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public MediaService Media
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _media ??= new MediaService(_http, _options);
        }
    }

    /// <summary>
    /// Releases the connections this client built itself.
    /// </summary>
    /// <remarks>
    /// When the client came from a container it owns nothing and this call does
    /// nothing.
    /// </remarks>
    public void Dispose()
    {
        // A client without connections of its own stays usable. In a container
        // it is a singleton: if a single call could shut it down, it would be
        // unavailable to everyone else.
        if (_owned.Length == 0 || _disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IDisposable owned in _owned)
        {
            owned.Dispose();
        }
    }
}
