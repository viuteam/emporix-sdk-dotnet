using Microsoft.Extensions.Options;

namespace Viu.Emporix.Tests;

public class EmporixOptionsValidatorTests
{
    private static readonly EmporixOptionsValidator Validator = new();

    /// <summary>The minimum that must be valid: a tenant, nothing else.</summary>
    private static EmporixOptions Minimal() => new() { Tenant = "acme" };

    private static ValidateOptionsResult Validate(EmporixOptions options)
        => Validator.Validate(name: null, options);

    [Fact]
    public void Minimal_options_are_valid()
    {
        Assert.True(Validate(Minimal()).Succeeded);
    }

    [Fact]
    public void Credentials_may_be_empty()
    {
        // A client that only forwards externally supplied tokens needs no
        // credentials of its own. The Node SDK allows that explicitly.
        EmporixOptions options = Minimal();

        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_tenant_fails(string tenant)
    {
        ValidateOptionsResult result = Validate(new EmporixOptions { Tenant = tenant });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Tenant", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Acme")]      // Grossbuchstabe
    [InlineData("1acme")]     // beginnt mit Ziffer
    [InlineData("acme-shop")] // Bindestrich
    [InlineData("acme_shop")] // Unterstrich
    [InlineData("acme shop")] // Leerzeichen
    public void Malformed_tenant_fails(string tenant)
    {
        Assert.True(Validate(new EmporixOptions { Tenant = tenant }).Failed);
    }

    [Theory]
    [InlineData("ab")]                      // kurz
    [InlineData("a")]                       // sehr kurz
    [InlineData("averylongtenantname1234")] // 23 Zeichen
    public void Tenant_length_is_not_constrained(string tenant)
    {
        // A deliberate difference from the Node SDK: it enforces a 3-to-16
        // character rule and notes itself that the bound is an undocumented
        // assumption. A longer tenant that Emporix accepts must not fail here.
        Assert.True(Validate(new EmporixOptions { Tenant = tenant }).Succeeded);
    }

    [Theory]
    [InlineData("not-absolute")]
    [InlineData("ftp://api.emporix.io")]
    [InlineData("")]
    public void Invalid_host_fails(string host)
    {
        EmporixOptions options = Minimal();
        options.Host = host;

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Host", StringComparison.Ordinal));
    }

    [Fact]
    public void Backend_without_secret_fails()
    {
        EmporixOptions options = Minimal();
        options.Credentials.Backend = new EmporixServiceCredentials { ClientId = "id" };

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Backend_without_client_id_fails()
    {
        EmporixOptions options = Minimal();
        options.Credentials.Backend = new EmporixServiceCredentials { Secret = "s" };

        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void Complete_backend_is_valid()
    {
        EmporixOptions options = Minimal();
        options.Credentials.Backend = new EmporixServiceCredentials { ClientId = "id", Secret = "s" };

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Storefront_without_client_id_fails()
    {
        EmporixOptions options = Minimal();
        options.Credentials.Storefront = new EmporixStorefrontCredentials();

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Storefront", StringComparison.Ordinal));
    }

    [Fact]
    public void Storefront_needs_no_secret()
    {
        EmporixOptions options = Minimal();
        options.Credentials.Storefront = new EmporixStorefrontCredentials { ClientId = "public-id" };

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Incomplete_custom_credential_set_fails_and_names_the_key()
    {
        EmporixOptions options = Minimal();
        options.Credentials.Custom["partner"] = new EmporixServiceCredentials { ClientId = "id" };

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("partner", StringComparison.Ordinal));
    }

    [Fact]
    public void A_custom_set_named_like_the_default_fails_because_it_is_unreachable()
    {
        // «backend» addresses Credentials.Backend, so a custom set under that
        // key can never be resolved: the token provider checks the default name
        // first and never reaches the dictionary. Configured together, the two
        // silently disagree about which client id is in use; configured alone,
        // the error says Backend is not set, which reads as nonsense to someone
        // who just configured «backend».
        EmporixOptions options = Minimal();
        options.Credentials.Custom[AuthContext.DefaultCredentialSet] =
            new EmporixServiceCredentials { ClientId = "id", Secret = "secret" };

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Backend", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Backend")]
    [InlineData("BACKEND")]
    public void A_custom_set_named_like_the_default_is_rejected_whatever_its_casing(string key)
    {
        // Names are compared without regard to case, so «Backend» addresses the
        // same set as «backend» and is just as unreachable.
        EmporixOptions options = Minimal();
        options.Credentials.Custom[key] =
            new EmporixServiceCredentials { ClientId = "id", Secret = "secret" };

        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void Connect_timeout_larger_than_read_timeout_fails()
    {
        // The overall limit includes the connect limit — the other way round the
        // connect limit would have no effect.
        EmporixOptions options = Minimal();
        options.Timeouts.Connect = TimeSpan.FromSeconds(30);
        options.Timeouts.Read = TimeSpan.FromSeconds(10);

        Assert.True(Validate(options).Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_timeouts_fail(int seconds)
    {
        EmporixOptions options = Minimal();
        options.Timeouts.Read = TimeSpan.FromSeconds(seconds);

        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void Zero_retry_attempts_fails()
    {
        EmporixOptions options = Minimal();
        options.Retry.MaxAttempts = 0;

        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void Single_attempt_disables_retry_and_is_valid()
    {
        EmporixOptions options = Minimal();
        options.Retry.MaxAttempts = 1;

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Non_positive_token_lifetime_fails()
    {
        EmporixOptions options = Minimal();
        options.TokenCache.MaxLifetime = TimeSpan.Zero;

        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void All_failures_are_reported_at_once()
    {
        // Whoever misconfigured three things should not have to start three times.
        EmporixOptions options = new()
        {
            Tenant = "INVALID",
            Host = "also-not",
        };
        options.Retry.MaxAttempts = 0;

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    [Fact]
    public void Defaults_match_the_node_sdk()
    {
        // Matching the Node SDK's behaviour is a deliberate promise; a quietly
        // changed default would be a deviation.
        EmporixOptions options = new();

        Assert.Equal("https://api.emporix.io", options.Host);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeouts.Connect);
        Assert.Equal(TimeSpan.FromSeconds(60), options.Timeouts.Read);
        Assert.Equal(3, options.Retry.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(8), options.Retry.MaxBackoff);
        Assert.Equal(TimeSpan.FromSeconds(60), options.TokenCache.ExpirationBuffer);
        Assert.Equal(TimeSpan.FromHours(1), options.TokenCache.MaxLifetime);
    }
}
