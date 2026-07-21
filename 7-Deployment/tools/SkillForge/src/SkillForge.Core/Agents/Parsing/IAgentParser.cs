using SkillForge.Core.Agents.Model;

namespace SkillForge.Core.Agents.Parsing;

/// <summary>
/// Interface for parsing harness-specific agent definition files into an <see cref="AgentModel"/>.
/// </summary>
public interface IAgentParser
{
    bool CanParse(string filePath);
    AgentModel Parse(string filePath, HarnessKind harness);
}
