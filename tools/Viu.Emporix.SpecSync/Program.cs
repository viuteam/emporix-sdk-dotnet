using System.Diagnostics;
using System.Text.Json;
using Viu.Emporix.SpecSync;

// Downloads the Emporix API specifications, repairs known defects and generates
// the SDK's data types from them.
//
//   dotnet run --project tools/Viu.Emporix.SpecSync -- fetch      download only
//   dotnet run --project tools/Viu.Emporix.SpecSync -- generate   generate only
//   dotnet run --project tools/Viu.Emporix.SpecSync              both

string command = args.Length > 0 ? args[0] : "all";
string repositoryRoot = FindRepositoryRoot();
string specsDirectory = Path.Combine(repositoryRoot, "specs");
string generatedDirectory = Path.Combine(repositoryRoot, "src", "Viu.Emporix", "Generated");

return command switch
{
    "fetch" => await FetchAsync(),
    "generate" => await GenerateAsync(),
    "all" => await FetchAsync() is var fetched && fetched != 0 ? fetched : await GenerateAsync(),
    _ => Fail($"Unknown command \"{command}\". Allowed: fetch, generate, all."),
};

async Task<int> FetchAsync()
{
    Directory.CreateDirectory(specsDirectory);

    string manifestPath = Path.Combine(specsDirectory, "sync-manifest.json");
    SyncManifest? previous = ReadManifest(manifestPath);
    DateTimeOffset now = DateTimeOffset.UtcNow;

    using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    SortedDictionary<string, SpecManifestEntry> entries = new(StringComparer.Ordinal);
    List<string> staleWarnings = [];
    object gate = new();

    Console.WriteLine($"Downloading {SpecCatalog.All.Count} specifications …");

    await Parallel.ForEachAsync(
        SpecCatalog.All,
        new ParallelOptions { MaxDegreeOfParallelism = 8 },
        async (spec, cancellationToken) =>
        {
            using HttpResponseMessage response = await http.GetAsync(spec.Url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"{spec.Name}: {(int)response.StatusCode} von {spec.Url}");
            }

            string raw = await response.Content.ReadAsStringAsync(cancellationToken);

            // Repair before writing and hashing: the vendored specification is
            // then already correct and the digest stays stable across runs.
            PatchOutcome outcome = SpecPatches.Apply(spec.Name, raw);

            await File.WriteAllTextAsync(
                Path.Combine(specsDirectory, $"{spec.Name}.yml"),
                outcome.Yaml,
                cancellationToken);

            lock (gate)
            {
                entries[spec.Name] = new SpecManifestEntry
                {
                    Url = spec.Url,
                    SpecVersion = SyncManifest.ReadSpecVersion(outcome.Yaml),
                    FetchedAt = now,
                    Sha256 = SyncManifest.Hash(outcome.Yaml),
                };

                foreach (string reason in outcome.Applied)
                {
                    Console.WriteLine($"  repaired {spec.Name}: {reason}");
                }

                foreach (SpecPatch patch in outcome.Stale)
                {
                    staleWarnings.Add($"  ⚠ {spec.Name}: repair no longer applies — remove it: {patch.Reason}");
                }
            }
        });

    foreach (string warning in staleWarnings)
    {
        Console.WriteLine(warning);
    }

    SyncManifest next = new() { GeneratedAt = now, Services = entries };
    IReadOnlyList<string> changed = SyncManifest.Diff(previous, next);

    await File.WriteAllTextAsync(
        manifestPath,
        JsonSerializer.Serialize(next, SyncManifest.JsonOptions) + Environment.NewLine);

    if (previous is null)
    {
        Console.WriteLine($"Wrote initial state ({entries.Count} services).");
    }
    else if (changed.Count > 0)
    {
        Console.WriteLine($"Changed: {string.Join(", ", changed)}");
    }
    else
    {
        Console.WriteLine("No content changes.");
    }

    return 0;
}

