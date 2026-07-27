namespace KyberWeave.Core.Agents.Model;

/// <summary>
/// Defines the capabilities and mappings for a specific development harness platform.
/// Used by validation and linting engines to understand whether parent agents (like conductor)
/// are implemented natively in the harness agent folder or via skill mappings.
/// </summary>
public sealed class HarnessCapabilityProfile
{
    public HarnessKind Harness { get; init; }
    public string DirectoryName { get; init; } = string.Empty;
    public bool SupportsNativeParentAgents { get; init; } = true;
    public Dictionary<string, string> MappedRoleSkillOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<HarnessKind, HarnessCapabilityProfile> DefaultProfiles { get; } =
        new Dictionary<HarnessKind, HarnessCapabilityProfile>
        {
            [HarnessKind.Codex] = new HarnessCapabilityProfile
            {
                Harness = HarnessKind.Codex,
                DirectoryName = ".codex/agents",
                SupportsNativeParentAgents = false,
                MappedRoleSkillOverrides = new() { ["conductor"] = "conductor" }
            },
            [HarnessKind.Cursor] = new HarnessCapabilityProfile
            {
                Harness = HarnessKind.Cursor,
                DirectoryName = ".cursor/agents",
                SupportsNativeParentAgents = false,
                MappedRoleSkillOverrides = new() { ["conductor"] = "conductor" }
            },
            [HarnessKind.Claude] = new HarnessCapabilityProfile
            {
                Harness = HarnessKind.Claude,
                DirectoryName = ".claude/agents",
                SupportsNativeParentAgents = false,
                MappedRoleSkillOverrides = new() { ["conductor"] = "conductor" }
            },
            [HarnessKind.GitHubCopilot] = new HarnessCapabilityProfile
            {
                Harness = HarnessKind.GitHubCopilot,
                DirectoryName = ".github/agents",
                SupportsNativeParentAgents = false,
                MappedRoleSkillOverrides = new() { ["conductor"] = "conductor" }
            },
            [HarnessKind.OpenCode] = new HarnessCapabilityProfile
            {
                Harness = HarnessKind.OpenCode,
                DirectoryName = ".opencode/agents",
                SupportsNativeParentAgents = true
            },
            [HarnessKind.Kilo] = new HarnessCapabilityProfile
            {
                Harness = HarnessKind.Kilo,
                DirectoryName = ".kilo/agents",
                SupportsNativeParentAgents = true
            }
        };
}
