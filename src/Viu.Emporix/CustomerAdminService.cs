using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.CustomerServiceModels;

namespace Viu.Emporix;

/// <summary>
/// Customers, as a seller manages them.
/// </summary>
/// <remarks>
/// <para>
/// The other side of <see cref="CustomerService"/>: that one is a person acting
/// for themselves, this one is a back office acting on their behalf. The
/// distinction is not cosmetic — a customer is addressed by their number here,
/// never by «me», and every call needs a service token.
/// </para>
/// <para>
/// Addresses appear in both, and they are the same addresses. Which service to
/// use depends on who is asking, not on what is being changed.
/// </para>
/// </remarks>
public sealed class CustomerAdminService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CustomerAdminService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/customer/{_tenant}/customers";

    /// <summary>One customer's addresses.</summary>
    /// <param name="customerNumber">The customer.</param>
    public CustomerAdminAddressOperations AddressesOf(string customerNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);
        return new CustomerAdminAddressOperations(
            _http,
            $"{BasePath}/{Uri.EscapeDataString(customerNumber)}/addresses");
    }

    /// <summary>Fetches a customer.</summary>
    /// <param name="customerNumber">The customer number.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CustomerForSellerDto?> GetAsync(
        string customerNumber,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(customerNumber)}",
                Auth = Defaults.Service(auth),
            },
            CustomerAdminJsonContext.Default.CustomerForSellerDto,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of customers.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<CustomerForSellerDto>> ListAsync(
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = Paging(pageNumber, pageSize),
            },
            CustomerAdminJsonContext.Default.ListCustomerForSellerDto,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches customers.</summary>
    /// <param name="query">The Emporix query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A <c>POST</c> because the query does not fit in an address, but declared
    /// repeatable: it only reads.
    /// </remarks>
    public async Task<PaginatedItems<CustomerForSellerDto>> SearchAsync(
        string query,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = Paging(pageNumber, pageSize),
                // The specification declares this body inline, so the generator
                // named it «Body». Callers never see it — the query is a string.
                Content = EmporixJsonContent.Create(
                    new Body { Q = query },
                    CustomerAdminJsonContext.Default.Body),
                Idempotent = true,
            },
            CustomerAdminJsonContext.Default.ListCustomerForSellerDto,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a customer on their behalf.</summary>
    /// <param name="customer">The customer to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Unlike a self-service sign-up this needs no password: the seller creates
    /// the account and the customer sets one later.
    /// </remarks>
    public async Task<ResourceLocation?> CreateAsync(
        CustomerSignupBySellerDto customer,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    customer,
                    CustomerAdminJsonContext.Default.CustomerSignupBySellerDto),
            },
            CustomerAdminJsonContext.Default.ResourceLocation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a customer.</summary>
    /// <param name="customerNumber">The customer number.</param>
    /// <param name="customer">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string customerNumber,
        CustomerUpdateBySellerDto customer,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);
        ArgumentNullException.ThrowIfNull(customer);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(customerNumber)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    customer,
                    CustomerAdminJsonContext.Default.CustomerUpdateBySellerDto),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of a customer.</summary>
    /// <param name="customerNumber">The customer number.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string customerNumber,
        CustomerPatchBySellerDto changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(customerNumber)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    changes,
                    CustomerAdminJsonContext.Default.CustomerPatchBySellerDto),
            },
            cancellationToken);
    }

    /// <summary>Deletes a customer.</summary>
    /// <param name="customerNumber">The customer number.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Their orders remain and keep pointing at a customer who no longer
    /// resolves. For a deletion request under data-protection law this is the
    /// call, but the orders are a separate question.
    /// </remarks>
    public Task DeleteAsync(
        string customerNumber,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(customerNumber)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Imports customers in bulk.</summary>
    /// <param name="customers">The customers to import.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// For a migration from another system. Carries password hashes, which is
    /// why it is separate from ordinary creation — and why it is not repeatable.
    /// </remarks>
    public Task ImportAsync(
        IEnumerable<CustomerImportDto> customers,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customers);

        List<CustomerImportDto> body = [.. customers];

        return body.Count == 0
            ? Task.CompletedTask
            : _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Post,
                    Path = $"{BasePath}/import",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        CustomerAdminJsonContext.Default.ListCustomerImportDto),
                },
                cancellationToken);
    }

    /// <summary>Reads the password-migration retention configuration.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// How long imported password hashes stay usable before the customer must
    /// reset. Only relevant while a migration is running.
    /// </remarks>
    public async Task<PasswordMigrationRetentionConfigResponse?> GetPasswordMigrationConfigAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/customer/{_tenant}/config/password-migration-retention",
                Auth = Defaults.Service(auth),
            },
            CustomerAdminJsonContext.Default.PasswordMigrationRetentionConfigResponse,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Sets the password-migration retention configuration.</summary>
    /// <param name="configuration">The retention window and its reminders.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PasswordMigrationRetentionConfigResponse?> SetPasswordMigrationConfigAsync(
        PasswordMigrationRetentionConfigRequest configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/customer/{_tenant}/config/password-migration-retention",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration,
                    CustomerAdminJsonContext.Default.PasswordMigrationRetentionConfigRequest),
            },
            CustomerAdminJsonContext.Default.PasswordMigrationRetentionConfigResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ends the password-migration retention window.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Imported hashes stop working, so every customer who has not signed in
    /// since the migration has to reset their password.
    /// </remarks>
    public Task DeletePasswordMigrationConfigAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"/customer/{_tenant}/config/password-migration-retention",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
    ];
}

