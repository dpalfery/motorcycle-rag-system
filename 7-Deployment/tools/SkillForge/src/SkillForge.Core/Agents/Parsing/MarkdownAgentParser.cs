using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using SkillForge.Core.Agents.Model;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SkillForge.Core.Agents.Parsing;

/// <summary>
/// Parser for Markdown frontmatter agent definition files (.agent.md, .md).
/// </summary>
public sealed class MarkdownAgentParser : IAgentParser
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();

    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

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

        var document = Markdown.Parse(raw, Pipeline);
        var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();

        string description = string.Empty;
        string model = string.Empty;
        string body = raw;
        var tools = new List<string>();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (yamlBlock is not null)
        {
            var rawYaml = ExtractYamlText(raw, yamlBlock);
            try
            {
                var dict = Deserializer.Deserialize<Dictionary<string, object>>(rawYaml);
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

            body = ExtractBody(raw, yamlBlock);
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

    private static string ExtractYamlText(string content, YamlFrontMatterBlock block)
    {
        var slice = content.Substring(block.Span.Start, block.Span.Length);
        var lines = slice.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[0].TrimStart().StartsWith("---")) lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].TrimStart().StartsWith("---")) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    private static string ExtractBody(string content, YamlFrontMatterBlock block)
    {
        var afterIndex = block.Span.End + 1;
        if (afterIndex >= content.Length) return string.Empty;
        return content.Substring(afterIndex).TrimStart('\r', '\n');
    }
}
