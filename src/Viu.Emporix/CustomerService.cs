using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Viu.Emporix.CustomerModels;

namespace Viu.Emporix;

/// <summary>
/// An authenticated customer session.
/// </summary>
/// <remarks>
/// <see cref="CustomerToken"/> is what you pass to
/// <see cref="AuthContext.Customer(string)"/> on subsequent calls. The SDK does
/// not store it — where it lives is the calling application's decision.
/// </remarks>
public sealed class CustomerSession
{
    internal CustomerSession(
        string customerToken,
        string saasToken,
        string refreshToken,
        string? sessionId,
        int? expiresIn)
    {
        CustomerToken = customerToken;
        SaasToken = saasToken;
        RefreshToken = refreshToken;
        SessionId = sessionId;
        ExpiresIn = expiresIn;
    }

    /// <summary>The bearer token for this customer's calls.</summary>
    public string CustomerToken { get; }

    /// <summary>
    /// A second token some checkout endpoints require as a <c>saas-token</c>
    /// header. Empty when the response carried none.
    /// </summary>
    /// <remarks>
    /// Renewal does not restore it — the refresh response omits it. Sign in
    /// again when you need it back.
    /// </remarks>
    public string SaasToken { get; }

    /// <summary>The token used to renew this session. Empty when none was issued.</summary>
    public string RefreshToken { get; }

    /// <summary>The session this login belongs to, where Emporix supplies one.</summary>
    public string? SessionId { get; }

    /// <summary>How many seconds the token remains valid, where stated.</summary>
    public int? ExpiresIn { get; }

    /// <summary>Returns a description that contains no tokens.</summary>
    public override string ToString()
        => $"CustomerSession {{ SessionId = {SessionId}, ExpiresIn = {ExpiresIn} }}";
}

/// <summary>Credentials for signing a customer in.</summary>
public sealed class CustomerLogin
{
    /// <summary>The email address.</summary>
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    /// <summary>The password.</summary>
    [JsonPropertyName("password")]
    public required string Password { get; init; }
}

/// <summary>The body sent when renewing a session.</summary>
internal sealed class RefreshSessionBody
{
    [JsonPropertyName("refreshToken")]
    public required string RefreshToken { get; init; }
}