/// <summary>
/// One customer's addresses, as a seller manages them.
/// </summary>
/// <remarks>
/// The same addresses <see cref="CustomerAddressOperations"/> exposes to the
/// customer themselves, reached through
/// <see cref="CustomerAdminService.AddressesOf"/>.
/// </remarks>
public sealed class CustomerAdminAddressOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal CustomerAdminAddressOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Lists the addresses.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<Address>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = _basePath,
                Auth = Defaults.Service(auth),
            },
            CustomerAdminJsonContext.Default.ListAddress,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Address?> GetAsync(
        string addressId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/{Uri.EscapeDataString(addressId)}",
                Auth = Defaults.Service(auth),
            },
            CustomerAdminJsonContext.Default.Address,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds an address.</summary>
    /// <param name="address">The address to add.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceLocation?> CreateAsync(
        Address_2 address,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    address,
                    CustomerAdminJsonContext.Default.Address_2),
            },
            CustomerAdminJsonContext.Default.ResourceLocation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces an address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="address">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Unlike the customer's own view, the seller endpoint does route <c>PUT</c>
    /// as well as <c>PATCH</c>. The two services are not symmetric here.
    /// </remarks>
    public Task ReplaceAsync(
        string addressId,
        AddressUpdateDto address,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);
        ArgumentNullException.ThrowIfNull(address);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/{Uri.EscapeDataString(addressId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    address,
                    CustomerAdminJsonContext.Default.AddressUpdateDto),
            },
            cancellationToken);
    }

    /// <summary>Changes individual fields of an address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string addressId,
        AddressUpdateDto changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{_basePath}/{Uri.EscapeDataString(addressId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    changes,
                    CustomerAdminJsonContext.Default.AddressUpdateDto),
            },
            cancellationToken);
    }

    /// <summary>Deletes an address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string addressId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/{Uri.EscapeDataString(addressId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Tags an address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="tags">The tags to add.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task AddTagsAsync(
        string addressId,
        IEnumerable<string> tags,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ChangeTagsAsync(HttpMethod.Post, addressId, tags, auth, cancellationToken);

    /// <summary>Removes tags from an address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="tags">The tags to remove.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RemoveTagsAsync(
        string addressId,
        IEnumerable<string> tags,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ChangeTagsAsync(HttpMethod.Delete, addressId, tags, auth, cancellationToken);

    private Task ChangeTagsAsync(
        HttpMethod method,
        string addressId,
        IEnumerable<string> tags,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);
        ArgumentNullException.ThrowIfNull(tags);

        string joined = string.Join(',', tags);
        ArgumentException.ThrowIfNullOrWhiteSpace(joined, nameof(tags));

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = method,
                Path = $"{_basePath}/{Uri.EscapeDataString(addressId)}/tags",
                Auth = Defaults.Service(auth),
                Query = [new("tags", joined)],
            },
            cancellationToken);
    }
}
