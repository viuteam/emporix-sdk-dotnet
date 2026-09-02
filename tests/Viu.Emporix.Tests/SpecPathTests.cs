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
        List<(string Call, string File)> used = ReadServicePaths(root, out _);

        // A guard on the guard: if the scan finds nothing, it is broken rather
        // than the code being clean.
        Assert.True(declared.Count > 300, $"Only {declared.Count} specification paths found.");
        Assert.True(used.Count > 60, $"Only {used.Count} service paths found.");

        string[] unknown = [.. used
            .Where(u => !Exempt(u.Call) && !Known(declared, u.Call))
            .Select(u => $"{u.Call}  ({u.File})")
            .Distinct()
            .Order()];

        Assert.Empty(unknown);
    }

    /// <summary>
    /// The one address no specification can confirm.
    /// </summary>
    /// <remarks>
    /// Emporix vendors no specification for cloud functions — the whole point of
    /// the service is that the shapes belong to whoever deployed the function.
    /// The address is verified against the Node SDK instead
    /// (<c>packages/sdk/src/services/cloud-functions.ts</c>), and the decision is
    /// recorded in ADR-0009. It is listed here rather than skipped silently so
    /// that a second exemption has to be argued for.
    /// </remarks>
    private static bool Exempt(string call)
        => call.EndsWith("/cloud-functions/{}/functions/{}{}", StringComparison.Ordinal);

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
    /// Collects every path the hand-written services build.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <param name="unresolved">
    /// The calls whose path could not be worked out. Reported rather than
    /// dropped: a path this scanner cannot see is a path neither direction of
    /// the check covers, and a silent blind spot is worse than a small one that
    /// is counted.
    /// </param>
    /// <remarks>
    /// A path is written in one of four shapes, and all four are resolved here.
    /// The scanner used to understand only the first, which left 212 of 639
    /// calls unchecked without saying so.
    /// <list type="bullet">
    /// <item>an interpolated literal, <c>$"{BasePath}/configs"</c>;</item>
    /// <item>the base path alone, <c>Path = BasePath</c>;</item>
    /// <item>a nested class's base path, <c>_basePath</c>, which comes from the
    /// parent at construction — and one class may be constructed from several
    /// places with different bases, as the fee attachments are;</item>
    /// <item>a private helper returning a path, which may return more than one
    /// shape depending on its arguments.</item>
    /// </list>
    /// </remarks>
    private static List<(string Call, string File)> ReadServicePaths(
        DirectoryInfo root,
        out List<string> unresolved)
    {
        List<(string, string)> used = [];
        unresolved = [];

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root.FullName, "src", "Viu.Emporix"),
            "*.cs"))
        {
            string text = WithoutComments(File.ReadAllText(file));
            string name = Path.GetFileName(file);

            // A file may hold several services, so each base applies from where
            // it is declared until the next one. Two declaration forms: an
            // expression body with the tenant in it, and a const for the two
            // services whose path carries no tenant.
            List<(int Offset, string Value)> bases =
            [
                .. Regex.Matches(text, @"string BasePath\s*=>\s*\$""([^""]+)""")
                    .Select(m => (m.Index, m.Groups[1].Value)),
                .. Regex.Matches(text, @"const string BasePath\s*=\s*""([^""]+)""")
                    .Select(m => (m.Index, m.Groups[1].Value)),
            ];
            bases.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            Dictionary<string, List<string>> constructions = [];
            foreach (Match m in Regex.Matches(
                text, @"new (\w+Operations)\(\s*_http,\s*\$""([^""]+)""", RegexOptions.Singleline))
            {
                if (!constructions.TryGetValue(m.Groups[1].Value, out List<string>? sites))
                {
                    constructions[m.Groups[1].Value] = sites = [];
                }

                sites.Add(m.Groups[2].Value);
            }

            Dictionary<string, List<string>> helpers = [];
            foreach (Match m in Regex.Matches(
                text, @"private string (\w+)\([^)]*\)\s*(=>[\s\S]*?;|\{[\s\S]*?\n    \})"))
            {
                List<string> paths =
                    [.. Regex.Matches(m.Groups[2].Value, @"\$""([^""]+)""").Select(x => x.Groups[1].Value)];

                if (paths.Count > 0)
                {
                    helpers[m.Groups[1].Value] = paths;
                }
            }

            List<(int Offset, string Name)> classes =
            [
                .. Regex.Matches(text, @"\n(?:public|internal) sealed class (\w+)")
                    .Select(m => (m.Index, m.Groups[1].Value)),
            ];

            foreach (Match match in Regex.Matches(
                text,
                @"Method = (?:HttpMethod\.(\w+)|(\w+))[^;]*?Path =\s*"
                + @"((?:\$?""[^""]*""|\s*\+\s*|[\w.]+\([^)]*\)|[\w.]+)+)",
                RegexOptions.Singleline))
            {
                // A method held in a variable cannot be resolved statically, so
                // those calls are checked on the path alone.
                string verb = match.Groups[1].Success
                    ? match.Groups[1].Value.ToUpperInvariant()
                    : "*";

                string expression = match.Groups[3].Value.Trim().TrimEnd(',');
                string? basePath = bases
                    .Where(b => b.Offset < match.Index)
                    .Select(b => b.Value)
                    .LastOrDefault();
                string? owner = classes
                    .Where(c => c.Offset < match.Index)
                    .Select(c => c.Name)
                    .LastOrDefault();

                List<string> candidates = Candidates(expression, helpers);

                if (candidates.Count == 0)
                {
                    unresolved.Add($"{name}: {verb} {expression}");
                    continue;
                }

                if (owner is not null
                    && constructions.TryGetValue(owner, out List<string>? sites)
                    && sites.Count > 1)
                {
                    candidates =
                    [
                        .. candidates.SelectMany(
                            c => sites.Select(s => c.Replace("{_basePath}", s, StringComparison.Ordinal))),
                    ];
                }

                List<string> resolved = [.. candidates.Select(
                    c => Resolve(c, basePath, owner, constructions, helpers))];

                if (resolved.All(r => r.StartsWith('/')))
                {
                    used.AddRange(resolved.Select(r => ($"{verb} {Normalise(r)}", name)));
                }
                else
                {
                    unresolved.Add($"{name}: {verb} {expression}");
                }
            }
        }

        return used;
    }

    /// <summary>
    /// Strips line comments, so prose about the code is not read as code.
    /// </summary>
    /// <remarks>
    /// Found the hard way: a comment in AvailabilityService explaining a fixed
    /// path defect quoted the assignment it was about, and the scanner read the
    /// quotation as a real call — reporting the very defect the comment says was
    /// fixed. Anything that scans source has to ignore what is not source.
    /// </remarks>
    private static string WithoutComments(string source)
        => Regex.Replace(source, @"^[ \t]*//.*$", string.Empty, RegexOptions.Multiline);

    /// <summary>The path shapes one assignment can produce, before substitution.</summary>
    private static List<string> Candidates(string expression, Dictionary<string, List<string>> helpers)
    {
        if (expression.Contains('"', StringComparison.Ordinal))
        {
            return
            [
                string.Concat(Regex.Matches(expression, @"""([^""]*)""").Select(m => m.Groups[1].Value)),
            ];
        }

        if (expression == "BasePath")
        {
            return ["{BasePath}"];
        }

        if (expression is "_basePath" or "basePath")
        {
            return ["{_basePath}"];
        }

        Match helper = Regex.Match(expression, @"^(\w+)\(");

        return helper.Success && helpers.TryGetValue(helper.Groups[1].Value, out List<string>? paths)
            ? [.. paths]
            : [];
    }

    /// <summary>
    /// Substitutes the placeholders until none are left.
    /// </summary>
    /// <remarks>
    /// Repeated on purpose: a nested class's base path is itself written in
    /// terms of its parent's, so one pass is not enough.
    /// </remarks>
    private static string Resolve(
        string path,
        string? basePath,
        string? owner,
        Dictionary<string, List<string>> constructions,
        Dictionary<string, List<string>> helpers)
    {
        for (int pass = 0; pass < 5; pass++)
        {
            if (owner is not null && constructions.TryGetValue(owner, out List<string>? sites))
            {
                path = path.Replace("{_basePath}", sites[0], StringComparison.Ordinal);
            }

            foreach ((string helper, List<string> paths) in helpers)
            {
                path = Regex.Replace(path, @"\{" + helper + @"\([^{}]*\)\}", paths[0].Replace("$", "$$"));
            }

            if (basePath is not null)
            {
                path = path.Replace("{BasePath}", basePath, StringComparison.Ordinal);
            }

            if (!path.Contains("{BasePath}", StringComparison.Ordinal)
                && !path.Contains("{_basePath}", StringComparison.Ordinal))
            {
                break;
            }
        }

        return path;
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

    /// <summary>
    /// Checks the other direction: every operation a specification declares
    /// should be reachable through a facade.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check above catches a call the API does not have. This one catches
    /// the opposite — an operation the API has and the SDK does not — which is
    /// what a specification sync quietly introduces. The upstream import
    /// service gained a <c>DELETE</c> on its schedule, and nothing said so; it
    /// was found by hand.
    /// </para>
    /// <para>
    /// The gaps are pinned rather than merely counted, so both directions fail
    /// loudly: a new one appears when upstream adds an operation, and a closed
    /// one appears when someone implements it and forgets this list.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_operation_a_specification_declares_has_a_facade()
    {
        DirectoryInfo root = FindRepositoryRoot();
        HashSet<string> declared = ReadSpecificationPaths(root);
        List<(string Call, string File)> used = ReadServicePaths(root, out _);

        HashSet<string> covered = [.. used.Select(u => u.Call)];
        string[] wildcards = [.. used.Where(u => u.Call.StartsWith("* ", StringComparison.Ordinal))
            .Select(u => u.Call[1..])];

        string[] uncovered = [.. declared
            .Where(d => !covered.Contains(d)
                && !wildcards.Any(w => d.EndsWith(w, StringComparison.Ordinal))
                && !ImplementedWithoutAFacade(d)
                && !Superseded(d))
            .Order()];

        Assert.Equal(KnownGaps, uncovered);
    }

    /// <summary>
    /// Operations the SDK reaches, but not through a service facade.
    /// </summary>
    /// <remarks>
    /// The token endpoints belong to <c>DefaultTokenProvider</c> and the
    /// customer session endpoints to a private helper in
    /// <c>CustomerService</c> that takes its path as an argument. Both are
    /// covered by their own tests; neither is a gap.
    /// </remarks>
    private static bool ImplementedWithoutAFacade(string call) =>
        call is "POST /oauth/token"
            or "GET /customerlogin/auth/anonymous/login"
            or "GET /customerlogin/auth/anonymous/refresh"
            or "POST /customer/{}/login"
            or "POST /customer/{}/socialLogin"
            or "POST /customer/{}/exchangeauthtoken"
            or "GET /customer/{}/refreshauthtoken";

    /// <summary>
    /// Operations the specification itself marks as superseded.
    /// </summary>
    /// <remarks>
    /// <c>categoryTree</c> under <c>/categories</c> carries
    /// <c>deprecated: true</c>; the SDK uses <c>/category-trees</c>, which is
    /// the endpoint that replaced it.
    /// </remarks>
    private static bool Superseded(string call) =>
        call is "GET /category/{}/categories/categoryTree";

    /// <summary>
    /// Operations Emporix offers and this SDK does not implement yet.
    /// </summary>
    /// <remarks>
    /// Availability has two halves. The stock records are covered; the
    /// locations a site ships from are not, and neither is the search across
    /// them. Nothing depends on them today, and pinning them here is the
    /// difference between a gap someone chose and a gap nobody noticed.
    /// </remarks>
    private static readonly string[] KnownGaps =
    [
        "DELETE /availability/{}/locations/{}",
        "GET /availability/{}/locations/{}",
        "POST /availability/{}/locations/{}",
        "POST /availability/{}/search/locations",
        "PUT /availability/{}/locations/{}",
    ];

    /// <summary>
    /// Guards the scanner itself: a path it cannot read is checked by neither
    /// direction.
    /// </summary>
    /// <remarks>
    /// One call is unreadable, and deliberately so: <c>CustomerService</c>
    /// funnels its four session endpoints through a private helper that takes
    /// the path as an argument. Those four are listed in
    /// <see cref="ImplementedWithoutAFacade"/>. Anything else appearing here
    /// means a new way of writing a path has crept in, and with it a blind
    /// spot — which is how 212 of 639 calls once went unchecked in silence.
    /// </remarks>
    [Fact]
    public void The_scanner_reads_every_path_but_one()
    {
        ReadServicePaths(FindRepositoryRoot(), out List<string> unresolved);

        Assert.Equal(["CustomerService.cs: POST path"], unresolved);
    }
}
