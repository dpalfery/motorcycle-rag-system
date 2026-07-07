using Spectre.Console.Cli;
using SkillForge.Cli.Rendering;
using SkillForge.Core.Diagnostics;
using SkillForge.Core.Validation;

namespace SkillForge.Cli.Commands;

public sealed class ValidateCommand : Command<AnalysisSettings>
{
    public override int Execute(CommandContext context, AnalysisSettings settings)
    {
        var report = new DiagnosticReport();
        var set = CommandHelpers.LoadOrReport(settings.Path, report);

        if (set is not null)
            foreach (var skill in set.Skills)
                report.AddRange(SpecValidator.Validate(skill));

        CommandHelpers.Finish(report, settings, "validate");
        return report.HasErrors ? 1 : 0;
    }
}