async Task<int> GenerateAsync()
{
    if (!Directory.Exists(specsDirectory))
    {
        return Fail($"No directory {specsDirectory}. Run \"fetch\" first.");
    }

    Directory.CreateDirectory(generatedDirectory);

    // Clear it out entirely: a specification that disappears from the catalog
    // must not leave an orphaned file behind.
    foreach (string existing in Directory.GetFiles(generatedDirectory, "*.cs"))
    {
        File.Delete(existing);
    }

    Console.WriteLine($"Generating types for {SpecCatalog.All.Count} specifications …");

    foreach (SpecSource spec in SpecCatalog.All)
    {
        string input = Path.Combine(specsDirectory, $"{spec.Name}.yml");

        if (!File.Exists(input))
        {
            return Fail($"Missing: {input}. Run \"fetch\" first.");
        }

        string typeName = ToPascalCase(spec.Name);
        string output = Path.Combine(generatedDirectory, $"{typeName}.cs");

        int exitCode = await RunAsync(
            "dotnet",
            [
                "nswag",
                "openapi2csclient",
                $"/input:{input}",
                $"/output:{output}",
                $"/namespace:Viu.Emporix.{typeName}Models",

                // Data types only. The calling layer is hand-written — it carries
                // the knowledge no specification provides (ADR-0001).
                "/generateClientClasses:false",
                "/jsonLibrary:SystemTextJson",

                // No validation attributes: they pull in reflection and conflict
                // with the AOT requirement from ADR-0004.
                "/generateDataAnnotations:false",
                "/generateNullableReferenceTypes:true",
                "/generateOptionalPropertiesAsNullable:true",

                // Inline unnamed «any» types instead of referencing an empty
                // class that is never generated.
                "/inlineNamedAny:true",
            ],
            repositoryRoot);

        if (exitCode != 0)
        {
            return Fail($"NSwag failed on {spec.Name} with exit code {exitCode}.");
        }

        IReadOnlyList<string> resolved = await PostProcessAsync(output);
        Console.WriteLine(resolved.Count == 0
            ? $"  {spec.Name} → Generated/{typeName}.cs"
            : $"  {spec.Name} → Generated/{typeName}.cs  (resolved: {string.Join(", ", resolved)})");
    }

    Console.WriteLine("Done.");
    return 0;
}

/// <summary>
/// Puts the generated-code marker at the top and runs the post-processing.
/// </summary>
/// <remarks>
/// Analyzers use the marker to tell that the file is not hand-maintained and
/// their rules do not apply. Without that line every generated file would have
/// to meet the standards written for hand-written code.
/// </remarks>
static async Task<IReadOnlyList<string>> PostProcessAsync(string path)
{
    const string marker = "// <auto-generated />";
    string content = await File.ReadAllTextAsync(path);

    (content, IReadOnlyList<string> resolved) = GeneratedCodeFixer.ResolveEmptyAliasClasses(content);
    (content, IReadOnlyList<string> dangling) = GeneratedCodeFixer.ResolveDanglingTypeReferences(content);

    if (dangling.Count > 0)
    {
        resolved = [.. resolved, .. dangling.Select(name => $"{name} → JsonElement")];
    }

    if (!content.StartsWith(marker, StringComparison.Ordinal))
    {
        content = marker + Environment.NewLine + content;
    }

    await File.WriteAllTextAsync(path, content);
    return resolved;
}

static async Task<int> RunAsync(string fileName, string[] arguments, string workingDirectory)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"{fileName} could not be started.");

    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
    {
        await Console.Error.WriteLineAsync(await stdout);
        await Console.Error.WriteLineAsync(await stderr);
    }

    return process.ExitCode;
}

static SyncManifest? ReadManifest(string path)
{
    if (!File.Exists(path))
    {
        return null;
    }

    try
    {
        return JsonSerializer.Deserialize<SyncManifest>(File.ReadAllText(path), SyncManifest.JsonOptions);
    }
    catch (JsonException)
    {
        // A corrupted state must not block the sync; it is simply rewritten.
        return null;
    }
}

/// <summary>Turns a service name into a valid C# identifier.</summary>
static string ToPascalCase(string value)
    => string.Concat(value
        .Split('-', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

/// <summary>Locates the repository root by looking for the solution file.</summary>
static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);

    while (directory is not null)
    {
        if (directory.GetFiles("Viu.Emporix.slnx").Length > 0)
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException(
        "Viu.Emporix.slnx not found — the tool must run inside the repository.");
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
