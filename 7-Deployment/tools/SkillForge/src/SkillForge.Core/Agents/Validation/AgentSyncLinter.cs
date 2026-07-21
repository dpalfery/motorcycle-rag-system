using SkillForge.Core.Agents.Model;
using SkillForge.Core.Diagnostics;
using SkillForge.Core.Model;
using SkillForge.Core.Text;
using SkillForge.Core.Validation;

namespace SkillForge.Core.Agents.Validation;

/// <summary>
/// Linter for evaluating cross-harness role parity, instruction drift, and routing readiness.
/// </summary>
public static class AgentSyncLinter
{
    public const string RuleUnsatisfiedRole = "SF-AGENT-SYNC-001";
    public const string RuleInstructionDrift = "SF-AGENT-SYNC-002";
    public const string RuleLowRoutingScore = "SF-AGENT-LINT-001";

    public static DiagnosticReport LintSet(AgentSet agentSet, string rootDirectoryPath)
    {
        var report = new DiagnosticReport();
        var matrix = agentSet.GetRoleHarnessMatrix();
        var allRoles = agentSet.GetAllRoleNames();
        var profiles = HarnessCapabilityProfile.DefaultProfiles;

        // 1. Cross-Harness Role Parity Check with Role Satisfaction Engine
        foreach (var role in allRoles)
        {
            matrix.TryGetValue(role, out var harnessMap);
            harnessMap ??= new Dictionary<HarnessKind, AgentModel>();

            foreach (var (harnessKind, profile) in profiles)
            {
                bool satisfied = harnessMap.ContainsKey(harnessKind);

                // If not satisfied natively in the harness folder, check if it's satisfied via skill mapping or skill directory
                if (!satisfied)
                {
                    if (profile.MappedRoleSkillOverrides.TryGetValue(role, out var skillName))
                    {
                        var skillDir = Path.Combine(rootDirectoryPath, ".agents", "skills", skillName);
                        var docSkillDir = Path.Combine(rootDirectoryPath, "6-Docs");
                        if (Directory.Exists(skillDir) || File.Exists(Path.Combine(rootDirectoryPath, "SKILL.md")))
                        {
                            satisfied = true; // Role is satisfied via mapped skill
                        }
                    }
                }

                if (!satisfied)
                {
                    report.Add(new Diagnostic(RuleUnsatisfiedRole, Severity.Warning,
                        $"Agent role '{role}' is missing in harness '{harnessKind}' ({profile.DirectoryName}). All 6 harness configurations should remain synchronized.",
                        role, profile.DirectoryName));
                }
            }

            // 2. Instruction Drift Detection across existing harness implementations of this role
            var roleAgents = harnessMap.Values.ToList();
            if (roleAgents.Count >= 2)
            {
                var baseAgent = roleAgents[0];
                var baseVec = TextVectorizer.Vectorize(baseAgent.InstructionsBody);

                for (int i = 1; i < roleAgents.Count; i++)
                {
                    var compareAgent = roleAgents[i];
                    var compareVec = TextVectorizer.Vectorize(compareAgent.InstructionsBody);
                    var similarity = TextVectorizer.CosineSimilarity(baseVec, compareVec);

                    if (similarity < 0.70)
                    {
                        report.Add(new Diagnostic(RuleInstructionDrift, Severity.Warning,
                            $"Instruction drift detected for role '{role}' between '{baseAgent.Harness}' and '{compareAgent.Harness}' (similarity: {similarity:P0}).",
                            role, compareAgent.FilePath));
                    }
                }
            }

            // 3. Routing Description Score for each agent instance
            foreach (var agent in roleAgents)
            {
                if (!string.IsNullOrWhiteSpace(agent.Description))
                {
                    var dummySkill = new Skill
                    {
                        SkillFilePath = agent.FilePath,
                        DirectoryPath = agent.DirectoryPath,
                        RawFrontmatter = string.Empty,
                        InstructionsBody = agent.InstructionsBody,
                        Frontmatter = new SkillFrontmatter
                        {
                            Name = agent.RoleName,
                            Description = agent.Description
                        }
                    };

                    var score = DescriptionScorer.Score(dummySkill);
                    if (score.Total < 50)
                    {
                        report.Add(new Diagnostic(RuleLowRoutingScore, Severity.Info,
                            $"Agent '{agent.RoleName}' in '{agent.Harness}' has low description routing score ({score.Total}/100). Consider adding explicit 'Use when...' and 'Do NOT use for...' clauses.",
                            agent.RoleName, agent.FilePath));
                    }
                }
            }
        }

        return report;
    }
}
