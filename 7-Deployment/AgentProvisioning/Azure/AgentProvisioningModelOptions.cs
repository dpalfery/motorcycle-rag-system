namespace MotorcycleRAG.AgentProvisioning.Azure;

public sealed record AgentProvisioningModelOptions(
    IReadOnlyList<string> OrchestratorModelCandidates,
    string SubAgentModel)
{
    public static AgentProvisioningModelOptions Default { get; } = new(
        AgentDefinitions.DefaultOrchestratorModelCandidates,
        AgentDefinitions.SubAgentModel);

    public static AgentProvisioningModelOptions FromEnvironment(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var orchestratorCandidates = ParseCandidates(
            getEnvironmentVariable("ORCHESTRATOR_MODEL_DEPLOYMENTS"),
            getEnvironmentVariable("ORCHESTRATOR_MODEL_DEPLOYMENT"));

        var subAgentModel = getEnvironmentVariable("SUBAGENT_MODEL_DEPLOYMENT");
        if (string.IsNullOrWhiteSpace(subAgentModel))
            subAgentModel = AgentDefinitions.SubAgentModel;

        return new AgentProvisioningModelOptions(orchestratorCandidates, subAgentModel);
    }

    private static IReadOnlyList<string> ParseCandidates(string? candidates, string? singleCandidate)
    {
        var configured = Split(candidates).Concat(Split(singleCandidate)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return configured.Length > 0
            ? configured
            : AgentDefinitions.DefaultOrchestratorModelCandidates;
    }

    private static IEnumerable<string> Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate));
    }
}
