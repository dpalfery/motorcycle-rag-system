using KyberWeave.Core.Agents.Model;
using KyberWeave.Core.Parsing;

namespace KyberWeave.Core.Agents.Parsing;

/// <summary>
/// Parser for Markdown frontmatter agent definition files (.agent.md, .md).
/// </summary>
/// <remarks>
/// Frontmatter is read through the shared <see cref="MarkdownFrontmatterReader"/> so that
/// skills, agent definitions and documentation all resolve a frontmatter block the same
/// way. A second pipeline here would drift from the shared one silently.
/// </remarks>
public sealed class MarkdownAgentParser : IAgentParser
{
    public bool CanParse(string filePath) =>
        filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase);

    public AgentModel Parse(string filePath, HarnessKind harness)
    {
        var raw = File.ReadAllText(filePath);
        var dirName = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        var fileName = Path.GetFileName(filePath);

        // Standardize role name from file name (e.g. architect.agent.md -> architect, dotnet-dev.md -> dotnet-dev)
        string roleName = fileName.Replace(".agent.md", "", StringComparison.OrdinalIgnoreCase)
                                 .Replace(".md", "", StringComparison.OrdinalIgnoreCase);

        var read = MarkdownFrontmatterReader.Read(raw);

        string description = string.Empty;
        string model = string.Empty;
        string body = read.HasFrontmatter ? read.Body : raw;
        var tools = new List<string>();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (read.HasFrontmatter)
        {
            try
            {
                var dict = MarkdownFrontmatterReader.Deserializer
                    .Deserialize<Dictionary<string, object>>(read.Yaml);
                if (dict is not null)
                {
                    foreach (var (k, v) in dict)
                    {
                        var valStr = v?.ToString() ?? string.Empty;
                        metadata[k] = valStr;

                        if (k.Equals("name", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(valStr))
                            roleName = valStr;
                        else if (k.Equals("description", StringComparison.OrdinalIgnoreCase))
                            description = valStr;
                        else if (k.Equals("model", StringComparison.OrdinalIgnoreCase))
                            model = valStr;
                    }
                }
            }
            catch
            {
                // Best-effort yaml parsing
            }
        }

        return new AgentModel
        {
            RoleName = roleName,
            Harness = harness,
            FilePath = Path.GetFullPath(filePath),
            DirectoryPath = dirName,
            Description = description,
            InstructionsBody = body,
            ModelPreference = model,
            Tools = tools,
            FrontmatterOrMetadata = metadata
        };
    }
}
