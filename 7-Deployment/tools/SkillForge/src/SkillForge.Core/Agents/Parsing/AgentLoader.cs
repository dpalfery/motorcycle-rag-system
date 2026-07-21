using SkillForge.Core.Agents.Model;

namespace SkillForge.Core.Agents.Parsing;

/// <summary>
/// Result of attempting to load a single agent file.
/// </summary>
public sealed record AgentLoadResult(bool Success, AgentModel? Agent, string Path, string? Error);

/// <summary>
/// Discovers and loads agent definitions across project directories.
/// </summary>
public static class AgentLoader
{
    private static readonly TomlAgentParser TomlParser = new();
    private static readonly MarkdownAgentParser MarkdownParser = new();

    public static AgentSet LoadAll(string rootPath)
    {
        var results = LoadResults(rootPath);
        var validAgents = results.Where(r => r.Success && r.Agent is not null).Select(r => r.Agent!).ToList();
        return new AgentSet(validAgents);
    }

    public static List<AgentLoadResult> LoadResults(string rootPath)
    {
        var results = new List<AgentLoadResult>();
        var fullRoot = Path.GetFullPath(rootPath);

        foreach (var profile in HarnessCapabilityProfile.DefaultProfiles.Values)
        {
            var harnessDir = Path.Combine(fullRoot, profile.DirectoryName);
            if (!Directory.Exists(harnessDir)) continue;

            var files = Directory.GetFiles(harnessDir, "*.*", SearchOption.TopDirectoryOnly)
                                 .Where(f => f.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                try
                {
                    AgentModel agent;
                    if (TomlParser.CanParse(file))
                    {
                        agent = TomlParser.Parse(file, profile.Harness);
                    }
                    else if (MarkdownParser.CanParse(file))
                    {
                        agent = MarkdownParser.Parse(file, profile.Harness);
                    }
                    else
                    {
                        continue;
                    }

                    results.Add(new AgentLoadResult(true, agent, file, null));
                }
                catch (Exception ex)
                {
                    results.Add(new AgentLoadResult(false, null, file, ex.Message));
                }
            }
        }

        return results;
    }
}
