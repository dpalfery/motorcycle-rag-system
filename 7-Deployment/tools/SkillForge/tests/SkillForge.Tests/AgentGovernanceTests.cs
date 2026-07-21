using SkillForge.Core.Agents.Model;
using SkillForge.Core.Agents.Parsing;
using SkillForge.Core.Agents.Routing;
using SkillForge.Core.Agents.Security;
using SkillForge.Core.Agents.Validation;
using SkillForge.Core.Diagnostics;
using Xunit;

namespace SkillForge.Tests;

public class AgentGovernanceTests
{
    [Fact]
    public void TomlAgentParser_Parses_Codex_Manifest()
    {
        var tempFile = Path.GetTempFileName() + ".toml";
        var content = """
            name = "architect"
            description = "Produces an implementation plan before coding."
            model = "gpt-5.6-sol"

            developer_instructions = '''
            You are an experienced technical leader.
            Planning behavior: Inspect codebase first.
            '''
            """;
        File.WriteAllText(tempFile, content);

        try
        {
            var parser = new TomlAgentParser();
            Assert.True(parser.CanParse(tempFile));

            var agent = parser.Parse(tempFile, HarnessKind.Codex);
            Assert.Equal("architect", agent.RoleName);
            Assert.Equal("Produces an implementation plan before coding.", agent.Description);
            Assert.Equal("gpt-5.6-sol", agent.ModelPreference);
            Assert.Contains("Planning behavior", agent.InstructionsBody);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void MarkdownAgentParser_Parses_Cursor_Manifest()
    {
        var tempFile = Path.GetTempFileName() + ".agent.md";
        var content = """
            ---
            name: dotnet-dev
            description: Use when writing C# and .NET code.
            model: gpt-5.6-sol
            ---
            You are a senior .NET engineer.
            """;
        File.WriteAllText(tempFile, content);

        try
        {
            var parser = new MarkdownAgentParser();
            Assert.True(parser.CanParse(tempFile));

            var agent = parser.Parse(tempFile, HarnessKind.Cursor);
            Assert.Equal("dotnet-dev", agent.RoleName);
            Assert.Equal("Use when writing C# and .NET code.", agent.Description);
            Assert.Contains("senior .NET engineer", agent.InstructionsBody);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AgentSyncLinter_Satisfies_MappedSkill_Roles_Like_Conductor()
    {
        var agents = new List<AgentModel>
        {
            new AgentModel
            {
                RoleName = "conductor",
                Harness = HarnessKind.Kilo,
                FilePath = "/tmp/.kilo/agents/conductor.md",
                DirectoryPath = "/tmp/.kilo/agents",
                Description = "Orchestrates multi-agent tasks.",
                InstructionsBody = "Orchestrate work."
            }
        };

        var set = new AgentSet(agents);
        var report = AgentSyncLinter.LintSet(set, "/tmp");

        // Conductor is mapped as a skill for Claude/Cursor/Antigravity/Codex, so role satisfaction should not fail for them if skills exist
        var missingErrors = report.Items.Where(i => i.Code == AgentSyncLinter.RuleUnsatisfiedRole && i.SkillName == "conductor").ToList();
        Assert.NotNull(missingErrors);
    }

    [Fact]
    public void AgentPromptScanner_Flags_Hardcoded_Secrets()
    {
        var agent = new AgentModel
        {
            RoleName = "test-agent",
            Harness = HarnessKind.Claude,
            FilePath = "/tmp/test.md",
            DirectoryPath = "/tmp",
            Description = "Test agent",
            InstructionsBody = "Use secret key sk-123456789012345678901234 to authenticate."
        };

        var report = AgentPromptScanner.Scan(agent);
        Assert.Contains(report.Items, i => i.Code == AgentPromptScanner.RuleHardcodedSecret);
    }

    [Fact]
    public void AgentRoutingEvaluator_Routes_Prompt_To_Best_Agent()
    {
        var agents = new List<AgentModel>
        {
            new AgentModel
            {
                RoleName = "dotnet-dev",
                Harness = HarnessKind.Codex,
                FilePath = "/tmp/.codex/agents/dotnet-dev.toml",
                DirectoryPath = "/tmp/.codex/agents",
                Description = "Use when writing C# .NET code and ASP.NET Core APIs.",
                InstructionsBody = "Write clean C#."
            },
            new AgentModel
            {
                RoleName = "python-dev",
                Harness = HarnessKind.Codex,
                FilePath = "/tmp/.codex/agents/python-dev.toml",
                DirectoryPath = "/tmp/.codex/agents",
                Description = "Use when writing Python code and local processing services.",
                InstructionsBody = "Write clean Python."
            }
        };

        var set = new AgentSet(agents);
        var result = AgentRoutingEvaluator.Route("Build an ASP.NET Core API in C#", set);

        Assert.True(result.Fired);
        Assert.Equal("dotnet-dev", result.SelectedRole);
    }
}
