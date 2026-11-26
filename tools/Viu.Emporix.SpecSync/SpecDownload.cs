namespace Viu.Emporix.SpecSync;

/// <summary>
/// Reads one upstream specification, optionally through a directory of raw,
/// unrepaired copies.
/// </summary>
/// <remarks>
/// The skill run in <c>.github/workflows/spec-sync.yml</c> has no network once
/// Claude starts. A step before it fetches with <see cref="CacheVariable"/> set,
/// which fills the directory, and every fetch after that reads from it. That is
/// what lets a repair added to <see cref="SpecPatches"/> take effect offline:
/// the repairs are applied on fetch, never to the vendored files.
/// </remarks>
internal static class SpecDownload
{
    public const string CacheVariable = "EMPORIX_SPEC_CACHE";

    public static async Task<string> ReadAsync(
        HttpClient http,
        SpecSource spec,
        string? cacheDirectory,
        CancellationToken cancellationToken)
    {
        string? cached = string.IsNullOrEmpty(cacheDirectory)
            ? null
            : Path.Combine(cacheDirectory, $"{spec.Name}.yml");

        if (cached is not null && File.Exists(cached))
        {
            return await File.ReadAllTextAsync(cached, cancellationToken);
        }

        using HttpResponseMessage response = await http.GetAsync(spec.Url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{spec.Name}: {(int)response.StatusCode} from {spec.Url}");
        }

        string raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (cached is not null)
        {
            // Written aside and moved, so an interrupted run leaves no half file
            // that the next fetch would read as the specification.
            Directory.CreateDirectory(cacheDirectory!);
            await File.WriteAllTextAsync($"{cached}.partial", raw, cancellationToken);
            File.Move($"{cached}.partial", cached, overwrite: true);
        }

        return raw;
    }
}
