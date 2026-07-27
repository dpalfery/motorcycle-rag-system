using System.Diagnostics;
using System.Globalization;

namespace KyberWeave.Core.CodeGraph;

/// <summary>One resolved code symbol from the CodeGraph index.</summary>
/// <param name="Language">Indexed language, e.g. <c>csharp</c> or <c>typescript</c>. A bare
/// symbol name can collide across languages, so callers disambiguating a match need it.</param>
/// <param name="StartLine">1-based line the symbol is declared on.</param>
public sealed record CodeGraphNode(
    string Id,
    string Kind,
    string Name,
    string QualifiedName,
    string FilePath,
    string Language,
    int StartLine)
{
    /// <summary>The symbol's location as <c>file:line</c>.</summary>
    public string Location => StartLine > 0 ? $"{FilePath}:{StartLine}" : FilePath;
}

/// <summary>
/// Reads <c>.codegraph/codegraph.db</c> and answers symbol and route lookups.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of resolving against the index rather than grepping source is that
/// CodeGraph canonicalizes what the source text does not express. An ASP.NET route
/// composed from <c>[Route("api/me")]</c> and <c>[HttpGet("usage")]</c> is indexed as
/// <c>GET /api/me/usage</c> — a string that appears nowhere in the file.
/// </para>
/// <para>
/// The index is read through one batched invocation of the <c>sqlite3</c> CLI rather
/// than the <c>Microsoft.Data.Sqlite</c> package. That package's native dependency,
/// <c>SQLitePCLRaw.lib.e_sqlite3</c>, carries advisory GHSA-2m69-gcr7-jv3q at every
/// published version with no patched release available, and this repository runs
/// blocking dependency scanning. One subprocess call loads the whole node table into
/// memory, after which every lookup is in-process — so this is also faster than
/// per-symbol querying would have been.
/// </para>
/// </remarks>
public sealed class CodeGraphResolver
{
    private const char FieldSeparator = '';

    private static readonly string[] SymbolKinds =
        ["class", "interface", "method", "function", "struct", "enum", "type_alias"];

    private readonly Dictionary<string, List<CodeGraphNode>> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CodeGraphNode>> _byQualifiedName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CodeGraphNode> _routes = new(StringComparer.Ordinal);
    private readonly List<string> _filePaths = [];

    /// <summary>True when an index was found and read.</summary>
    public bool IsAvailable { get; }

    /// <summary>Why the index could not be read, when <see cref="IsAvailable"/> is false.</summary>
    public string? UnavailableReason { get; }

    /// <summary>Where the index was looked for, for diagnostics.</summary>
    public string DatabasePath { get; }

    public CodeGraphResolver(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        DatabasePath = Path.Combine(Path.GetFullPath(repoRoot), ".codegraph", "codegraph.db");

        if (!File.Exists(DatabasePath))
        {
            UnavailableReason = $"No CodeGraph index at {DatabasePath}.";
            return;
        }

        try
        {
            Load();
            IsAvailable = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            UnavailableReason = ex.Message;
        }
    }

    private void Load()
    {
        // One pass over the node table. 'import' rows are excluded: they are module
        // references, never a documentable symbol.
        const string sql = """
            SELECT id, kind, name, qualified_name, file_path, language, start_line
            FROM nodes
            WHERE kind <> 'import';
            """;

        foreach (var line in RunSqlite(sql))
        {
            var parts = line.Split(FieldSeparator);
            if (parts.Length < 7) continue;

            _ = int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startLine);
            var node = new CodeGraphNode(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], startLine);

            Index(_byName, node.Name, node);
            Index(_byQualifiedName, node.QualifiedName, node);

            if (node.Kind == "route")
            {
                _routes.TryAdd(node.Name, node);
            }

            if (node.FilePath.Length > 0)
            {
                _filePaths.Add(node.FilePath);
            }
        }
    }

    private static void Index(Dictionary<string, List<CodeGraphNode>> map, string key, CodeGraphNode node)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }
        list.Add(node);
    }

    private IEnumerable<string> RunSqlite(string sql)
    {
        var startInfo = new ProcessStartInfo("sqlite3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // Arguments are passed as a list, never through a shell, so the database path
        // cannot be interpreted as anything but a path.
        //
        // The connection is deliberately not opened with '-readonly'. The CodeGraph
        // daemon leaves the index in WAL journal mode, and opening a WAL database
        // requires a shared-memory (-shm) file; '-readonly' forbids creating one, so it
        // fails outright with "unable to open database file" whenever no other connection
        // is already holding the index open. Only SELECT statements are ever issued.
        startInfo.ArgumentList.Add("-noheader");
        startInfo.ArgumentList.Add("-separator");
        startInfo.ArgumentList.Add(FieldSeparator.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(DatabasePath);
        startInfo.ArgumentList.Add(sql);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the 'sqlite3' process.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Reading the CodeGraph index with 'sqlite3' failed (exit {process.ExitCode}): {error.Trim()}");
        }

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Resolves a bare symbol name, or a fully qualified name, to every match.</summary>
    public IReadOnlyList<CodeGraphNode> ResolveSymbol(string name)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(name)) return [];

        if (_byName.TryGetValue(name, out var byName)) return byName;
        if (_byQualifiedName.TryGetValue(name, out var byQualified)) return byQualified;
        return [];
    }

    /// <summary>Resolves an exact <c>METHOD /path</c> route string.</summary>
    public IReadOnlyList<CodeGraphNode> ResolveRoute(string route)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(route)) return [];
        return _routes.TryGetValue(route, out var node) ? [node] : [];
    }

    /// <summary>True when the index contains at least one file beneath the given path prefix.</summary>
    public bool HasFilesUnder(string relativePathPrefix)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(relativePathPrefix)) return false;

        var normalized = relativePathPrefix.Replace('\\', '/').TrimEnd('/');

        // "." is the repository root: a system-level document legitimately describes the
        // whole tree, so every indexed file is beneath it.
        if (normalized is "." or "") return _filePaths.Count > 0;

        return _filePaths.Exists(p => p.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Candidate symbol names for a "did you mean" hint after a failed resolve.</summary>
    public IReadOnlyList<string> CandidateNames(string like)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(like)) return [];

        // Anchor on a short prefix so the candidate pool stays small; a rename usually
        // preserves a prefix or a suffix, not neither.
        var prefix = like.Length <= 4 ? like : like[..4];

        return _byName
            .Where(kv => kv.Value.Exists(n => SymbolKinds.Contains(n.Kind, StringComparer.Ordinal)))
            .Select(kv => kv.Key)
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(200)
            .ToList();
    }

    /// <summary>All indexed route strings, for a "did you mean" hint.</summary>
    public IReadOnlyList<string> AllRoutes() => IsAvailable ? _routes.Keys.ToList() : [];
}
