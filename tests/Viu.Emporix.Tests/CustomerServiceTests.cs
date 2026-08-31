using System.Net;
using Microsoft.Extensions.Options;
using Viu.Emporix.CustomerModels;

namespace Viu.Emporix.Tests;

public class CustomerServiceTests
{
    private static readonly AuthContext SignedIn = AuthContext.Customer("customer-token");

    private static CustomerService Create(StubHttpMessageHandler handler)
    {
        IOptions<EmporixOptions> options = Options.Create(new EmporixOptions { Tenant = "acme" });

        return new CustomerService(new EmporixHttpClient(new HttpClient(handler), options), options);
    }

    private static string Uri(StubHttpMessageHandler handler, int index = 0)
        => handler.RequestUris[index].PathAndQuery;

    private static CustomerLogin Credentials()
        => new() { Email = "a@b.co", Password = "secret" };

    // ---------- Reading a session ----------

    [Fact]
    public void A_session_is_read_from_snake_case_fields()
    {
        CustomerSession session = CustomerService.ParseSession(
            """
            {"access_token":"t","saas_token":"s","refresh_token":"r","session_id":"sess","expires_in":3600}
            """,
            "login");

        Assert.Equal("t", session.CustomerToken);
        Assert.Equal("s", session.SaasToken);
        Assert.Equal("r", session.RefreshToken);
        Assert.Equal("sess", session.SessionId);
        Assert.Equal(3600, session.ExpiresIn);
    }

    [Fact]
    public void A_session_is_read_from_camel_case_fields_too()
    {
        // Emporix uses both spellings depending on the endpoint. Reading only one
        // would leave the token silently empty on the other half of the API.
        CustomerSession session = CustomerService.ParseSession(
            """{"accessToken":"t","saasToken":"s","refreshToken":"r","sessionId":"sess"}""",
            "login");

        Assert.Equal("t", session.CustomerToken);
        Assert.Equal("s", session.SaasToken);
        Assert.Equal("r", session.RefreshToken);
        Assert.Equal("sess", session.SessionId);
    }

    [Fact]
    public void An_expiry_given_as_a_string_is_read()
    {
        CustomerSession session = CustomerService.ParseSession(
            """{"access_token":"t","expires_in":"1800"}""",
            "login");

        Assert.Equal(1800, session.ExpiresIn);
    }

    [Fact]
    public void Absent_optional_fields_become_empty_rather_than_null()
    {
        CustomerSession session = CustomerService.ParseSession("""{"access_token":"t"}""", "login");

        Assert.Equal(string.Empty, session.SaasToken);
        Assert.Equal(string.Empty, session.RefreshToken);
        Assert.Null(session.SessionId);
        Assert.Null(session.ExpiresIn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("""{"refresh_token":"r"}""")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    public void A_response_without_a_usable_token_is_an_authentication_error(string body)
    {
        // Whatever the response looks like, the caller gets an authentication
        // error rather than a serialization failure.
        Assert.Throws<EmporixAuthenticationException>(
            () => CustomerService.ParseSession(body, "login"));
    }

    [Fact]
    public void A_session_never_prints_its_tokens()
    {
        CustomerSession session = CustomerService.ParseSession(
            """{"access_token":"secret-token","saas_token":"secret-saas","session_id":"sess"}""",
            "login");

        string text = session.ToString();

        Assert.DoesNotContain("secret-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-saas", text, StringComparison.Ordinal);
        Assert.Contains("sess", text, StringComparison.Ordinal);
    }

    // ---------- Signing in ----------

    [Fact]
    public async Task Login_posts_the_credentials_anonymously()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"access_token":"t"}""");
        CustomerService customers = Create(handler);

        CustomerSession session = await customers.LoginAsync(Credentials());

        Assert.Equal("t", session.CustomerToken);
        Assert.Equal("/customer/acme/login", Uri(handler));
        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Anonymous, auth.Kind);
    }

    [Fact]
    public async Task Login_can_carry_the_visitors_anonymous_token()
    {
        // That is how Emporix moves a guest cart into the new session.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"access_token":"t"}""");
        CustomerService customers = Create(handler);

        await customers.LoginAsync(Credentials(), AuthContext.Raw("guest-token"));

