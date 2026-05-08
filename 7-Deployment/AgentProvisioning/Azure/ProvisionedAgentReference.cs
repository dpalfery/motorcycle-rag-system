namespace MotorcycleRAG.AgentProvisioning.Azure;

/// <summary>
/// Stable Foundry agent reference returned by versioned-agent provisioning.
/// </summary>
public record ProvisionedAgentReference(string Name, string Version);

/// <summary>
/// Agent references returned by <see cref="AgentProvisioningService.ProvisionAllAgentsAsync"/>
/// after all four Foundry agents have been versioned.
/// </summary>
public record ProvisionedAgentReferences(
    ProvisionedAgentReference Orchestrator,
    ProvisionedAgentReference VectorSearch,
    ProvisionedAgentReference WebSearch,
    ProvisionedAgentReference PDFSearch);
