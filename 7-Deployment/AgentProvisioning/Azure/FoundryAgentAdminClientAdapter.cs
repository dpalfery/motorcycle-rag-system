using Azure.AI.Projects.Agents;
using OpenAI.Responses;
using System.Globalization;

namespace MotorcycleRAG.AgentProvisioning.Azure;

/// <summary>
/// Implements <see cref="IAgentAdminOperations"/> using the Microsoft Foundry
/// versioned agents SDK.
/// </summary>
internal sealed class FoundryAgentAdminClientAdapter : IAgentAdminOperations
{
    private readonly AgentAdministrationClient _client;

    public FoundryAgentAdminClientAdapter(AgentAdministrationClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAgentNamesAsync(CancellationToken ct = default)
    {
        var agents = new List<string>();

        await foreach (var agent in _client.GetAgentsAsync(cancellationToken: ct))
        {
            agents.Add(agent.Name);
        }

        return agents.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<ProvisionedAgentReference> CreateAgentVersionAsync(
        string name,
        string model,
        string instructions,
        ResponseTool[] tools,
        CancellationToken ct = default)
    {
        var definition = new DeclarativeAgentDefinition(name)
        {
            Model = model,
            Instructions = instructions
        };

        foreach (var tool in tools)
        {
            definition.Tools.Add(tool);
        }

        var options = new ProjectsAgentVersionCreationOptions(definition)
        {
            Description = $"Motorcycle RAG {name}"
        };
        options.Metadata["system"] = "motorcycle-rag";

        var version = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var agent = await _client.CreateAgentVersionAsync(
            name,
            options,
            version,
            ct);

        return new ProvisionedAgentReference(agent.Value.Name, agent.Value.Version);
    }
}
