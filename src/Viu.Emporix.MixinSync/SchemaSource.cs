using Viu.Emporix.SchemaModels;

namespace Viu.Emporix.MixinSync;

/// <summary>
/// Reads a tenant's mixins from its Schema Service.
/// </summary>
/// <remarks>
/// The hosted schema at <c>metadata.url</c> is authoritative and is fetched
/// first. Only when that fails does the Schema Service's own attribute model
/// stand in, which is a lossier description of the same thing.
/// </remarks>
public sealed class SchemaSource
{
    private const int PageSize = 100;

    private readonly EmporixClient _client;
    private readonly HttpClient _http;

    /// <summary>Reads from one tenant.</summary>
    /// <param name="client">A client configured for that tenant.</param>
    /// <param name="http">Used for the schema URLs, which need no Emporix token.</param>
    public SchemaSource(EmporixClient client, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(http);

        _client = client;
        _http = http;
    }

    /// <summary>Every mixin the tenant has, across all pages.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The mixins, one per schema and assigned entity type.</returns>
    public async Task<IReadOnlyList<RawMixin>> ListAsync(CancellationToken cancellationToken = default)
    {
        // PaginatedItems.EnumerateAllAsync already walks every page and
        // terminates on HasNextPage. Rolling that loop by hand would duplicate
        // logic the SDK maintains, including its documented case where a full
        // final page costs one extra empty request.
        List<SchemaResponse> all = [];

        await foreach (SchemaResponse schema in PaginatedItems.EnumerateAllAsync(
            (page, token) => _client.Schemas.ListAsync(
                pageNumber: page, pageSize: PageSize, cancellationToken: token),
            cancellationToken: cancellationToken))
        {
            all.Add(schema);
        }

        return await ToRawMixins(all, FetchAsync);

        async Task<string?> FetchAsync(string url)
        {
            try
            {
                using HttpResponseMessage response = await _http.GetAsync(new Uri(url), cancellationToken);

                return response.IsSuccessStatusCode
                    ? await response.Content.ReadAsStringAsync(cancellationToken)
                    : null;
            }
            catch (Exception error)
                when (error is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                // A schema whose url is unreachable still has its attribute
                // model, which is better than dropping the mixin entirely.
                return null;
            }
        }
    }

    /// <summary>
    /// Turns schemas into mixins, one per assigned entity type.
    /// </summary>
    /// <param name="schemas">What the Schema Service returned.</param>
    /// <param name="fetch">Fetches a schema URL, or returns null on failure.</param>
    /// <returns>The normalized mixins.</returns>
    /// <remarks>
    /// Separated from the HTTP so it can be tested without a tenant.
    /// </remarks>
    public static async Task<IReadOnlyList<RawMixin>> ToRawMixins(
        IEnumerable<SchemaResponse> schemas,
        Func<string, Task<string?>> fetch)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(fetch);

        List<RawMixin> mixins = [];

        foreach (SchemaResponse schema in schemas)
        {
            string? key = schema.Id;
            double? version = schema.Metadata?.Version;
            string? url = schema.Metadata?.Url;

            // Without all three there is nothing to generate from and nothing to
            // record in metadata.mixins, so the schema is not usable as a mixin.
            if (string.IsNullOrWhiteSpace(key) || version is null || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            string body = await fetch(url) ?? AttributeSchema.FromAttributes(schema.Attributes ?? []);

            foreach (SchemaType type in schema.Types ?? [])
            {
                mixins.Add(new RawMixin
                {
                    Key = key,
                    Entity = type.ToString(),
                    Version = (int)version.Value,
                    Url = url,
                    Schema = body,
                });
            }
        }

        return mixins;
    }
}
