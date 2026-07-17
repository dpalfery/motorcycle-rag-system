using Azure;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using System.ClientModel;

namespace MotorcycleRAG.AgentProvisioning.Azure;

/// <summary>
/// Creates new versions for all five Foundry agent definitions (OrchestratorAgent, VectorSearchAgent,
/// WebSearchAgent, PDFSearchAgent, GraphQueryAgent) using the Microsoft Foundry Agents SDK.
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
    private readonly string _graphQuerySystemPrompt;

    /// <summary>
    /// Creates the provisioning service with the Foundry administration operations supplied by the CLI composition root.
    /// </summary>
    public AgentProvisioningService(
        IAgentAdminOperations adminOps,
        AgentProvisioningModelOptions modelOptions,
        ILogger<AgentProvisioningService> logger,
        string? orchestratorSystemPrompt = null,
        string? vectorSearchSystemPrompt = null,
        string? webSearchSystemPrompt = null,
        string? pdfSearchSystemPrompt = null,
        string? graphQuerySystemPrompt = null) {
        _adminOps = adminOps ?? throw new ArgumentNullException(nameof(adminOps));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modelOptions = modelOptions ?? throw new ArgumentNullException(nameof(modelOptions));
        _orchestratorSystemPrompt = orchestratorSystemPrompt ?? AgentDefinitions.OrchestratorSystemPrompt;
        _vectorSearchSystemPrompt = vectorSearchSystemPrompt ?? AgentDefinitions.VectorSearchSystemPrompt;
        _webSearchSystemPrompt = webSearchSystemPrompt ?? AgentDefinitions.WebSearchSystemPrompt;
        _pdfSearchSystemPrompt = pdfSearchSystemPrompt ?? AgentDefinitions.PDFSearchSystemPrompt;
        _graphQuerySystemPrompt = graphQuerySystemPrompt ?? AgentDefinitions.GraphQuerySystemPrompt;
    }

    /// <summary>
    /// Creates or updates all four Foundry agents and returns their assigned IDs.
    /// </summary>
    public async Task<ProvisionedAgentReferences> ProvisionAllAgentsAsync(CancellationToken ct = default) {
        _logger.LogInformation("Starting Foundry agent provisioning");

        ProvisionedAgentReference orchestrator = null!;
        foreach (var model in _modelOptions.OrchestratorModelCandidates) {
            try {
                orchestrator = await CreateAgentVersionAsync(
                    AgentDefinitions.OrchestratorAgentName,
                    model,
                    _orchestratorSystemPrompt,
                    AgentDefinitions.OrchestratorTools,
                    ct);
                _logger.LogInformation("Successfully created agent version for model {Model}", model);
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to create agent version for model {Model}", model);
            }
        }
        
        if (orchestrator == null) {
            throw new InvalidOperationException("Failed to create any orchestrator agent versions.");
        }

        var vectorSearch = await CreateAgentVersionAsync(
            AgentDefinitions.VectorSearchAgentName,
            _modelOptions.SubAgentModel,
            _vectorSearchSystemPrompt,
            AgentDefinitions.VectorSearchTools,
            ct);

        var webSearch = await CreateAgentVersionAsync(
            AgentDefinitions.WebSearchAgentName,
            _modelOptions.SubAgentModel,
            _webSearchSystemPrompt,
            AgentDefinitions.WebSearchTools,
            ct);

        var pdfSearch = await CreateAgentVersionAsync(
            AgentDefinitions.PDFSearchAgentName,
            _modelOptions.SubAgentModel,
            _pdfSearchSystemPrompt,
            AgentDefinitions.PDFSearchTools,
            ct);

        var graphQuery = await CreateAgentVersionAsync(
            AgentDefinitions.GraphQueryAgentName,
            AgentDefinitions.GraphQueryModel,
            _graphQuerySystemPrompt,
            AgentDefinitions.GraphQueryTools,
            ct);

        _logger.LogInformation(
            "Agent provisioning complete: orchestrator={OrchestratorName}@{OrchestratorVersion} vectorSearch={VectorName}@{VectorVersion} webSearch={WebName}@{WebVersion} pdfSearch={PdfName}@{PdfVersion} graphQuery={GraphName}@{GraphVersion}",
            orchestrator.Name, orchestrator.Version,
            vectorSearch.Name, vectorSearch.Version,
            webSearch.Name, webSearch.Version,
            pdfSearch.Name, pdfSearch.Version,
            graphQuery.Name, graphQuery.Version);

        return new ProvisionedAgentReferences(orchestrator, vectorSearch, webSearch, pdfSearch, graphQuery);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    internal async Task<ProvisionedAgentReference> CreateAgentVersionWithFallbackAsync(
        string name,
        IReadOnlyList<string> modelCandidates,
        string instructions,
        ResponseTool[] tools,
        CancellationToken ct) {
        if (modelCandidates.Count == 0)
            throw new InvalidOperationException($"No model deployment candidates configured for agent '{name}'");

        Exception? lastRejectedModel = null;
        foreach (var model in modelCandidates) {
            try {
                return await CreateAgentVersionAsync(name, model, instructions, tools, ct);
            }
            catch (Exception ex) when (ShouldTryNextModel(ex)) {
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

    private async Task<ProvisionedAgentReference> CreateAgentVersionAsync(
        string name,
        string model,
        string instructions,
        ResponseTool[] tools,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instructions))
            throw new InvalidOperationException($"System prompt for agent '{name}' is missing or empty");

        var existingAgentNames = await _adminOps.GetAgentNamesAsync(ct);
        var operation = existingAgentNames.Contains(name, StringComparer.Ordinal)
            ? "Creating new version for existing"
            : "Creating first version for";

        _logger.LogInformation("{Operation} agent '{AgentName}' with model deployment '{ModelDeployment}'", operation, name, model);
        return await _adminOps.CreateAgentVersionAsync(name, model, instructions, tools, ct);
    }

    internal static bool ShouldTryNextModel(Exception ex) {
        if (ex is RequestFailedException requestFailed)
            return IsModelRejection(requestFailed.Status, requestFailed.Message);

        if (ex is ClientResultException clientResult)
            return IsModelRejection(clientResult.Status, clientResult.Message);

        return false;
    }

    internal static bool IsModelRejection(int status, string message) {
        if (status is not (400 or 404))
            return false;

        return message.Contains("model", StringComparison.OrdinalIgnoreCase)
            || message.Contains("deployment", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }
}
