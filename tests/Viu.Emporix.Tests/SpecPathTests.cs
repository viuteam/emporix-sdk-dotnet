using System.Text.RegularExpressions;

namespace Viu.Emporix.Tests;

/// <summary>
/// Checks that every call the hand-written services build — method and address
/// together — actually exists in the Emporix specifications.
/// </summary>
/// <remarks>
/// <para>
/// This exists because three services shipped pointing at paths the API does not
/// have — a checkout at <c>/checkout/{tenant}/order</c> instead of
/// <c>/checkout/{tenant}/checkouts/order</c>, orders under <c>/order/</c> instead
/// of <c>/order-v2/</c>, and coupons under a <c>/coupons/</c> segment that was
/// never there, plus two calls sent as PUT to endpoints that only route PATCH.
/// Every one of them compiled, and their tests passed, because the tests
/// asserted the same wrong call the code built.
/// </para>
/// <para>
/// The specifications are the only source that can tell the difference, so the
/// check reads them. It scans the service sources rather than calling the
/// methods: a wrong path is a property of the source text, and 130-odd methods
/// would otherwise each need a stub and a fixture.
/// </para>
/// </remarks>
public class SpecPathTests
{
    [Fact]
    public void Every_call_a_service_builds_exists_in_a_specification()
    {
        DirectoryInfo root = FindRepositoryRoot();
        HashSet<string> declared = ReadSpecificationPaths(root);
        List<(string Call, string File)> used = ReadServicePaths(root);

        // A guard on the guard: if the scan finds nothing, it is broken rather
        // than the code being clean.
        Assert.True(declared.Count > 300, $"Only {declared.Count} specification paths found.");
        Assert.True(used.Count > 60, $"Only {used.Count} service paths found.");

        string[] unknown = [.. used
            .Where(u => !Known(declared, u.Call))
            .Select(u => $"{u.Call}  ({u.File})")
            .Distinct()
            .Order()];

        Assert.Empty(unknown);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "specs")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory;
    }

    /// <summary>
    /// Collects every path the specifications declare, prefixed by the segment
    /// their server URL carries.
    /// </summary>
    /// <remarks>
    /// Some specifications put the service name in the server URL
    /// (<c>https://api.emporix.io/label</c> plus <c>/labels</c>) and others in
    /// the path itself. Both end up as the same address, so both are folded in
    /// here.
    /// </remarks>
    private static HashSet<string> ReadSpecificationPaths(DirectoryInfo root)
    {
        HashSet<string> calls = [];

        foreach (string file in Directory.EnumerateFiles(Path.Combine(root.FullName, "specs"), "*.yml"))
        {
            string prefix = string.Empty;
            string? current = null;
            bool inServers = false;

            foreach (string line in File.ReadLines(file))
            {
                if (line.StartsWith("servers:", StringComparison.Ordinal))
                {
                    inServers = true;
                    continue;
                }

                if (inServers)
                {
                    Match server = Regex.Match(line, @"url:\s*'?https://[^/']+(/[A-Za-z0-9-]*)?'?");
                    if (server.Success)
                    {
                        prefix = server.Groups[1].Value.TrimEnd('/');
                        inServers = false;
                    }
                    else if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('-'))
                    {
                        inServers = false;
                    }
                }

                Match path = Regex.Match(line, @"^  '?(/[A-Za-z0-9{}/_.-]*)'?:\s*$");
                if (path.Success)
                {
                    current = Normalise(prefix + path.Groups[1].Value);
                    continue;
                }

                Match verb = Regex.Match(line, @"^    (get|post|put|patch|delete):\s*$");
                if (verb.Success && current is not null)
                {
                    calls.Add($"{verb.Groups[1].Value.ToUpperInvariant()} {current}");
                }
            }
        }

        return calls;
    }

    /// <summary>
    /// Collects every path the hand-written services build, resolving the
    /// <c>BasePath</c> each class defines.
    /// </summary>
    private static List<(string Call, string File)> ReadServicePaths(DirectoryInfo root)
    {
        List<(string, string)> used = [];

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root.FullName, "src", "Viu.Emporix"),
            "*.cs"))
        {
            string text = File.ReadAllText(file);
            string name = Path.GetFileName(file);

            // A file may hold several services, so each BasePath applies from
            // where it is declared until the next one.
            List<(int Offset, string Value)> bases =
            [
                .. Regex.Matches(text, @"private string BasePath => \$""([^""]+)""")
                    .Select(m => (m.Index, m.Groups[1].Value)),
            ];

            foreach (Match match in Regex.Matches(
                text,
                @"Method = (?:HttpMethod\.(\w+)|(\w+))[^;]*?Path = ((?:\$?""[^""]*""|\s*\+\s*)+)",
                RegexOptions.Singleline))
            {
                // A method held in a variable cannot be resolved statically, so
                // those calls are checked on the path alone.
                string verb = match.Groups[1].Success
                    ? match.Groups[1].Value.ToUpperInvariant()
                    : "*";

                string built = string.Concat(
                    Regex.Matches(match.Groups[3].Value, @"""([^""]*)""").Select(m => m.Groups[1].Value));

                string? basePath = bases
                    .Where(b => b.Offset < match.Index)
                    .Select(b => b.Value)
                    .LastOrDefault();

                if (basePath is not null)
                {
                    built = built.Replace("{BasePath}", basePath, StringComparison.Ordinal);
                }

                if (built.StartsWith('/'))
                {
                    used.Add(($"{verb} {Normalise(built)}", name));
                }
            }
        }

        return used;
    }

    /// <summary>
    /// Whether a specification declares this call. A wildcard verb matches any
    /// method on the same path.
    /// </summary>
    private static bool Known(HashSet<string> declared, string call)
        => call.StartsWith("* ", StringComparison.Ordinal)
            ? declared.Any(d => d.EndsWith(call[1..], StringComparison.Ordinal))
            : declared.Contains(call);

    /// <summary>Reduces a path to its shape: parameter names do not matter.</summary>
    private static string Normalise(string path)
        => Regex.Replace(Regex.Replace(path, @"\{[^}]*\}", "{}"), "/+", "/");
}
