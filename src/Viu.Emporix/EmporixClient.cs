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
    private SalesOrderService? _salesOrders;
    private MediaService? _media;
    private TaxService? _tax;
    private FeeService? _fee;
    private CouponService? _coupon;
    private PaymentService? _payment;
    private ShippingService? _shipping;
    private ReturnService? _return;
    private InvoiceService? _invoice;
    private LegalEntityService? _legalEntity;
    private ContactAssignmentService? _contactAssignment;
    private LocationService? _location;
    private CustomerAdminService? _customerAdmin;
    private ApprovalService? _approval;
    private QuoteService? _quote;
    private SegmentService? _segment;
    private IamService? _iam;
    private SchemaService? _schema;
    private SiteService? _site;
    private VendorService? _vendor;
    private CurrencyService? _currency;
    private CountryService? _country;
    private WebhookService? _webhook;
    private UnitService? _unit;
    private SequentialIdService? _sequentialId;
    private ConfigurationService? _configuration;
    private SessionContextService? _sessionContext;
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

    /// <summary>A customer's own orders.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public OrderService Orders
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _orders ??= new OrderService(_http, _options);
        }
    }

    /// <summary>The administrative order collection.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public SalesOrderService SalesOrders
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _salesOrders ??= new SalesOrderService(_http, _options);
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

    /// <summary>Tax configuration and calculation.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public TaxService Taxes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _tax ??= new TaxService(_http, _options);
        }
    }

    /// <summary>Fees, and what they are attached to.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public FeeService Fees
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _fee ??= new FeeService(_http, _options);
        }
    }

    /// <summary>Coupons, redemptions and referral coupons.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public CouponService Coupons
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _coupon ??= new CouponService(_http, _options);
        }
    }

    /// <summary>Payment methods, and moving money.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public PaymentService Payments
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _payment ??= new PaymentService(_http, _options);
        }
    }

    /// <summary>Zones, methods, quotes and delivery times.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public ShippingService Shipping
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _shipping ??= new ShippingService(_http, _options);
        }
    }

    /// <summary>Returns against an order.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public ReturnService Returns
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _return ??= new ReturnService(_http, _options);
        }
    }

    /// <summary>Invoice generation, as a background job.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public InvoiceService Invoices
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _invoice ??= new InvoiceService(_http, _options);
        }
    }

    /// <summary>The companies a B2B tenant sells to.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public LegalEntityService LegalEntities
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _legalEntity ??= new LegalEntityService(_http, _options);
        }
    }

    /// <summary>Who may act for which legal entity.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public ContactAssignmentService ContactAssignments
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _contactAssignment ??= new ContactAssignmentService(_http, _options);
        }
    }

    /// <summary>Where a legal entity receives.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public LocationService Locations
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _location ??= new LocationService(_http, _options);
        }
    }

    /// <summary>Customers, as a seller manages them.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public CustomerAdminService CustomerAdmin
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _customerAdmin ??= new CustomerAdminService(_http, _options);
        }
    }

    /// <summary>Carts and quotes waiting for a decision.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public ApprovalService Approvals
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _approval ??= new ApprovalService(_http, _options);
        }
    }

    /// <summary>Negotiated prices, before they become orders.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public QuoteService Quotes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _quote ??= new QuoteService(_http, _options);
        }
    }

    /// <summary>Who gets which prices and which catalogue.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public SegmentService Segments
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _segment ??= new SegmentService(_http, _options);
        }
    }

    /// <summary>Identity and access — users, groups, and what they may do.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public IamService Iam
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _iam ??= new IamService(_http, _options);
        }
    }

    /// <summary>Schemas and custom entities — a tenant's own data shapes.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public SchemaService Schemas
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _schema ??= new SchemaService(_http, _options);
        }
    }

    /// <summary>The storefronts a tenant runs.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public SiteService Sites
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _site ??= new SiteService(_http, _options);
        }
    }

    /// <summary>Who sells, in a marketplace.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public VendorService Vendors
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _vendor ??= new VendorService(_http, _options);
        }
    }

    /// <summary>Currencies and the rates between them.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public CurrencyService Currencies
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _currency ??= new CurrencyService(_http, _options);
        }
    }

    /// <summary>Countries and regions.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public CountryService Countries
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _country ??= new CountryService(_http, _options);
        }
    }

    /// <summary>Emporix calling out when something happens.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public WebhookService Webhooks
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _webhook ??= new WebhookService(_http, _options);
        }
    }

    /// <summary>Units of measure, and converting between them.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public UnitService Units
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _unit ??= new UnitService(_http, _options);
        }
    }

    /// <summary>Order numbers and the like.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public SequentialIdService SequentialIds
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _sequentialId ??= new SequentialIdService(_http, _options);
        }
    }

    /// <summary>Tenant and client configuration.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public ConfigurationService Configuration
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _configuration ??= new ConfigurationService(_http, _options);
        }
    }

    /// <summary>What a session carries beyond its token.</summary>
    /// <exception cref="ObjectDisposedException">The client has already been disposed.</exception>
    public SessionContextService SessionContext
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _sessionContext ??= new SessionContextService(_http, _options);
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
