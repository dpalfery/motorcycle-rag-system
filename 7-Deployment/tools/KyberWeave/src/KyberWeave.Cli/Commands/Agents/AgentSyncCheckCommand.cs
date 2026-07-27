using System.ComponentModel;
using KyberWeave.Core.Agents.Parsing;
using KyberWeave.Core.Agents.Validation;
using KyberWeave.Core.Diagnostics;
using Spectre.Console.Cli;

namespace KyberWeave.Cli.Commands.Agents;

public sealed class AgentSyncCheckCommand : Command<AnalysisSettings>
{
    public override int Execute(CommandContext context, AnalysisSettings settings)
    {
        var report = new DiagnosticReport();
        var agentSet = AgentLoader.LoadAll(settings.Path);

        var r = AgentSyncLinter.LintSet(agentSet, settings.Path);
        report.AddRange(r.Items);

        CommandHelpers.Finish(report, settings, "agent sync-check", "Agent");
        return report.HasErrors ? 1 : 0;
    }
}
