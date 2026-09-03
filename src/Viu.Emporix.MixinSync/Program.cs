using System.Text.Json;
using Viu.Emporix;
using Viu.Emporix.MixinSync;

// Generates typed C# for an Emporix tenant's mixins and detects schema drift.
//
//   emporix-mixins pull       read the Schema Service, write snapshot and lock
//   emporix-mixins generate   turn the snapshot into C#
//   emporix-mixins check      compare the tenant against the lock, for CI

string command = args.Length > 0 ? args[0] : "help";
string configPath = args.Length > 1
    ? args[1]
    : Path.Combine(Directory.GetCurrentDirectory(), "emporix-mixins.json");

try
{
    return command switch
    {
        "pull" => await PullAsync(MixinConfig.Load(configPath), configPath),
        "generate" => Generate(MixinConfig.Load(configPath), configPath),
        "check" => await CheckAsync(MixinConfig.Load(configPath), configPath),
        "help" or "--help" or "-h" => Usage(0),
        _ => Usage(2, $"Unknown command \"{command}\"."),
    };
}
catch (Exception error) when (error is FileNotFoundException or ArgumentException or InvalidOperationException)
{
    // The expected failures — a missing config, a missing value, a collision in
    // the generated names. A stack trace would bury the message that matters.
    Console.Error.WriteLine($"emporix-mixins: {error.Message}");
    return 1;
}

static int Usage(int code, string? problem = null)
{
    if (problem is not null)
    {
        Console.Error.WriteLine($"emporix-mixins: {problem}");
    }

    Console.WriteLine("""
        usage: emporix-mixins <pull|generate|check> [config]

          pull      Read the tenant's Schema Service, write the snapshot and the lockfile.
          generate  Turn the snapshot into C# types, contexts and a registry.
          check     Compare the tenant against the lockfile. Exits 1 on drift.

        The config defaults to ./emporix-mixins.json. Credentials come from
        EMPORIX_BACKEND_CLIENT_ID and EMPORIX_BACKEND_SECRET.
        """);

    return code;
}

static async Task<int> PullAsync(MixinConfig config, string configPath)
{
    string root = RootOf(configPath);

    using EmporixClient client = ClientFor(config);
    using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    IReadOnlyList<RawMixin> mixins = await new SchemaSource(client, http).ListAsync();

    string lockPath = Path.Combine(root, config.LockFile);
    string snapshotPath = SnapshotPathFor(lockPath, root);

    Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
    File.WriteAllText(snapshotPath, JsonSerializer.Serialize(mixins, MixinJson.Options));
    Lockfile.Write(lockPath, Lockfile.Build(mixins, DateTimeOffset.UtcNow));

    Console.WriteLine($"Pulled {mixins.Count} mixins into {snapshotPath} and {lockPath}.");

    return 0;
}

/// The directory the config sits in; every configured path is relative to it.
static string RootOf(string configPath)
    => Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

/// The snapshot lives beside the lockfile: the two are written together and
/// read together, and keeping them adjacent makes that visible in a review.
static string SnapshotPathFor(string lockPath, string root)
    => Path.Combine(Path.GetDirectoryName(lockPath) ?? root, "mixins.snapshot.json");

static EmporixClient ClientFor(MixinConfig config)
{
    string? clientId = Environment.GetEnvironmentVariable("EMPORIX_BACKEND_CLIENT_ID");
    string? secret = Environment.GetEnvironmentVariable("EMPORIX_BACKEND_SECRET");

    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
    {
        throw new InvalidOperationException(
            "Set EMPORIX_BACKEND_CLIENT_ID and EMPORIX_BACKEND_SECRET. The Schema Service is seller-side.");
    }

    EmporixOptions options = new() { Tenant = config.Tenant };
    options.Credentials.Backend = new EmporixServiceCredentials { ClientId = clientId, Secret = secret };

    return new EmporixClient(options);
}

static int Generate(MixinConfig config, string configPath)
{
    string root = RootOf(configPath);
    string lockPath = Path.Combine(root, config.LockFile);
    string snapshotPath = SnapshotPathFor(lockPath, root);

    if (!File.Exists(snapshotPath))
    {
        throw new FileNotFoundException($"No snapshot at {snapshotPath}. Run pull first.", snapshotPath);
    }

    List<RawMixin> mixins = JsonSerializer.Deserialize<List<RawMixin>>(
        File.ReadAllText(snapshotPath), MixinJson.Options) ?? [];

    string outputDirectory = Path.Combine(root, config.Out);
    Directory.CreateDirectory(outputDirectory);

    // A mixin removed from the tenant must not leave an orphaned file behind —
    // the same reasoning SpecSync applies to its generated specifications.
    foreach (string stale in Directory.GetFiles(outputDirectory, "*.g.cs"))
    {
        File.Delete(stale);
    }

    IReadOnlyDictionary<string, string> files = Generator.Generate(mixins, config.Namespace);

    foreach ((string name, string content) in files)
    {
        File.WriteAllText(Path.Combine(outputDirectory, name), content);
    }

    Console.WriteLine($"Generated {files.Count} files for {mixins.Count} mixins into {outputDirectory}.");

    return 0;
}

static Task<int> CheckAsync(MixinConfig config, string configPath)
    => throw new NotImplementedException("Task 12 implements check.");