/// <summary>
/// Customer accounts and sessions.
/// </summary>
/// <remarks>
/// Signing up and signing in run against an anonymous token. Everything that
/// concerns the signed-in person requires that person's own token — the SDK
/// never stores it, so it is passed per call.
/// </remarks>
public sealed class CustomerService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CustomerService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
        Addresses = new CustomerAddressOperations(http, _tenant);
    }

    private string BasePath => $"/customer/{_tenant}";

    /// <summary>The signed-in customer's addresses.</summary>
    public CustomerAddressOperations Addresses { get; }

    /// <summary>
    /// Signs a customer in.
    /// </summary>
    /// <param name="credentials">Email and password.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixAuthenticationException">
    /// The credentials were rejected, or the response carried no token.
    /// </exception>
    /// <remarks>
    /// Passing the anonymous token the visitor already holds lets Emporix carry
    /// their guest cart into the new session.
    /// </remarks>
    public async Task<CustomerSession> LoginAsync(
        CustomerLogin credentials,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        return await PostSessionAsync(
            $"{BasePath}/login",
            EmporixJsonContent.Create(credentials, CustomerJsonContext.Default.CustomerLogin),
            Defaults.Anonymous(auth),
            "login",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renews a session with its refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token from a previous session.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The renewed session carries no <see cref="CustomerSession.SaasToken"/> —
    /// Emporix omits it here. Sign in again when you need it.
    /// </remarks>
    public async Task<CustomerSession> RefreshSessionAsync(
        string refreshToken,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        return await PostSessionAsync(
            $"{BasePath}/refreshauthtoken",
            EmporixJsonContent.Create(
                new RefreshSessionBody { RefreshToken = refreshToken },
                CustomerJsonContext.Default.RefreshSessionBody),
            Defaults.Anonymous(auth),
            "session refresh",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Signs the customer out, invalidating their token at Emporix.
    /// </summary>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task LogoutAsync(AuthContext auth, CancellationToken cancellationToken = default)
        => _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/logout",
                Auth = RequireCustomer(auth),
            },
            cancellationToken);

    /// <summary>Registers a new customer.</summary>
    /// <param name="customer">The account to create.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">
    /// Emporix rejected the account — most often because the email is already taken.
    /// </exception>
    public async Task<Customer?> SignUpAsync(
        Customer customer,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/signup",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(customer, CustomerJsonContext.Default.Customer),
            },
            CustomerJsonContext.Default.Customer,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches the signed-in customer's own profile.</summary>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Customer?> GetMeAsync(
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/me",
                Auth = RequireCustomer(auth),
            },
            CustomerJsonContext.Default.Customer,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Updates the signed-in customer's own profile.</summary>
    /// <param name="customer">The new state.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Customer?> UpdateMeAsync(
        Customer customer,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return await _http.SendAsync(
            new EmporixRequest
            {
                // PATCH, not PUT: the endpoint has no full replace, and a PUT
                // is simply not routed.
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/me",
                Auth = RequireCustomer(auth),
                Content = EmporixJsonContent.Create(customer, CustomerJsonContext.Default.Customer),
            },
            CustomerJsonContext.Default.Customer,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a password reset by sending the customer an email.
    /// </summary>
    /// <param name="email">The account's email address.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Emporix answers the same way whether or not the address exists, so this
    /// cannot be used to probe for accounts.
    /// </remarks>
    public Task RequestPasswordResetAsync(
        string email,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/password/reset",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    new PasswordResetRequest { Email = email },
                    CustomerJsonContext.Default.PasswordResetRequest),
            },
            cancellationToken);
    }


    /// <summary>Signs in through a social identity provider.</summary>
    /// <param name="providerToken">The token the provider issued.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The token comes from the provider's own flow, which the SDK does not
    /// drive — it only exchanges the result for an Emporix session.
    /// </remarks>
    public Task<CustomerSession> SocialLoginAsync(
        string providerToken,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerToken);

        return PostSessionAsync(
            $"{BasePath}/socialLogin",
            EmporixJsonContent.Create(
                new SocialLoginRequest { Token = providerToken },
                CustomerJsonContext.Default.SocialLoginRequest),
            Defaults.Anonymous(auth),
            "social login",
            cancellationToken);
    }

    /// <summary>Exchanges a token issued elsewhere for an Emporix session.</summary>
    /// <param name="subjectAccessToken">The token to exchange.</param>
    /// <param name="config">The exchange configuration to apply.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<CustomerSession> ExchangeTokenAsync(
        string subjectAccessToken,
        string? config = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectAccessToken);

        return PostSessionAsync(
            $"{BasePath}/exchangeauthtoken",
            content: null,
            Defaults.Anonymous(auth),
            "token exchange",
            cancellationToken,
            query:
            [
                new("subjectAccessToken", subjectAccessToken),
                new("config", config),
            ]);
    }

    /// <summary>Checks whether a customer token is still good.</summary>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the token is good for, or <see langword="null"/> for an empty answer.</returns>
    /// <exception cref="EmporixAuthenticationException">The token is not valid.</exception>
    /// <remarks>
    /// A rejected token surfaces as an exception, not as a <see langword="false"/> —
    /// the distinction between «expired» and «never valid» is in the failure.
    /// </remarks>
    public async Task<CustomerModels.ValidateTokenResponse?> ValidateTokenAsync(
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/validateauthtoken",
                Auth = auth,
            },
            CustomerJsonContext.Default.ValidateTokenResponse,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Changes the signed-in customer's password.</summary>
    /// <param name="currentPassword">The password in force.</param>
    /// <param name="newPassword">The replacement.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">
    /// The current password is wrong, or the new one fails the tenant's rules.
    /// </exception>
    /// <remarks>
    /// Emporix may invalidate existing sessions on success, so treat the
    /// caller's token as spent and sign in again.
    /// </remarks>
    public Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/password/change",
                Auth = auth,
                Content = EmporixJsonContent.Create(
                    new CustomerModels.PasswordChangeDto
                    {
                        CurrentPassword = currentPassword,
                        NewPassword = newPassword,
                    },
                    CustomerJsonContext.Default.PasswordChangeDto),
            },
            cancellationToken);
    }

    /// <summary>Completes a password reset.</summary>
    /// <param name="token">The token from the reset mail.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">The token has expired or the password is rejected.</exception>
    public Task ConfirmPasswordResetAsync(
        string token,
        string newPassword,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/password/reset/update",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    new CustomerModels.PasswordUpdate { Token = token, Password = newPassword },
                    CustomerJsonContext.Default.PasswordUpdate),
            },
            cancellationToken);
    }

    /// <summary>Confirms a sign-up from the activation mail.</summary>
    /// <param name="token">The token from the mail.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ConfirmSignUpAsync(
        string token,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/signup/optin/{Uri.EscapeDataString(token)}",
                Auth = Defaults.Anonymous(auth),
            },
            cancellationToken);
    }

    /// <summary>Sends the activation mail again.</summary>
    /// <param name="email">The address the sign-up was made with.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Identified by the address rather than by a token: the point is that the
    /// person never received the first mail and has no token to offer.
    /// </remarks>
    public Task ResendActivationAsync(
        string email,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/signup/optin/refresh_token",
                Auth = Defaults.Anonymous(auth),
                Content = EmporixJsonContent.Create(
                    new CustomerModels.RefreshToken { Email = email },
                    CustomerJsonContext.Default.RefreshToken),
            },
            cancellationToken);
    }

    /// <summary>Starts changing the signed-in customer's email address.</summary>
    /// <param name="request">The current address, the password and the new address.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The address does not change here: Emporix sends a confirmation mail, and
    /// <see cref="ConfirmEmailChangeAsync"/> completes it.
    /// </remarks>
    public Task ChangeEmailAsync(
        CustomerModels.ChangeEmailRequestDto request,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/me/accounts/internal/email/change",
                Auth = auth,
                Content = EmporixJsonContent.Create(
                    request,
                    CustomerJsonContext.Default.ChangeEmailRequestDto),
            },
            cancellationToken);
    }

    /// <summary>Completes an email change.</summary>
    /// <param name="token">The token from the confirmation mail.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The sign-in address changes here, so a session minted against the old
    /// address may no longer resolve.
    /// </remarks>
    public Task ConfirmEmailChangeAsync(
        string token,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/me/accounts/internal/email/change/confirm",
                Auth = auth,
                Content = EmporixJsonContent.Create(
                    new CustomerModels.UpdateEmail { Token = token },
                    CustomerJsonContext.Default.UpdateEmail),
            },
            cancellationToken);
    }

    /// <summary>
    /// Reads a session out of a login response.
    /// </summary>
    /// <remarks>
    /// Emporix returns these fields in two spellings — <c>access_token</c> on
    /// some endpoints, <c>accessToken</c> on others — so both are read. Parsing
    /// goes through <see cref="JsonDocument"/> rather than a model: a model
    /// would need every field twice, and this way an unexpected response shape
    /// cannot throw where a clear authentication error belongs.
    /// </remarks>
    private async Task<CustomerSession> PostSessionAsync(
        string path,
        HttpContent? content,
        AuthContext auth,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null)
    {
        string body = await _http.SendForBodyAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = path,
                Auth = auth,
                Content = content,
                Query = query,
            },
            cancellationToken).ConfigureAwait(false);

        return ParseSession(body, operation);
    }

    internal static CustomerSession ParseSession(string? body, string operation)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new EmporixAuthenticationException($"The {operation} response was empty.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new EmporixAuthenticationException(
                    $"The {operation} response was not an object.",
                    rawBody: body);
            }

            string? token = ReadString(root, "access_token") ?? ReadString(root, "accessToken");

            if (string.IsNullOrEmpty(token))
            {
                throw new EmporixAuthenticationException(
                    $"The {operation} response contained no access token.",
                    rawBody: body);
            }

            return new CustomerSession(
                token,
                ReadString(root, "saas_token") ?? ReadString(root, "saasToken") ?? string.Empty,
                ReadString(root, "refresh_token") ?? ReadString(root, "refreshToken") ?? string.Empty,
                ReadString(root, "session_id") ?? ReadString(root, "sessionId"),
                ReadExpiresIn(root));
        }
        catch (JsonException)
        {
            throw new EmporixAuthenticationException(
                $"The {operation} response could not be read.",
                rawBody: body);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static int? ReadExpiresIn(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out JsonElement value)
            && !root.TryGetProperty("expiresIn", out value))
        {
            return null;
        }

        // Depending on the endpoint this arrives as a number or a string.
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// Ensures the call carries the customer's own token.
    /// </summary>
    /// <remarks>
    /// A raw token is accepted too: it is how an externally issued customer
    /// token reaches the SDK.
    /// </remarks>
    private static AuthContext RequireCustomer(AuthContext auth)
        => auth.Kind is AuthKind.Customer or AuthKind.Raw
            ? auth
            : throw new EmporixConfigurationException(
                "This call acts on behalf of a signed-in customer and requires that "
                + "customer's own token.");
}

