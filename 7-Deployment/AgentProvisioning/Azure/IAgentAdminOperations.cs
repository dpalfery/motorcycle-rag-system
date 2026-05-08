using Azure.AI.Agents.Persistent;

namespace MotorcycleRAG.AgentProvisioning.Azure;

/// <summary>
/// Abstraction over Azure AI Agents persistent-agent CRUD operations.
/// Enables unit testing of <see cref="AgentProvisioningService"/> without hitting Azure.
/// </summary>
public interface IAgentAdminOperations
{
    /// <summary>Lists all agents, returning their IDs and names.</summary>
    Task<IReadOnlyList<(string Id, string Name)>> GetAgentsAsync(CancellationToken ct = default);

    /// <summary>Creates a new agent and returns its assigned ID.</summary>
    Task<string> CreateAgentAsync(
        string name,
        string model,
        string instructions,
        ToolDefinition[] tools,
        CancellationToken ct = default);

    /// <summary>Updates an existing agent and returns its ID.</summary>
    Task<string> UpdateAgentAsync(
        string agentId,
        string name,
        string model,
        string instructions,
        ToolDefinition[] tools,
        CancellationToken ct = default);
}
