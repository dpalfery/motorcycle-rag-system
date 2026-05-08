using OpenAI.Responses;

namespace MotorcycleRAG.AgentProvisioning.Azure;

/// <summary>
/// Abstraction over Microsoft Foundry versioned-agent provisioning operations.
/// Enables unit testing of <see cref="AgentProvisioningService"/> without hitting Azure.
/// </summary>
public interface IAgentAdminOperations
{
    /// <summary>Lists all agents, returning their names.</summary>
    Task<IReadOnlyList<string>> GetAgentNamesAsync(CancellationToken ct = default);

    /// <summary>Creates a new immutable version for an agent and returns its reference.</summary>
    Task<ProvisionedAgentReference> CreateAgentVersionAsync(
        string name,
        string model,
        string instructions,
        ResponseTool[] tools,
        CancellationToken ct = default);
}
