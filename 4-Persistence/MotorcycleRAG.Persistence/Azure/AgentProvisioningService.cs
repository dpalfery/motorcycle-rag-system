using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Creates or updates all four Foundry agent definitions (OrchestratorAgent, VectorSearchAgent,
/// WebSearchAgent, PDFSearchAgent) using the Azure AI Agents Persistent SDK.
/// Upsert logic: finds existing agents by name, updates them if found, creates them if not.
/// Called exclusively from the <c>MotorcycleRAG.AgentProvisioning</c> CLI during the deploy pipeline.
/// </summary>
public sealed class AgentProvisioningService
{
    private readonly IAgentAdminOperations _adminOps;
    private readonly ILogger<AgentProvisioningService> _logger;

    /// <summary>Production constructor — creates a <see cref="PersistentAgentsClient"/> from options.</summary>
    public AgentProvisioningService(
        IOptions<AzureFoundryOptions> options,
        ILogger<AgentProvisioningService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var config = options.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(config.FoundryEndpoint))
            throw new InvalidOperationException("AzureAI:FoundryEndpoint is required for AgentProvisioningService");

        var client = new PersistentAgentsClient(config.FoundryEndpoint, new DefaultAzureCredential());
        _adminOps = new PersistentAgentAdminClientAdapter(client);
        _logger = logger;
    }

    /// <summary>Test constructor — injects a mock <see cref="IAgentAdminOperations"/>.</summary>
    internal AgentProvisioningService(
        IAgentAdminOperations adminOps,
        ILogger<AgentProvisioningService> logger)
    {
        _adminOps = adminOps ?? throw new ArgumentNullException(nameof(adminOps));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates or updates all four Foundry agents and returns their assigned IDs.
    /// </summary>
    public async Task<ProvisionedAgentIds> ProvisionAllAgentsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Foundry agent provisioning");

        var orchestratorId = await UpsertAgentAsync(
            AgentDefinitions.OrchestratorAgentName,
            AgentDefinitions.OrchestratorModel,
            AgentDefinitions.OrchestratorSystemPrompt,
            AgentDefinitions.OrchestratorTools,
            ct);

        var vectorSearchId = await UpsertAgentAsync(
            AgentDefinitions.VectorSearchAgentName,
            AgentDefinitions.SubAgentModel,
            AgentDefinitions.VectorSearchSystemPrompt,
            AgentDefinitions.VectorSearchTools,
            ct);

        var webSearchId = await UpsertAgentAsync(
            AgentDefinitions.WebSearchAgentName,
            AgentDefinitions.SubAgentModel,
            AgentDefinitions.WebSearchSystemPrompt,
            AgentDefinitions.WebSearchTools,
            ct);

        var pdfSearchId = await UpsertAgentAsync(
            AgentDefinitions.PDFSearchAgentName,
            AgentDefinitions.SubAgentModel,
            AgentDefinitions.PDFSearchSystemPrompt,
            AgentDefinitions.PDFSearchTools,
            ct);

        _logger.LogInformation(
            "Agent provisioning complete: orchestrator={OrchestratorId} vectorSearch={VectorId} webSearch={WebId} pdfSearch={PdfId}",
            orchestratorId, vectorSearchId, webSearchId, pdfSearchId);

        return new ProvisionedAgentIds(orchestratorId, vectorSearchId, webSearchId, pdfSearchId);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string> UpsertAgentAsync(
        string name,
        string model,
        string instructions,
        ToolDefinition[] tools,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instructions))
            throw new InvalidOperationException($"System prompt for agent '{name}' is missing or empty");

        var existingAgents = await _adminOps.GetAgentsAsync(ct);

        foreach (var (id, existingName) in existingAgents)
        {
            if (existingName == name)
            {
                _logger.LogInformation("Updating existing agent '{AgentName}' (id={AgentId})", name, id);
                return await _adminOps.UpdateAgentAsync(id, name, model, instructions, tools, ct);
            }
        }

        _logger.LogInformation("Creating new agent '{AgentName}'", name);
        return await _adminOps.CreateAgentAsync(name, model, instructions, tools, ct);
    }
}
