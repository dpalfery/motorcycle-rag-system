using Microsoft.Extensions.Logging;
using MotorcycleRAG.AgentProvisioning.Azure;
using System.Text.Json;

// -------------------------------------------------------------------------
// MotorcycleRAG.AgentProvisioning
//
// CLI entry point for the GitHub Actions deploy pipeline.
// Reads Foundry configuration from environment variables, provisions all four
// Foundry agents via AgentProvisioningService, then writes the assigned agent
// IDs to stdout as JSON for the pipeline to capture and store in Key Vault.
//
// Required environment variable:
//   AZURE_FOUNDRY_ENDPOINT          — Azure AI Foundry project endpoint URL
// Optional environment variables:
//   ORCHESTRATOR_MODEL_DEPLOYMENTS  — Comma-separated deployment names, preferred first
//   SUBAGENT_MODEL_DEPLOYMENT       — Deployment name for vector/web/pdf agents
//
// Outputs (stdout, JSON):
//   {
//     "orchestratorAgentId": "...",
//     "vectorSearchAgentId": "...",
//     "webSearchAgentId":    "...",
//     "pdfSearchAgentId":    "..."
//   }
//
// Exit codes:
//   0 — success
//   1 — configuration error or provisioning failure
// -------------------------------------------------------------------------

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger("AgentProvisioning");

try
{
    var foundryEndpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_ENDPOINT");
    if (string.IsNullOrWhiteSpace(foundryEndpoint))
    {
        logger.LogError("AZURE_FOUNDRY_ENDPOINT environment variable is required");
        Environment.Exit(1);
    }

    var modelOptions = AgentProvisioningModelOptions.FromEnvironment(Environment.GetEnvironmentVariable);

    var provisioningLogger = loggerFactory.CreateLogger<AgentProvisioningService>();
    var service = new AgentProvisioningService(foundryEndpoint, modelOptions, provisioningLogger);

    logger.LogInformation(
        "Starting agent provisioning against {Endpoint} with orchestrator model candidates {OrchestratorCandidates} and subagent model {SubAgentModel}",
        foundryEndpoint,
        string.Join(",", modelOptions.OrchestratorModelCandidates),
        modelOptions.SubAgentModel);

    var agentIds = await service.ProvisionAllAgentsAsync();

    logger.LogInformation("Provisioning complete");

    // Write agent IDs as JSON to stdout — captured by the pipeline
    var output = new
    {
        orchestratorAgentId = agentIds.OrchestratorAgentId,
        vectorSearchAgentId = agentIds.VectorSearchAgentId,
        webSearchAgentId = agentIds.WebSearchAgentId,
        pdfSearchAgentId = agentIds.PDFSearchAgentId
    };

    Console.WriteLine(JsonSerializer.Serialize(output));

    Environment.Exit(0);
}
catch (Exception ex)
{
    logger.LogError(ex, "Agent provisioning failed");
    Environment.Exit(1);
}
