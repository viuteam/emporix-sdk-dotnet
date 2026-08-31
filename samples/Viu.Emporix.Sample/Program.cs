using Viu.Emporix;
using Viu.Emporix.ProductModels;

// Shows usage without a container. Credentials come from the environment so
// none end up in source:
//
//   EMPORIX_TENANT=mytenant EMPORIX_CLIENT_ID=... dotnet run
//
// With nothing set, the sample explains what it would need and exits.

string? tenant = Environment.GetEnvironmentVariable("EMPORIX_TENANT");
string? clientId = Environment.GetEnvironmentVariable("EMPORIX_CLIENT_ID");

if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(clientId))
{
    Console.WriteLine("Please set EMPORIX_TENANT and EMPORIX_CLIENT_ID.");
    return 0;
}

using EmporixClient client = new(new EmporixOptions
{
    Tenant = tenant,
    Credentials =
    {
        Storefront = new EmporixStorefrontCredentials { ClientId = clientId },
    },
});

try
{
    PaginatedItems<BasicProductWithId> page = await client.Products.ListAsync(
        new ProductPageOptions { PageSize = 5, IncludeTotalCount = true });

    Console.WriteLine($"Tenant {client.Tenant}: {page.Items.Count} of {page.TotalCount?.ToString() ?? "?"} products");

    foreach (BasicProductWithId product in page.Items)
    {
        Console.WriteLine($"  {product.Id}  {product.Code}");
    }

    return 0;
}
catch (EmporixApiException exception)
{
    // The correlation id belongs in every support request.
    Console.Error.WriteLine(
        $"Emporix responded with {(int)exception.StatusCode}: {exception.Message}");
    Console.Error.WriteLine($"Correlation id: {exception.CorrelationId}");
    return 1;
}
catch (EmporixTransportException exception)
{
    Console.Error.WriteLine($"Emporix was unreachable: {exception.Message}");
    return 1;
}