/// <summary>The body of a password reset request.</summary>
internal sealed class PasswordResetRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; init; }
}

/// <summary>The signed-in customer's addresses.</summary>
public sealed class CustomerAddressOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CustomerAddressOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/customer/{_tenant}/me/addresses";

    /// <summary>Lists the customer's addresses.</summary>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AddressDto>> ListAsync(
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = RequireCustomer(auth),
            },
            CustomerJsonContext.Default.ListAddressDto,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Adds an address.</summary>
    /// <param name="address">The address to add.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<AddressDto?> CreateAsync(
        AddressCreateDto address,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = RequireCustomer(auth),
                Content = EmporixJsonContent.Create(
                    address,
                    CustomerJsonContext.Default.AddressCreateDto),
            },
            CustomerJsonContext.Default.AddressDto,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces an address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="address">The new state.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string addressId,
        AddressUpdateDto address,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);
        ArgumentNullException.ThrowIfNull(address);

        return _http.SendAsync(
            new EmporixRequest
            {
                // PATCH, as with the profile: there is no full replace here.
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(addressId)}",
                Auth = RequireCustomer(auth),
                Content = EmporixJsonContent.Create(
                    address,
                    CustomerJsonContext.Default.AddressUpdateDto),
            },
            cancellationToken);
    }

    /// <summary>Deletes an address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string addressId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(addressId)}",
                Auth = RequireCustomer(auth),
            },
            cancellationToken);
    }

    private static AuthContext RequireCustomer(AuthContext auth)
        => auth.Kind is AuthKind.Customer or AuthKind.Raw
            ? auth
            : throw new EmporixConfigurationException(
                "Addresses belong to a signed-in customer and require that customer's own token.");

    /// <summary>Fetches one of the customer's addresses.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<AddressDto?> GetAsync(
        string addressId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addressId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(addressId)}",
                Auth = auth,
            },
            CustomerJsonContext.Default.AddressDto,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tags an address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="tags">The tags to add, for example <c>BILLING</c> or <c>SHIPPING</c>.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Tags travel as one comma-separated query parameter, which is how the
    /// endpoint is specified — a tag containing a comma cannot be expressed.
    /// </remarks>
    public Task AddTagsAsync(
        string addressId,
        IEnumerable<string> tags,
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => ChangeTagsAsync(HttpMethod.Post, addressId, tags, auth, cancellationToken);

    /// <summary>Removes tags from an address.</summary>
    /// <param name="addressId">The address id.</param>
    /// <param name="tags">The tags to remove.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RemoveTagsAsync(
        string addressId,
        IEnumerable<string> tags,
        AuthContext auth,
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
                Path = $"{BasePath}/{Uri.EscapeDataString(addressId)}/tags",
                Auth = auth,
                Query = [new("tags", joined)],
            },
            cancellationToken);
    }
}
