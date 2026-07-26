using System.ComponentModel;
using KyberWeave.Core.Agents.Parsing;
using KyberWeave.Core.Agents.Validation;
using KyberWeave.Core.Diagnostics;
using Spectre.Console.Cli;

namespace KyberWeave.Cli.Commands.Agents;

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

        CommandHelpers.Finish(report, settings, "agent validate", "Agent");
        return report.HasErrors ? 1 : 0;
    }
}
