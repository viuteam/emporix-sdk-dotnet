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

static Task<int> PullAsync(MixinConfig config, string configPath)
    => throw new NotImplementedException("Task 10 implements pull.");

static int Generate(MixinConfig config, string configPath)
    => throw new NotImplementedException("Task 11 implements generate.");

static Task<int> CheckAsync(MixinConfig config, string configPath)
    => throw new NotImplementedException("Task 12 implements check.");
