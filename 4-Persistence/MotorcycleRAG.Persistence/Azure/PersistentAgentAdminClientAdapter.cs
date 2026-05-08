using Azure.AI.Agents.Persistent;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Implements <see cref="IAgentAdminOperations"/> using <see cref="PersistentAgentsClient"/>
/// from the <c>Azure.AI.Agents.Persistent</c> SDK.
/// Used by <see cref="AgentProvisioningService"/> in production.
/// </summary>
internal sealed class PersistentAgentAdminClientAdapter : IAgentAdminOperations
{
    private readonly PersistentAgentsAdministrationClient _client;

    public PersistentAgentAdminClientAdapter(PersistentAgentsAdministrationClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Id, string Name)>> GetAgentsAsync(CancellationToken ct = default)
    {
        var agents = new List<(string Id, string Name)>();

        await foreach (var agent in _client.GetAgentsAsync(limit: 100, cancellationToken: ct))
        {
            agents.Add((agent.Id, agent.Name));
        }

        return agents.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<string> CreateAgentAsync(
        string name,
        string model,
        string instructions,
        ToolDefinition[] tools,
        CancellationToken ct = default)
    {
        var agent = await _client.CreateAgentAsync(
            model: model,
            name: name,
            description: $"Motorcycle RAG {name}",
            instructions: instructions,
            tools: tools,
            cancellationToken: ct);

        return agent.Value.Id;
    }

    /// <inheritdoc />
    public async Task<string> UpdateAgentAsync(
        string agentId,
        string name,
        string model,
        string instructions,
        ToolDefinition[] tools,
        CancellationToken ct = default)
    {
        var agent = await _client.UpdateAgentAsync(
            assistantId: agentId,
            model: model,
            name: name,
            description: $"Motorcycle RAG {name}",
            instructions: instructions,
            tools: tools,
            cancellationToken: ct);

        return agent.Value.Id;
    }
}
