using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using KyberWeave.Cli.Rendering;
using KyberWeave.Core.Diagnostics;
using KyberWeave.Core.CodeGraph;
using KyberWeave.Core.Docs.Export;
using KyberWeave.Core.Docs.Model;
using KyberWeave.Core.Docs.Parsing;
using KyberWeave.Core.Docs.Validation;

namespace KyberWeave.Cli.Commands.Docs;

/// <summary>Settings common to the documentation commands.</summary>
public class DocsSettings : AnalysisSettings
{
    [CommandOption("--docs-root <DIR>")]
    [Description("Documentation root relative to the repository root. Defaults to 6-Docs.")]
    [DefaultValue("6-Docs")]
    public string DocsRoot { get; set; } = "6-Docs";
}

/// <summary>Schema tier: frontmatter conformance. Needs no code index.</summary>
public sealed class DocsValidateCommand : Command<DocsSettings>
{
    public override int Execute(CommandContext context, DocsSettings settings)
    {
        var set = new DocumentLoader(settings.Path, settings.DocsRoot).Load();
        var report = new DocSpecValidator(settings.Path).Validate(set);

        CommandHelpers.Finish(report, settings, "docs validate", "Document");
        return report.HasErrors ? 1 : 0;
    }
}

/// <summary>Entity-drift tier: resolves documented code references against CodeGraph.</summary>
public sealed class DocsDriftCommand : Command<DocsSettings>
{
    public override int Execute(CommandContext context, DocsSettings settings)
    {
        var set = new DocumentLoader(settings.Path, settings.DocsRoot).Load();

        var resolver = new CodeGraphResolver(settings.Path);
        var report = new DocDriftLinter(resolver).Validate(set);

        CommandHelpers.Finish(report, settings, "docs drift", "Document");
        return report.HasErrors ? 1 : 0;
    }
}

public sealed class DocsGraphSettings : DocsSettings
{
    [CommandOption("-o|--out <DIR>")]
    [Description("Output directory for nodes.jsonl and edges.jsonl.")]
    [DefaultValue("./build/doc-graph")]
    public string Out { get; set; } = "./build/doc-graph";
}

/// <summary>Emits the documentation graph as newline-delimited JSON.</summary>
public sealed class DocsGraphCommand : Command<DocsGraphSettings>
{
    public override int Execute(CommandContext context, DocsGraphSettings settings)
    {
        var set = new DocumentLoader(settings.Path, settings.DocsRoot).Load();

        var resolver = new CodeGraphResolver(settings.Path);
        if (!resolver.IsAvailable)
        {
            AnsiConsole.MarkupLine(
                $"[red]{Markup.Escape(resolver.UnavailableReason ?? "CodeGraph index unavailable.")}[/] " +
                "Document-to-code join edges cannot be emitted.");
            return 1;
        }

        var result = new DocGraphExporter(resolver).Export(set, settings.Out);

        AnsiConsole.MarkupLine(
            $"[green]{result.NodeCount} nodes[/] → {Markup.Escape(result.NodesPath)}");
        AnsiConsole.MarkupLine(
            $"[green]{result.EdgeCount} edges[/] → {Markup.Escape(result.EdgesPath)}");
        return 0;
    }
}

/// <summary>Governance view: doc-type coverage by component.</summary>
public sealed class DocsCatalogCommand : Command<DocsSettings>
{
    public override int Execute(CommandContext context, DocsSettings settings)
    {
        var set = new DocumentLoader(settings.Path, settings.DocsRoot).Load();

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("Component");
        table.AddColumn("Documents");
        table.AddColumn("Doc types");
        table.AddColumn("Missing frontmatter");

        foreach (var (component, documents) in set.ByComponent().OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var types = documents
                .Where(d => d.DocType != DocType.Unknown)
                .Select(d => d.DocType.ToString().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal);

            var missing = documents.Count(d => !d.HasFrontmatter);

            table.AddRow(
                new Markup(Markup.Escape(component)),
                new Markup(documents.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Markup(Markup.Escape(string.Join(", ", types))),
                new Markup(missing == 0
                    ? "[green]0[/]"
                    : $"[yellow]{missing}[/]"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"[grey]{set.Documents.Count} documents in scope; " +
            $"{set.Documents.Count(d => d.HasFrontmatter)} with frontmatter.[/]");
        return 0;
    }
}
