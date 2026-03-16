namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Agent IDs returned by <see cref="AgentProvisioningService.ProvisionAllAgentsAsync"/>
/// after all four Foundry agents have been created or updated.
/// </summary>
public record ProvisionedAgentIds(
    string OrchestratorAgentId,
    string VectorSearchAgentId,
    string WebSearchAgentId,
    string PDFSearchAgentId);