        // The stub sits below the authentication handler, so the header is not
        // set here — what matters is that the context reaches the chain intact.
        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Raw, auth.Kind);
        Assert.Equal("guest-token", auth.Token);
    }

    [Fact]
    public async Task Rejected_credentials_surface_as_an_authentication_error()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.Unauthorized,
            """{"message":"Bad credentials"}""");
        CustomerService customers = Create(handler);

        await Assert.ThrowsAsync<EmporixAuthenticationException>(async () =>
            await customers.LoginAsync(Credentials()));
    }

    [Fact]
    public async Task Renewal_sends_the_refresh_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"access_token":"t2"}""");
        CustomerService customers = Create(handler);

        CustomerSession session = await customers.RefreshSessionAsync("r1");

        Assert.Equal("t2", session.CustomerToken);
        Assert.Equal("/customer/acme/refreshauthtoken", Uri(handler));
        Assert.Contains("r1", handler.RequestBodies[0], StringComparison.Ordinal);
        // Emporix omits the saas token on renewal.
        Assert.Equal(string.Empty, session.SaasToken);
    }

    // ---------- Acting for the signed-in customer ----------

    [Fact]
    public async Task The_own_profile_requires_the_customers_own_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CustomerService customers = Create(handler);

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await customers.GetMeAsync(AuthContext.Service()));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task A_raw_token_is_accepted_as_a_customer_token()
    {
        // That is how an externally issued customer token reaches the SDK.
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CustomerService customers = Create(handler);

        Customer? me = await customers.GetMeAsync(AuthContext.Raw("external"));

        Assert.Equal("c1", me?.Id);
    }

    [Fact]
    public async Task Signup_runs_anonymously()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.Created, """{"id":"c1"}""");
        CustomerService customers = Create(handler);

        await customers.SignUpAsync(new Customer());

        Assert.Equal("/customer/acme/signup", Uri(handler));
        handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Auth, out AuthContext auth);
        Assert.Equal(AuthKind.Anonymous, auth.Kind);
    }

    [Fact]
    public async Task A_password_reset_is_requested_anonymously()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        CustomerService customers = Create(handler);

        await customers.RequestPasswordResetAsync("a@b.co");

        Assert.Equal("/customer/acme/password/reset", Uri(handler));
        Assert.Contains("a@b.co", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    // ---------- Addresses ----------

    [Fact]
    public async Task Addresses_hang_off_the_signed_in_customer()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """[{"id":"a1"}]""");
        CustomerService customers = Create(handler);

        IReadOnlyList<AddressDto> addresses = await customers.Addresses.ListAsync(SignedIn);

        Assert.Single(addresses);
        Assert.Equal("/customer/acme/me/addresses", Uri(handler));
    }

    [Fact]
    public async Task Addresses_refuse_a_service_token()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        CustomerService customers = Create(handler);

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await customers.Addresses.ListAsync(AuthContext.Service()));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task An_address_is_addressed_by_id()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        CustomerService customers = Create(handler);

        await customers.Addresses.DeleteAsync("a1", SignedIn);

        Assert.Equal("/customer/acme/me/addresses/a1", Uri(handler));
    }

    [Fact]
    public async Task Empty_arguments_are_rejected()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "{}");
        CustomerService customers = Create(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await customers.RefreshSessionAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await customers.Addresses.DeleteAsync("", SignedIn));
        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Profile, password and tags ----------

    [Fact]
    public async Task Updating_the_profile_patches()
    {
        // The endpoint routes PATCH only; a PUT is a 404 dressed up as a bug
        // report about «updates not saving».
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"c1"}""");
        CustomerService customers = Create(handler);

        await customers.UpdateMeAsync(new Customer(), SignedIn);

        Assert.Equal(HttpMethod.Patch, handler.RequestMethods[0]);
        Assert.Equal("/customer/acme/me", Uri(handler));
    }

    [Fact]
    public async Task Changing_a_password_sends_both_of_them()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        CustomerService customers = Create(handler);

        await customers.ChangePasswordAsync("old-one", "new-one", SignedIn);

        Assert.Equal("/customer/acme/password/change", Uri(handler));
        Assert.Contains("old-one", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("new-one", handler.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tags_travel_comma_separated()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        CustomerService customers = Create(handler);

        await customers.Addresses.AddTagsAsync("a1", ["BILLING", "SHIPPING"], SignedIn);

        Assert.Equal(
            "/customer/acme/me/addresses/a1/tags?tags=BILLING%2CSHIPPING",
            Uri(handler));
    }

    [Fact]
    public async Task No_tags_is_rejected_before_the_request()
    {
        // An empty list would send tags= and clear nothing, silently.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        CustomerService customers = Create(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await customers.Addresses.RemoveTagsAsync("a1", [], SignedIn));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task An_address_update_patches_too()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, """{"id":"a1"}""");
        CustomerService customers = Create(handler);

        await customers.Addresses.UpdateAsync(
            "a1",
            new AddressUpdateDto(),
            SignedIn);

        Assert.Equal(HttpMethod.Patch, handler.RequestMethods[0]);
    }

    [Fact]
    public async Task Confirming_a_sign_up_carries_the_token_in_the_path()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, string.Empty);
        CustomerService customers = Create(handler);

        await customers.ConfirmSignUpAsync("tok en/1");

        Assert.Equal("/customer/acme/signup/optin/tok%20en%2F1", Uri(handler));
    }
}
