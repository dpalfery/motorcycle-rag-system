using SkillForge.Core.Agents.Model;
using SkillForge.Core.Diagnostics;

namespace SkillForge.Core.Agents.Validation;

/// <summary>
/// Validates individual Agent definition files against specification rules.
/// </summary>
public static class AgentSpecValidator
{
    public const string RuleMissingName = "SF-AGENT-SPEC-001";
    public const string RuleMissingDescription = "SF-AGENT-SPEC-002";
    public const string RuleMissingInstructions = "SF-AGENT-SPEC-003";
    public const string RuleBrokenReference = "SF-AGENT-SPEC-004";

    public static DiagnosticReport Validate(AgentModel agent)
    {
        var report = new DiagnosticReport();

        // 1. Role Name Check
        if (string.IsNullOrWhiteSpace(agent.RoleName))
        {
            report.Add(new Diagnostic(RuleMissingName, Severity.Error,
                "Agent definition must specify a non-empty name / role.",
                Path.GetFileName(agent.FilePath), agent.FilePath));
        }

        // 2. Description Check
        if (string.IsNullOrWhiteSpace(agent.Description))
        {
            report.Add(new Diagnostic(RuleMissingDescription, Severity.Warning,
                $"Agent '{agent.RoleName}' has no description — routing orchestrators may misroute prompts.",
                Path.GetFileName(agent.FilePath), agent.FilePath));
        }

        // 3. Instructions Body Check
        if (string.IsNullOrWhiteSpace(agent.InstructionsBody))
        {
            report.Add(new Diagnostic(RuleMissingInstructions, Severity.Error,
                $"Agent '{agent.RoleName}' has an empty system prompt / instructions body.",
                Path.GetFileName(agent.FilePath), agent.FilePath));
        }

        return report;
    }
}
