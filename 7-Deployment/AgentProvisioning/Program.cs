using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;
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
//   AZURE_FOUNDRY_ENDPOINT  — Azure AI Foundry project endpoint URL
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

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger("AgentProvisioning");

try
{
    // Build configuration from environment variables only
    // Secrets never in source — all values from env / Key Vault
    var configuration = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .Build();

    var foundryEndpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_ENDPOINT");
    if (string.IsNullOrWhiteSpace(foundryEndpoint))
    {
        logger.LogError("AZURE_FOUNDRY_ENDPOINT environment variable is required");
        Environment.Exit(1);
    }

    var options = Options.Create(new AzureFoundryOptions
    {
        FoundryEndpoint = foundryEndpoint,
        // Other options are not needed for agent provisioning
        SearchServiceEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? string.Empty,
        DocumentIntelligenceEndpoint = Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT") ?? string.Empty,
        Models = new ModelOptions
        {
            MaxTokens = 4096,
            Temperature = 0.1f
        },
        Retry = new RetryOptions
        {
            MaxRetries = 3
        }
    });

    var provisioningLogger = loggerFactory.CreateLogger<AgentProvisioningService>();
    var service = new AgentProvisioningService(options, provisioningLogger);

    logger.LogInformation("Starting agent provisioning against {Endpoint}", foundryEndpoint);

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
