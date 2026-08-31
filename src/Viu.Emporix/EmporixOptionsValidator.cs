using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Validates <see cref="EmporixOptions"/> at startup so an incomplete
/// configuration does not surface on the first API call.
/// </summary>
internal sealed partial class EmporixOptionsValidator : IValidateOptions<EmporixOptions>
{
    /// <summary>
    /// Emporix requires a lowercase, alphanumeric tenant.
    /// </summary>
    /// <remarks>
    /// Deliberately without a length bound. The Node SDK enforces 3 to 16
    /// characters and notes itself that the bound is an assumption rather than
    /// documented — it would reject a longer tenant that Emporix accepts.
    /// </remarks>
    [GeneratedRegex("^[a-z][a-z0-9]*$")]
    private static partial Regex TenantPattern { get; }

    public ValidateOptionsResult Validate(string? name, EmporixOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Tenant))
        {
            failures.Add($"{nameof(EmporixOptions.Tenant)} is required.");
        }
        else if (!TenantPattern.IsMatch(options.Tenant))
        {
            failures.Add(
                $"{nameof(EmporixOptions.Tenant)} \"{options.Tenant}\" is invalid: "
                + "lowercase letters and digits only, starting with a letter.");
        }

        if (!Uri.TryCreate(options.Host, UriKind.Absolute, out Uri? host)
            || (host.Scheme != Uri.UriSchemeHttps && host.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add(
                $"{nameof(EmporixOptions.Host)} \"{options.Host}\" is not an absolute http or https URL.");
        }

        // Credentials as a whole are optional — a client that only forwards
        // externally supplied tokens needs none. But a set that is present must
        // be complete: a half-filled set is always a mistake.
        ValidateService(options.Credentials.Backend, $"{nameof(EmporixCredentials.Backend)}", failures);

        foreach ((string key, EmporixServiceCredentials credentials) in options.Credentials.Custom)
        {
            ValidateService(credentials, $"{nameof(EmporixCredentials.Custom)}[\"{key}\"]", failures);
        }

        if (options.Credentials.Storefront is { } storefront
            && string.IsNullOrWhiteSpace(storefront.ClientId))
        {
            failures.Add(
                $"{nameof(EmporixCredentials.Storefront)}.{nameof(EmporixStorefrontCredentials.ClientId)} "
                + "is required when storefront credentials are set.");
        }

        if (options.Timeouts.Connect <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(EmporixOptions.Timeouts)}.{nameof(EmporixTimeoutOptions.Connect)} must be positive.");
        }

        if (options.Timeouts.Read <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(EmporixOptions.Timeouts)}.{nameof(EmporixTimeoutOptions.Read)} must be positive.");
        }

        if (options.Timeouts.Connect > options.Timeouts.Read)
        {
            failures.Add(
                $"{nameof(EmporixTimeoutOptions.Connect)} ({options.Timeouts.Connect}) must not exceed "
                + $"{nameof(EmporixTimeoutOptions.Read)} ({options.Timeouts.Read}) — the overall limit "
                + "includes the connect limit.");
        }

        if (options.Retry.MaxAttempts < 1)
        {
            failures.Add(
                $"{nameof(EmporixOptions.Retry)}.{nameof(EmporixRetryOptions.MaxAttempts)} must be at least 1 "
                + "(1 means no retry).");
        }

        if (options.Retry.MaxBackoff < TimeSpan.Zero)
        {
            failures.Add($"{nameof(EmporixOptions.Retry)}.{nameof(EmporixRetryOptions.MaxBackoff)} must not be negative.");
        }

        if (options.TokenCache.ExpirationBuffer < TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(EmporixOptions.TokenCache)}.{nameof(EmporixTokenCacheOptions.ExpirationBuffer)} "
                + "must not be negative.");
        }

        if (options.TokenCache.MaxLifetime <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(EmporixOptions.TokenCache)}.{nameof(EmporixTokenCacheOptions.MaxLifetime)} must be positive.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateService(
        EmporixServiceCredentials? credentials,
        string path,
        List<string> failures)
    {
        if (credentials is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(credentials.ClientId))
        {
            failures.Add($"{path}.{nameof(EmporixServiceCredentials.ClientId)} is required.");
        }

        if (string.IsNullOrWhiteSpace(credentials.Secret))
        {
            failures.Add($"{path}.{nameof(EmporixServiceCredentials.Secret)} is required.");
        }
    }
}
