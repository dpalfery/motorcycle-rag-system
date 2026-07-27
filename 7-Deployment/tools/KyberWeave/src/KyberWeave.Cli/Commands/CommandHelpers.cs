using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using KyberWeave.Cli.Rendering;
using KyberWeave.Core.Diagnostics;
using KyberWeave.Core.Skills.Model;
using KyberWeave.Core.Skills.Parsing;

namespace KyberWeave.Cli.Commands;

/// <summary>Settings common to the analysis commands (validate/lint/scan).</summary>
public class AnalysisSettings : CommandSettings
{
    [CommandArgument(0, "[path]")]
    [Description("Path to a SKILL.md, a skill directory, or a root containing many skills. Defaults to current directory.")]
    public string Path { get; set; } = ".";

    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output format: table | json | sarif | markdown.")]
    [DefaultValue("table")]
    public string Format { get; set; } = "table";

    [CommandOption("--no-info")]
    [Description("Hide Info-level findings.")]
    public bool NoInfo { get; set; }

    public OutputFormat ParsedFormat => Format.ToLowerInvariant() switch
    {
        "json" => OutputFormat.Json,
        "sarif" => OutputFormat.Sarif,
        "markdown" or "md" => OutputFormat.Markdown,
        _ => OutputFormat.Table
    };
}

public static class CommandHelpers
{
    /// <summary>
    /// Loads skills from a path. Adds any parse failures to <paramref name="report"/> as
    /// errors and returns the successfully parsed set. Returns null only when the path
    /// itself is invalid (nothing to do).
    /// </summary>
    public static SkillSet? LoadOrReport(string path, DiagnosticReport report)
    {
        var results = SkillLoader.Load(path);
        var skills = new List<Skill>();

        foreach (var r in results)
        {
            if (r.Success) skills.Add(r.Skill!);
            else report.Add(new Diagnostic("KW-PARSE-000", Severity.Error,
                r.Error ?? "Failed to parse skill.", System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(r.Path) ?? r.Path), r.Path));
        }

        // If nothing parsed and the only result is a "path not found / none found" message, treat as fatal-but-reported.
        if (skills.Count == 0 && results.All(r => !r.Success))
            return new SkillSet(skills); // report already carries the errors; caller decides exit code

        return new SkillSet(skills);
    }

    public static void Finish(DiagnosticReport report, AnalysisSettings settings, string command, string subjectLabel)
    {
        if (settings.NoInfo)
        {
            var filtered = new DiagnosticReport();
            filtered.AddRange(report.Items.Where(i => i.Severity != Severity.Info));
            report = filtered;
        }

        ReportRenderer.Render(report, settings.ParsedFormat, command, subjectLabel);
        if (settings.ParsedFormat == OutputFormat.Table)
        {
            AnsiConsole.WriteLine();
            ReportRenderer.RenderSummary(report);
        }
    }
}
