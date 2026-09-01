namespace Viu.Emporix.SmokeTest;

/// <summary>
/// What the smoke test needs to reach a tenant.
/// </summary>
/// <remarks>
/// Read from the environment, never from a file in the repository: a client id
/// belongs to a tenant, and a tenant belongs to whoever is running this.
/// </remarks>
internal sealed record Configuration(
    string Tenant,
    string ClientId,
    string Site,
    string? Currency,
    string? Country,
    string? ProductId,
    string? Host,
    string? BackendClientId,
    string? BackendSecret)
{
    /// <summary>
    /// Whether the second, service-token pass can run.
    /// </summary>
    /// <remarks>
    /// The anonymous pass needs only a storefront client id. Everything a seller
    /// does — taxes, IAM, imports, the audit log — needs client credentials, and
    /// those are a different pair. The smoke test runs whichever it has.
    /// </remarks>
    public bool HasBackendCredentials =>
        BackendClientId is { Length: > 0 } && BackendSecret is { Length: > 0 };

    /// <summary>
    /// Reads the configuration, or explains what is missing.
    /// </summary>
    public static Configuration? FromEnvironment(out string? missing)
    {
        string? tenant = Read("EMPORIX_TENANT");
        string? clientId = Read("EMPORIX_CLIENT_ID");
        string? site = Read("EMPORIX_SITE");

        List<string> absent = [];

        if (tenant is null)
        {
            absent.Add("EMPORIX_TENANT");
        }

        if (clientId is null)
        {
            absent.Add("EMPORIX_CLIENT_ID");
        }

        if (site is null)
        {
            absent.Add("EMPORIX_SITE");
        }

        if (absent.Count > 0)
        {
            missing = string.Join(", ", absent);
            return null;
        }

        missing = null;
        return new Configuration(
            tenant!,
            clientId!,
            site!,
            Read("EMPORIX_CURRENCY"),
            Read("EMPORIX_COUNTRY"),
            Read("EMPORIX_PRODUCT_ID"),
            Read("EMPORIX_HOST"),
            Read("EMPORIX_BACKEND_CLIENT_ID"),
            Read("EMPORIX_BACKEND_SECRET"));
    }

    public EmporixOptions ToOptions()
    {
        EmporixOptions options = new()
        {
            Tenant = Tenant,
            Credentials = new EmporixCredentials
            {
                Backend = HasBackendCredentials
                    ? new EmporixServiceCredentials
                    {
                        ClientId = BackendClientId!,
                        Secret = BackendSecret!,
                    }
                    : null,
                Storefront = new EmporixStorefrontCredentials
                {
                    ClientId = ClientId,

                    // Without this the anonymous token carries no context, and
                    // price matching answers with an empty list rather than an
                    // error — the failure this smoke test most needs to surface.
                    Context = new EmporixStorefrontContext
                    {
                        Currency = Currency,
                        SiteCode = Site,
                        TargetLocation = Country,
                    },
                },
            },
        };

        if (Host is { Length: > 0 })
        {
            options.Host = Host;
        }

        return options;
    }

    private static string? Read(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
