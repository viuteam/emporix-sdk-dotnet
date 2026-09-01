using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Viu.Emporix.SchemaModels;

namespace Viu.Emporix;

/// <summary>
/// Schemas and custom entities — a tenant's own data shapes.
/// </summary>
/// <remarks>
/// <para>
/// Two things, and the difference matters. A <b>schema</b> describes the mixins
/// a tenant hangs on Emporix's own objects — the extra fields on a product, say.
/// A <b>custom entity</b> is a shape Emporix knows nothing about, stored and
/// queried here, reached through <see cref="CustomEntities"/> and
/// <see cref="InstancesOf"/>.
/// </para>
/// <para>
/// An instance is arbitrary JSON by definition, so instances are read and
/// written as <see cref="System.Text.Json.JsonElement"/>: the whole point is
/// that the SDK does not know the shape.
/// </para>
/// </remarks>
public sealed class SchemaService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal SchemaService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/schema/{_tenant}/schemas";

    /// <summary>The tenant's own entity types.</summary>
    public CustomEntityOperations CustomEntities => new(_http, _tenant);

    /// <summary>The stored instances of one custom entity type.</summary>
    /// <param name="type">The entity type.</param>
    public CustomInstanceOperations InstancesOf(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return new CustomInstanceOperations(
            _http,
            $"/schema/{_tenant}/custom-entities/{Uri.EscapeDataString(type)}/instances");
    }

    /// <summary>Lists the schemas.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<SchemaResponse>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
            },
            SchemaJsonContext.Default.ListSchemaResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a schema.</summary>
    /// <param name="id">The schema id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SchemaResponse?> GetAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            SchemaJsonContext.Default.SchemaResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a schema.</summary>
    /// <param name="schema">The schema to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IdResponse?> CreateAsync(
        SchemaCreation schema,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    schema,
                    SchemaJsonContext.Default.SchemaCreation),
            },
            SchemaJsonContext.Default.IdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a schema.</summary>
    /// <param name="id">The schema id.</param>
    /// <param name="schema">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Objects already carrying mixins under this schema are not migrated, so a
    /// removed attribute stays in the stored data and a narrowed one is not
    /// re-validated.
    /// </remarks>
    public Task ReplaceAsync(
        string id,
        SchemaUpdate schema,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(schema);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(schema, SchemaJsonContext.Default.SchemaUpdate),
            },
            cancellationToken);
    }

    /// <summary>Sets which Emporix types a schema applies to.</summary>
    /// <param name="id">The schema id.</param>
    /// <param name="types">The types.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task SetTypesAsync(
        string id,
        SchemaTypes types,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(types);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(id)}/types",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(types, SchemaJsonContext.Default.SchemaTypes),
            },
            cancellationToken);
    }

    /// <summary>Deletes a schema.</summary>
    /// <param name="id">The schema id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Mixin data already stored stays, unvalidated by anything.</remarks>
    public Task DeleteAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Uploads a schema as a file.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Sent as <c>multipart/form-data</c>, the same shape the media service
    /// uses. For a schema kept in version control this is the way to apply it.
    /// </remarks>
    public async Task<SchemaFileResponse?> UploadAsync(
        Stream content,
        string fileName,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using MultipartFormDataContent form = [];
        StreamContent file = new(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", fileName);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/file",
                Auth = Defaults.Service(auth),
                Content = form,
            },
            SchemaJsonContext.Default.SchemaFileResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the Emporix types schemas can extend.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<System.Text.Json.JsonElement> ListTypesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/schema/{_tenant}/types",
                Auth = Defaults.Service(auth),
            },
            SchemaJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Lists the schema references.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Which schema is bound to which object type.</remarks>
    public async Task<System.Text.Json.JsonElement> ListReferencesAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/schema/{_tenant}/references",
                Auth = Defaults.Service(auth),
            },
            SchemaJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Fetches a schema reference.</summary>
    /// <param name="id">The reference id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<System.Text.Json.JsonElement> GetReferenceAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/schema/{_tenant}/references/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            SchemaJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a schema reference.</summary>
    /// <param name="reference">The reference to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IdResponse?> CreateReferenceAsync(
        ReferenceCreation reference,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/schema/{_tenant}/references",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    reference,
                    SchemaJsonContext.Default.ReferenceCreation),
            },
            SchemaJsonContext.Default.IdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a schema reference.</summary>
    /// <param name="id">The reference id.</param>
    /// <param name="reference">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceReferenceAsync(
        string id,
        ReferenceUpdate reference,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(reference);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"/schema/{_tenant}/references/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    reference,
                    SchemaJsonContext.Default.ReferenceUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a schema reference.</summary>
    /// <param name="id">The reference id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteReferenceAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"/schema/{_tenant}/references/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Exports custom entity data.</summary>
    /// <param name="request">What to export.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Runs as a job rather than answering with the data. It only reads, so it
    /// is declared repeatable.
    /// </remarks>
    public async Task<ExportImportResponse?> ExportAsync(
        ExportImportRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/schema/{_tenant}/custom-entities/export",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    SchemaJsonContext.Default.ExportImportRequest),
                Idempotent = true,
            },
            SchemaJsonContext.Default.ExportImportResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Imports custom entity data.</summary>
    /// <param name="request">What to import.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Not repeatable: a retried import writes the data twice.
    /// </remarks>
    public async Task<ExportImportResponse?> ImportAsync(
        ExportImportRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/schema/{_tenant}/custom-entities/import",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    SchemaJsonContext.Default.ExportImportRequest),
            },
            SchemaJsonContext.Default.ExportImportResponse,
            cancellationToken).ConfigureAwait(false);
    }

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
    ];
}

