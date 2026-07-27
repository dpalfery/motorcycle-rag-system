using System.Globalization;
using KyberWeave.Core.Diagnostics;
using KyberWeave.Core.Docs.Model;

namespace KyberWeave.Core.Docs.Validation;

/// <summary>
/// Schema tier of documentation validation. Needs no code index, so it runs on every
/// documentation change.
/// </summary>
public sealed class DocSpecValidator
{
    public const string MissingFrontmatter = "KW-DOC-SPEC-001";
    public const string InvalidVocabulary = "KW-DOC-SPEC-002";
    public const string MissingRequiredKey = "KW-DOC-SPEC-003";
    public const string UnknownCatalogValue = "KW-DOC-SPEC-004";
    public const string SourceRootMissing = "KW-DOC-SPEC-005";
    public const string BadReference = "KW-DOC-SPEC-006";

    private readonly string _repoRoot;

    public DocSpecValidator(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        _repoRoot = Path.GetFullPath(repoRoot);
    }

    public DiagnosticReport Validate(DocumentSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        var report = new DiagnosticReport();

        var idOwners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var doc in set.Documents)
        {
            var id = doc.Frontmatter.Id;
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!idOwners.TryGetValue(id, out var paths))
            {
                paths = [];
                idOwners[id] = paths;
            }
            paths.Add(doc.RelativePath);
        }

        var knownIds = new HashSet<string>(idOwners.Keys, StringComparer.Ordinal);

        foreach (var doc in set.Documents)
        {
            ValidateDocument(doc, set, knownIds, idOwners, report);
        }

        return report;
    }

    private void ValidateDocument(
        DocumentModel doc,
        DocumentSet set,
        HashSet<string> knownIds,
        Dictionary<string, List<string>> idOwners,
        DiagnosticReport report)
    {
        if (!doc.HasFrontmatter)
        {
            report.Add(new Diagnostic(
                MissingFrontmatter, Severity.Error,
                "Document has no YAML frontmatter block.",
                doc.Subject, doc.RelativePath,
                "Add a frontmatter block per 6-Docs/documentation-ontology.md."));
            return;
        }

        if (doc.ParseError is not null)
        {
            report.Add(new Diagnostic(
                MissingFrontmatter, Severity.Error,
                $"Frontmatter block could not be parsed: {doc.ParseError}",
                doc.Subject, doc.RelativePath,
                "Check YAML indentation and that list values use '- ' items."));
            return;
        }

        var fm = doc.Frontmatter;

        // --- KW-DOC-SPEC-003: base keys -------------------------------------------------
        RequireValue(fm.Id, "id", doc, report);
        RequireValue(fm.Title, "title", doc, report);
        RequireValue(fm.Owner, "owner", doc, report);
        RequireValue(fm.LastReviewed, "last-reviewed", doc, report);

        // --- KW-DOC-SPEC-002: closed vocabularies ---------------------------------------
        if (string.IsNullOrWhiteSpace(fm.DocType))
        {
            RequireValue(fm.DocType, "doc-type", doc, report);
        }
        else if (doc.DocType == DocType.Unknown)
        {
            report.Add(new Diagnostic(
                InvalidVocabulary, Severity.Error,
                $"doc-type '{fm.DocType}' is not in the closed vocabulary.",
                doc.Subject, doc.RelativePath,
                "One of: architecture, onboarding, requirements, adr, plan, spec, runbook, reference, rule, governance, index."));
        }

        if (string.IsNullOrWhiteSpace(fm.Status))
        {
            RequireValue(fm.Status, "status", doc, report);
        }
        else if (doc.Status == DocStatus.Unknown)
        {
            report.Add(new Diagnostic(
                InvalidVocabulary, Severity.Error,
                $"status '{fm.Status}' is not in the closed vocabulary.",
                doc.Subject, doc.RelativePath,
                "One of: current, draft, needs-review, superseded."));
        }

        if (!string.IsNullOrWhiteSpace(fm.LastReviewed) &&
            !DateOnly.TryParseExact(fm.LastReviewed, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
        {
            report.Add(new Diagnostic(
                InvalidVocabulary, Severity.Error,
                $"last-reviewed '{fm.LastReviewed}' is not an ISO yyyy-MM-dd date.",
                doc.Subject, doc.RelativePath));
        }

        // --- KW-DOC-SPEC-003: per-doc-type requirements ---------------------------------
        ValidateRequiredForType(doc, report);

        // --- KW-DOC-SPEC-004: catalog vocabularies --------------------------------------
        if (!string.IsNullOrWhiteSpace(fm.Component) &&
            set.Components.Count > 0 &&
            !set.Components.Contains(fm.Component))
        {
            report.Add(new Diagnostic(
                UnknownCatalogValue, Severity.Error,
                $"component '{fm.Component}' is not a Component in the catalog.",
                doc.Subject, doc.RelativePath,
                Nearest(fm.Component, set.Components) is { } nearC
                    ? $"Closest catalog component: '{nearC}'."
                    : "Add a catalog row before referencing a new component."));
        }

        if (!string.IsNullOrWhiteSpace(fm.Owner) &&
            set.Owners.Count > 0 &&
            !set.Owners.Contains(fm.Owner))
        {
            report.Add(new Diagnostic(
                UnknownCatalogValue, Severity.Error,
                $"owner '{fm.Owner}' is not an Owner in the catalog.",
                doc.Subject, doc.RelativePath,
                Nearest(fm.Owner, set.Owners) is { } nearO
                    ? $"Closest catalog owner: '{nearO}'."
                    : null));
        }

        // --- KW-DOC-SPEC-005: source-root exists ----------------------------------------
        if (!string.IsNullOrWhiteSpace(fm.SourceRoot))
        {
            var full = Path.Combine(_repoRoot, fm.SourceRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full) && !File.Exists(full))
            {
                report.Add(new Diagnostic(
                    SourceRootMissing, Severity.Error,
                    $"source-root '{fm.SourceRoot}' does not exist.",
                    doc.Subject, doc.RelativePath,
                    "Use a repository-relative path to the component's source root."));
            }
        }

        // --- KW-DOC-SPEC-006: identity and cross-references ------------------------------
        if (!string.IsNullOrWhiteSpace(fm.Id) &&
            idOwners.TryGetValue(fm.Id, out var owners) &&
            owners.Count > 1)
        {
            report.Add(new Diagnostic(
                BadReference, Severity.Error,
                $"id '{fm.Id}' is declared by {owners.Count} documents: {string.Join(", ", owners)}.",
                doc.Subject, doc.RelativePath,
                "Document ids are unique and permanent."));
        }

        foreach (var reference in doc.DecidedBy)
        {
            if (!knownIds.Contains(reference))
            {
                report.Add(new Diagnostic(
                    BadReference, Severity.Error,
                    $"decided-by references unknown document id '{reference}'.",
                    doc.Subject, doc.RelativePath,
                    Nearest(reference, knownIds) is { } near ? $"Closest known id: '{near}'." : null));
            }
        }

        foreach (var reference in doc.Supersedes)
        {
            if (!knownIds.Contains(reference))
            {
                report.Add(new Diagnostic(
                    BadReference, Severity.Error,
                    $"supersedes references unknown document id '{reference}'.",
                    doc.Subject, doc.RelativePath,
                    Nearest(reference, knownIds) is { } near ? $"Closest known id: '{near}'." : null));
            }
        }
    }

    private static void ValidateRequiredForType(DocumentModel doc, DiagnosticReport report)
    {
        var fm = doc.Frontmatter;

        switch (doc.DocType)
        {
            case DocType.Architecture:
                RequireValue(fm.Component, "component", doc, report);
                // source-root and code-refs are a pair: naming a source root without
                // naming the symbols leaves the document unreachable from the code graph,
                // and naming symbols without a source root leaves them unanchored. A
                // system-level overview that describes no single component sets neither,
                // and is reached through its component edges instead.
                if (!string.IsNullOrWhiteSpace(fm.SourceRoot))
                {
                    RequireList(doc.CodeRefs, "code-refs", doc, report,
                        "An architecture document with a source-root must name the symbols it describes.");
                }
                else if (doc.CodeRefs.Count > 0)
                {
                    RequireValue(fm.SourceRoot, "source-root", doc, report);
                }
                break;

            case DocType.Onboarding:
                RequireValue(fm.Component, "component", doc, report);
                RequireValue(fm.SourceRoot, "source-root", doc, report);
                break;

            case DocType.Requirements:
                RequireValue(fm.Component, "component", doc, report);
                break;

            case DocType.Runbook:
                RequireValue(fm.Component, "component", doc, report);
                // A runbook that operates a component's code must name the symbols it
                // operates; a process-only runbook sets neither source-root nor code-refs.
                if (!string.IsNullOrWhiteSpace(fm.SourceRoot))
                {
                    RequireList(doc.CodeRefs, "code-refs", doc, report,
                        "A runbook with a source-root must name the symbols it operates, or drop source-root if it is process-only.");
                }
                break;

            case DocType.Plan:
            case DocType.Spec:
                RequireValue(fm.Component, "component", doc, report);
                break;

            case DocType.Adr:
            case DocType.Reference:
            case DocType.Rule:
            case DocType.Governance:
            case DocType.Index:
            case DocType.Unknown:
            default:
                break;
        }
    }

    private static void RequireValue(string? value, string key, DocumentModel doc, DiagnosticReport report)
    {
        if (!string.IsNullOrWhiteSpace(value)) return;
        report.Add(new Diagnostic(
            MissingRequiredKey, Severity.Error,
            $"Required key '{key}' is missing or empty.",
            doc.Subject, doc.RelativePath));
    }

    private static void RequireList(
        IReadOnlyList<string> values, string key, DocumentModel doc, DiagnosticReport report, string? hint = null)
    {
        if (values.Count > 0) return;
        report.Add(new Diagnostic(
            MissingRequiredKey, Severity.Error,
            $"Required key '{key}' must have at least one entry for doc-type '{doc.Frontmatter.DocType}'.",
            doc.Subject, doc.RelativePath, hint));
    }

    /// <summary>Closest candidate by edit distance, when one is plausibly a typo.</summary>
    internal static string? Nearest(string value, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var distance = Levenshtein(value, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best is null) return null;
        var threshold = Math.Max(3, value.Length / 2);
        return bestDistance <= threshold ? best : null;
    }

    internal static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
