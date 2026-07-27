using System.Text.RegularExpressions;
using KyberWeave.Core.Agents.Model;
using KyberWeave.Core.Diagnostics;

namespace KyberWeave.Core.Agents.Security;

/// <summary>
/// Security scanner for agent system prompts and manifests.
/// </summary>
public static class AgentPromptScanner
{
    public const string RuleSafetyBypass = "KW-AGENT-SEC-001";
    public const string RuleHardcodedSecret = "KW-AGENT-SEC-002";
    public const string RuleRiskyDirective = "KW-AGENT-SEC-003";

    private static readonly Regex SecretRegex =
        new(@"\b(?:sk-[A-Za-z0-9]{20,}|ghp_[A-Za-z0-9]{30,}|AKIA[0-9A-Z]{16}|Password\s*=\s*[^\s;]+)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BypassRegex =
        new(@"\b(ignore\s+(all\s+)?previous\s+instructions|bypass\s+(the\s+)?sandbox|disable\s+user\s+approval|skip\s+confirmation)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static DiagnosticReport Scan(AgentModel agent)
    {
        var report = new DiagnosticReport();
        var text = $"{agent.Description}\n{agent.InstructionsBody}";

        // 1. Secrets Scan
        var secretMatches = SecretRegex.Matches(text);
        foreach (Match match in secretMatches)
        {
            report.Add(new Diagnostic(RuleHardcodedSecret, Severity.Critical,
                $"Potential hardcoded secret or token detected in agent prompt: '{match.Value[..Math.Min(8, match.Value.Length)]}...'",
                Path.GetFileName(agent.FilePath), agent.FilePath));
        }

        // 2. Safety Bypass Scan
        var bypassMatches = BypassRegex.Matches(text);
        foreach (Match match in bypassMatches)
        {
            report.Add(new Diagnostic(RuleSafetyBypass, Severity.Warning,
                $"Potential safety gate bypass directive detected in prompt: '{match.Value}'",
                Path.GetFileName(agent.FilePath), agent.FilePath));
        }

        return report;
    }
}
