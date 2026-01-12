using MotorcycleRAG.Application.Services.Mcp;
using MotorcycleRAG.Application.Services.Telemetry;
using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Groups AgentOrchestrator dependencies to keep the orchestrator constructor small.
/// </summary>
public sealed class AgentOrchestratorDependencies
{
    public IAzureOpenAIClient OpenAIClient { get; }
    public McpToolManager McpToolManager { get; }
    public DegradedModeTracker DegradedModeTracker { get; }
    public SearchResultFusionService ResultFusionService { get; }

    public AgentOrchestratorDependencies(
        IAzureOpenAIClient openAIClient,
        McpToolManager mcpToolManager,
        DegradedModeTracker degradedModeTracker,
        SearchResultFusionService resultFusionService)
    {
        OpenAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        McpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
        DegradedModeTracker = degradedModeTracker ?? throw new ArgumentNullException(nameof(degradedModeTracker));
        ResultFusionService = resultFusionService ?? throw new ArgumentNullException(nameof(resultFusionService));
    }
}