/// <summary>
/// The tenant's own entity types.
/// </summary>
/// <remarks>
/// A type declared here is a shape Emporix knows nothing about. Its stored
/// records are reached through <see cref="SchemaService.InstancesOf"/>.
/// </remarks>
public sealed class CustomEntityOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CustomEntityOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/schema/{_tenant}/custom-entities";

    /// <summary>Lists the custom entity types.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CustomSchemaTypeResponse>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
            },
            SchemaJsonContext.Default.ListCustomSchemaTypeResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches a custom entity type.</summary>
    /// <param name="id">The type id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CustomSchemaTypeResponse?> GetAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            SchemaJsonContext.Default.CustomSchemaTypeResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Declares a custom entity type.</summary>
    /// <param name="type">The type to declare.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IdResponse?> CreateAsync(
        CustomSchemaTypeCreation type,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    type,
                    SchemaJsonContext.Default.CustomSchemaTypeCreation),
            },
            SchemaJsonContext.Default.IdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a custom entity type.</summary>
    /// <param name="id">The type id.</param>
    /// <param name="type">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Instances already stored are not migrated: narrowing the type leaves
    /// records that would no longer be accepted.
    /// </remarks>
    public async Task<IdResponse?> ReplaceAsync(
        string id,
        CustomSchemaTypeUpdate type,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(type);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    type,
                    SchemaJsonContext.Default.CustomSchemaTypeUpdate),
            },
            SchemaJsonContext.Default.IdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a custom entity type.</summary>
    /// <param name="id">The type id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// The stored records of one custom entity type.
/// </summary>
/// <remarks>
/// Reached through <see cref="SchemaService.InstancesOf"/>. An instance is
/// arbitrary JSON — the SDK deliberately does not know its shape, because the
/// point of a custom entity is that the tenant defines it.
/// </remarks>
public sealed class CustomInstanceOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal CustomInstanceOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Fetches one page of instances.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<System.Text.Json.JsonElement> ListAsync(
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Query = SchemaService.Paging(pageNumber, pageSize),
            },
            SchemaJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one instance.</summary>
    /// <param name="id">The instance id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<System.Text.Json.JsonElement> GetAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            SchemaJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches the instances.</summary>
    /// <param name="query">The search body, as the tenant's type defines it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A <c>POST</c> that only reads, so it is repeatable. The body's shape
    /// depends on the entity type, which is why it is raw JSON.
    /// </remarks>
    public async Task<System.Text.Json.JsonElement> SearchAsync(
        System.Text.Json.JsonElement query,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{_basePath}/search",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(query, SchemaJsonContext.Default.JsonElement),
                Idempotent = true,
            },
            SchemaJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Stores an instance.</summary>
    /// <param name="instance">The record, as the tenant's type defines it.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IdResponse?> CreateAsync(
        System.Text.Json.JsonElement instance,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(instance, SchemaJsonContext.Default.JsonElement),
            },
            SchemaJsonContext.Default.IdResponse,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Replaces an instance.</summary>
    /// <param name="id">The instance id.</param>
    /// <param name="instance">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IdResponse?> ReplaceAsync(
        string id,
        System.Text.Json.JsonElement instance,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(instance, SchemaJsonContext.Default.JsonElement),
            },
            SchemaJsonContext.Default.IdResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes individual fields of an instance.</summary>
    /// <param name="id">The instance id.</param>
    /// <param name="operations">The JSON Patch operations to apply.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string id,
        IEnumerable<PatchOperation> operations,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(operations);

        List<PatchOperation> body = [.. operations];
        ArgumentOutOfRangeException.ThrowIfZero(body.Count, nameof(operations));

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{_basePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    body,
                    SchemaJsonContext.Default.ListPatchOperation),
            },
            cancellationToken);
    }

    /// <summary>Deletes an instance.</summary>
    /// <param name="id">The instance id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string id,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/{Uri.EscapeDataString(id)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Stores several instances in one call.</summary>
    /// <param name="instances">The records.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<BulkResponse>> CreateManyAsync(
        System.Text.Json.JsonElement instances,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Post, instances, auth, cancellationToken);

    /// <summary>Replaces several instances in one call.</summary>
    /// <param name="instances">The records in their new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<BulkResponse>> ReplaceManyAsync(
        System.Text.Json.JsonElement instances,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Put, instances, auth, cancellationToken);

    /// <summary>Changes fields of several instances in one call.</summary>
    /// <param name="request">Which instances, and what to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<BulkResponse>> UpdateManyAsync(
        BulkPatchCustomInstanceRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{_basePath}/bulk",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    SchemaJsonContext.Default.BulkPatchCustomInstanceRequest),
            },
            SchemaJsonContext.Default.ListBulkResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Deletes several instances in one call.</summary>
    /// <param name="instances">Which instances to delete.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<BulkResponse>> DeleteManyAsync(
        System.Text.Json.JsonElement instances,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Delete, instances, auth, cancellationToken);

    private async Task<IReadOnlyList<BulkResponse>> BulkAsync(
        HttpMethod method,
        System.Text.Json.JsonElement instances,
        AuthContext auth,
        CancellationToken cancellationToken)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = method,
                Path = $"{_basePath}/bulk",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(instances, SchemaJsonContext.Default.JsonElement),
            },
            SchemaJsonContext.Default.ListBulkResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
}
