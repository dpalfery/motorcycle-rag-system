using System.ComponentModel;
using SkillForge.Core.Agents.Parsing;
using SkillForge.Core.Agents.Validation;
using SkillForge.Core.Diagnostics;
using Spectre.Console.Cli;

namespace SkillForge.Cli.Commands.Agents;

public sealed class AgentValidateCommand : Command<AnalysisSettings>
{
    public override int Execute(CommandContext context, AnalysisSettings settings)
    {
        var report = new DiagnosticReport();
        var agentSet = AgentLoader.LoadAll(settings.Path);

        foreach (var agent in agentSet.Agents)
        {
            var r = AgentSpecValidator.Validate(agent);
            report.AddRange(r.Items);
        }

        CommandHelpers.Finish(report, settings, "agent validate");
        return report.HasErrors ? 1 : 0;
    }
}
