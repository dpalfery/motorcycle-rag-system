using Azure.AI.Agents.Persistent;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Implements <see cref="IAgentAdminOperations"/> using <see cref="PersistentAgentsClient"/>
/// from the <c>Azure.AI.Agents.Persistent</c> SDK.
/// Used by <see cref="AgentProvisioningService"/> in production.
/// </summary>
internal sealed class PersistentAgentAdminClientAdapter : IAgentAdminOperations
{
    private readonly PersistentAgentsClient _client;

    public PersistentAgentAdminClientAdapter(PersistentAgentsClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Id, string Name)>> GetAgentsAsync(CancellationToken ct = default)
    {
        var result = new List<(string Id, string Name)>();

        await foreach (var agent in _client.Agents.GetAgentsAsync(cancellationToken: ct))
        {
            result.Add((agent.Id, agent.Name));
        }

        return result.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<string> CreateAgentAsync(
        string name,
        string model,
        string instructions,
        ToolDefinition[] tools,
        CancellationToken ct = default)
    {
        var options = new CreateAgentOptions(model)
        {
            Name = name,
            Instructions = instructions
        };

        foreach (var tool in tools)
        {
            options.Tools.Add(tool);
        }

        var response = await _client.Agents.CreateAgentAsync(options, ct);
        return response.Value.Id;
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
        var options = new UpdateAgentOptions
        {
            Model = model,
            Name = name,
            Instructions = instructions
        };

        foreach (var tool in tools)
        {
            options.Tools.Add(tool);
        }

        var response = await _client.Agents.UpdateAgentAsync(agentId, options, ct);
        return response.Value.Id;
    }
}
