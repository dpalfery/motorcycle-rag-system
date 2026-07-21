using System.Text.RegularExpressions;
using SkillForge.Core.Docs.Model;
using SkillForge.Core.Parsing;

namespace SkillForge.Core.Docs.Parsing;

/// <summary>
/// Discovers and parses the in-scope documentation corpus, and reads the catalog
/// vocabularies that frontmatter values are validated against.
/// </summary>
public sealed partial class DocumentLoader
{
    /// <summary>
    /// Directory names excluded from the corpus, matched against any path segment
    /// beneath the docs root. Archived material is historical and never retrieved as
    /// current guidance.
    /// </summary>
    private static readonly string[] ExcludedSegments = ["archive", "node_modules", "obj", "bin"];

    /// <summary>
    /// Vendored upstream files that carry unrelated frontmatter of their own. These are
    /// skill documents copied from external repositories, not MotorcycleRAG documentation.
    /// </summary>
    private static readonly string[] VendoredFiles =
    [
        "DevOps/build-performance.md",
        "DevOps/directory-build-organization.md",
        "DevOps/incremental-build.md",
        "DevOps/msbuild-antipatterns.md",
        "DevOps/msbuild-modernization.md"
    ];

    private readonly string _repoRoot;
    private readonly string _docsRoot;

    public DocumentLoader(string repoRoot, string docsRelativeRoot = "6-Docs")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        _repoRoot = Path.GetFullPath(repoRoot);
        _docsRoot = Path.Combine(_repoRoot, docsRelativeRoot);
    }

    public DocumentSet Load()
    {
        var documents = new List<DocumentModel>();

        if (Directory.Exists(_docsRoot))
        {
            foreach (var file in Directory
                         .EnumerateFiles(_docsRoot, "*.md", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                var relative = ToRelative(file);
                if (IsExcluded(relative)) continue;
                documents.Add(Parse(file, relative));
            }
        }

        var (components, owners) = ReadCatalogVocabularies();

        return new DocumentSet
        {
            Documents = documents,
            Components = components,
            Owners = owners
        };
    }

    internal bool IsExcluded(string relativePath)
    {
        var segments = relativePath.Split('/');
        if (segments.Any(s => ExcludedSegments.Contains(s, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Vendored paths are recorded relative to the docs root.
        var beneathDocs = relativePath.StartsWith("6-Docs/", StringComparison.Ordinal)
            ? relativePath["6-Docs/".Length..]
            : relativePath;

        return VendoredFiles.Contains(beneathDocs, StringComparer.OrdinalIgnoreCase);
    }

    private DocumentModel Parse(string absolutePath, string relativePath)
    {
        var raw = File.ReadAllText(absolutePath);
        var read = MarkdownFrontmatterReader.Read(raw);

        if (!read.HasFrontmatter)
        {
            return new DocumentModel
            {
                RelativePath = relativePath,
                FilePath = absolutePath,
                HasFrontmatter = false,
                BodyLinks = ExtractRelativeLinks(raw)
            };
        }

        DocumentFrontmatter? frontmatter = null;
        string? parseError = null;
        try
        {
            frontmatter = MarkdownFrontmatterReader.Deserializer
                .Deserialize<DocumentFrontmatter>(read.Yaml);
        }
        catch (Exception ex)
        {
            parseError = ex.Message;
        }

        frontmatter ??= new DocumentFrontmatter();

        return new DocumentModel
        {
            RelativePath = relativePath,
            FilePath = absolutePath,
            HasFrontmatter = true,
            ParseError = parseError,
            Frontmatter = frontmatter,
            DocType = ParseDocType(frontmatter.DocType),
            Status = ParseStatus(frontmatter.Status),
            BodyLinks = ExtractRelativeLinks(read.Body)
        };
    }

    internal static DocType ParseDocType(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "architecture" => DocType.Architecture,
            "onboarding" => DocType.Onboarding,
            "requirements" => DocType.Requirements,
            "adr" => DocType.Adr,
            "plan" => DocType.Plan,
            "spec" => DocType.Spec,
            "runbook" => DocType.Runbook,
            "reference" => DocType.Reference,
            "rule" => DocType.Rule,
            "governance" => DocType.Governance,
            "index" => DocType.Index,
            _ => DocType.Unknown
        };

    internal static DocStatus ParseStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "current" => DocStatus.Current,
            "draft" => DocStatus.Draft,
            "needs-review" => DocStatus.NeedsReview,
            "superseded" => DocStatus.Superseded,
            _ => DocStatus.Unknown
        };

    private string ToRelative(string absolutePath) =>
        Path.GetRelativePath(_repoRoot, absolutePath).Replace('\\', '/');

    internal static IReadOnlyList<string> ExtractRelativeLinks(string markdown)
    {
        var links = new List<string>();
        foreach (Match match in LinkPattern().Matches(markdown))
        {
            var target = match.Groups[1].Value.Trim().Trim('<', '>');
            var hashIndex = target.IndexOf('#', StringComparison.Ordinal);
            if (hashIndex >= 0) target = target[..hashIndex];
            if (target.Length == 0) continue;
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) continue;
            if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
            if (target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
            if (target.StartsWith('/')) continue;
            links.Add(target);
        }

        return links;
    }

    /// <summary>
    /// Reads the Component and Owner columns from the catalog. The catalog is the
    /// authoritative vocabulary for both; this reader does not duplicate the values.
    /// </summary>
    private (IReadOnlySet<string> Components, IReadOnlySet<string> Owners) ReadCatalogVocabularies()
    {
        var components = new HashSet<string>(StringComparer.Ordinal);
        var owners = new HashSet<string>(StringComparer.Ordinal);

        var catalogPath = Path.Combine(_docsRoot, "catalog.md");
        if (!File.Exists(catalogPath))
        {
            return (components, owners);
        }

        foreach (var line in File.ReadLines(catalogPath))
        {
            if (!line.StartsWith('|')) continue;

            var cells = line.Split('|', StringSplitOptions.None)
                .Select(c => c.Trim())
                .ToArray();

            // | Component | Type | Source root | Overview | Detailed documentation | Owner | Last reviewed | Status |
            // Split on a leading and trailing pipe yields a leading and trailing empty cell.
            if (cells.Length < 10) continue;

            var component = cells[1];
            var owner = cells[6];

            if (component.Length == 0 || component.StartsWith("---", StringComparison.Ordinal)) continue;
            if (component.Equals("Component", StringComparison.Ordinal)) continue;

            components.Add(component);
            if (owner.Length > 0 && !owner.StartsWith("---", StringComparison.Ordinal))
            {
                owners.Add(owner);
            }
        }

        return (components, owners);
    }

    [GeneratedRegex(@"\]\(([^)]*)\)")]
    private static partial Regex LinkPattern();
}
