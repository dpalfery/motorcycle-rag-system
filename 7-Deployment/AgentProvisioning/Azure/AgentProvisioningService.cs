using Azure.AI.Agents.Persistent;
using Azure;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.AgentProvisioning.Azure;

/// <summary>
/// Creates or updates all four Foundry agent definitions (OrchestratorAgent, VectorSearchAgent,
/// WebSearchAgent, PDFSearchAgent) using the Azure AI Agents Persistent SDK.
/// Upsert logic: finds existing agents by name, updates them if found, creates them if not.
/// Called exclusively from the <c>MotorcycleRAG.AgentProvisioning</c> CLI during the deploy pipeline.
/// </summary>
public sealed class AgentProvisioningService {
    private readonly IAgentAdminOperations _adminOps;
    private readonly ILogger<AgentProvisioningService> _logger;
    private readonly AgentProvisioningModelOptions _modelOptions;
    private readonly string _orchestratorSystemPrompt;
    private readonly string _vectorSearchSystemPrompt;
    private readonly string _webSearchSystemPrompt;
    private readonly string _pdfSearchSystemPrompt;

    /// <summary>Production constructor — creates a <see cref="PersistentAgentsClient"/> from options.</summary>
    public AgentProvisioningService(
        string foundryEndpoint,
        AgentProvisioningModelOptions modelOptions,
        ILogger<AgentProvisioningService> logger) {
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(foundryEndpoint))
            throw new InvalidOperationException("AzureAI:FoundryEndpoint is required for AgentProvisioningService");

        var client = new PersistentAgentsAdministrationClient(foundryEndpoint, new DefaultAzureCredential());
        _adminOps = new PersistentAgentAdminClientAdapter(client);
        _logger = logger;
        _modelOptions = modelOptions;
        _orchestratorSystemPrompt = AgentDefinitions.OrchestratorSystemPrompt;
        _vectorSearchSystemPrompt = AgentDefinitions.VectorSearchSystemPrompt;
        _webSearchSystemPrompt = AgentDefinitions.WebSearchSystemPrompt;
        _pdfSearchSystemPrompt = AgentDefinitions.PDFSearchSystemPrompt;
    }

    /// <summary>Test constructor — injects a mock <see cref="IAgentAdminOperations"/>.</summary>
    internal AgentProvisioningService(
        IAgentAdminOperations adminOps,
        ILogger<AgentProvisioningService> logger,
        AgentProvisioningModelOptions? modelOptions = null,
        string? orchestratorSystemPrompt = null,
        string? vectorSearchSystemPrompt = null,
        string? webSearchSystemPrompt = null,
        string? pdfSearchSystemPrompt = null) {
        _adminOps = adminOps ?? throw new ArgumentNullException(nameof(adminOps));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modelOptions = modelOptions ?? AgentProvisioningModelOptions.Default;
        _orchestratorSystemPrompt = orchestratorSystemPrompt ?? AgentDefinitions.OrchestratorSystemPrompt;
        _vectorSearchSystemPrompt = vectorSearchSystemPrompt ?? AgentDefinitions.VectorSearchSystemPrompt;
        _webSearchSystemPrompt = webSearchSystemPrompt ?? AgentDefinitions.WebSearchSystemPrompt;
        _pdfSearchSystemPrompt = pdfSearchSystemPrompt ?? AgentDefinitions.PDFSearchSystemPrompt;
    }

    /// <summary>
    /// Creates or updates all four Foundry agents and returns their assigned IDs.
    /// </summary>
    public async Task<ProvisionedAgentIds> ProvisionAllAgentsAsync(CancellationToken ct = default) {
        _logger.LogInformation("Starting Foundry agent provisioning");

        var orchestratorId = await UpsertAgentWithFallbackAsync(
            AgentDefinitions.OrchestratorAgentName,
            _modelOptions.OrchestratorModelCandidates,
            _orchestratorSystemPrompt,
            AgentDefinitions.OrchestratorTools,
            ct);

        var vectorSearchId = await UpsertAgentAsync(
            AgentDefinitions.VectorSearchAgentName,
            _modelOptions.SubAgentModel,
            _vectorSearchSystemPrompt,
            AgentDefinitions.VectorSearchTools,
            ct);

        var webSearchId = await UpsertAgentAsync(
            AgentDefinitions.WebSearchAgentName,
            _modelOptions.SubAgentModel,
            _webSearchSystemPrompt,
            AgentDefinitions.WebSearchTools,
            ct);

        var pdfSearchId = await UpsertAgentAsync(
            AgentDefinitions.PDFSearchAgentName,
            _modelOptions.SubAgentModel,
            _pdfSearchSystemPrompt,
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

    private async Task<string> UpsertAgentWithFallbackAsync(
        string name,
        IReadOnlyList<string> modelCandidates,
        string instructions,
        ToolDefinition[] tools,
        CancellationToken ct) {
        if (modelCandidates.Count == 0)
            throw new InvalidOperationException($"No model deployment candidates configured for agent '{name}'");

        Exception? lastRejectedModel = null;
        foreach (var model in modelCandidates) {
            try {
                return await UpsertAgentAsync(name, model, instructions, tools, ct);
            }
            catch (RequestFailedException ex) when (ShouldTryNextModel(ex)) {
                lastRejectedModel = ex;
                _logger.LogWarning(
                    ex,
                    "Foundry rejected model deployment '{ModelDeployment}' for agent '{AgentName}'. Trying next candidate.",
                    model,
                    name);
            }
        }

        throw new InvalidOperationException(
            $"Foundry rejected all configured model deployment candidates for agent '{name}'",
            lastRejectedModel);
    }

    private async Task<string> UpsertAgentAsync(
        string name,
        string model,
        string instructions,
        ToolDefinition[] tools,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instructions))
            throw new InvalidOperationException($"System prompt for agent '{name}' is missing or empty");

        var existingAgents = await _adminOps.GetAgentsAsync(ct);

        foreach (var (id, existingName) in existingAgents) {
            if (existingName == name) {
                _logger.LogInformation("Updating existing agent '{AgentName}' (id={AgentId})", name, id);
                return await _adminOps.UpdateAgentAsync(id, name, model, instructions, tools, ct);
            }
        }

        _logger.LogInformation("Creating new agent '{AgentName}'", name);
        return await _adminOps.CreateAgentAsync(name, model, instructions, tools, ct);
    }

    private static bool ShouldTryNextModel(RequestFailedException ex) {
        if (ex.Status is not (400 or 404))
            return false;

        var message = ex.Message;
        return message.Contains("model", StringComparison.OrdinalIgnoreCase)
            || message.Contains("deployment", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }
}
