using Azure;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.AgentProvisioning.Azure;
using System.ClientModel;
using System.Text.Json;

// -------------------------------------------------------------------------
// MotorcycleRAG.AgentProvisioning
//
// CLI entry point for the GitHub Actions deploy pipeline.
// Reads Foundry configuration from environment variables, provisions all four
// Foundry agents via AgentProvisioningService, then writes the assigned agent
// names and versions to stdout as JSON for the pipeline to capture.
//
// Required environment variable:
//   AZURE_FOUNDRY_ENDPOINT          — Azure AI Foundry project endpoint URL
// Optional environment variables:
//   ORCHESTRATOR_MODEL_DEPLOYMENTS  — Comma-separated deployment names, preferred first
//   SUBAGENT_MODEL_DEPLOYMENT       — Deployment name for vector/web/pdf agents
//
// Outputs (stdout, JSON):
//   {
//     "orchestratorAgentName": "...",
//     "orchestratorAgentVersion": "...",
//     "vectorSearchAgentName": "...",
//     "vectorSearchAgentVersion": "...",
//     "webSearchAgentName": "...",
//     "webSearchAgentVersion": "...",
//     "pdfSearchAgentName": "...",
//     "pdfSearchAgentVersion": "..."
//   }
//
// Exit codes:
//   0 — success
//   1 — configuration error or provisioning failure
// -------------------------------------------------------------------------

internal static class Program
{
    public static Task<int> Main(string[] args) => RunAsync();

    internal static async Task<int> RunAsync(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<Uri, IAgentAdminOperations>? createAdminOperations = null,
        Action<string>? writeOutput = null,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        createAdminOperations ??= AgentProvisioningComposition.CreateAdminOperations;
        writeOutput ??= Console.WriteLine;
        delayAsync ??= Task.Delay;

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("AgentProvisioning");

        try
        {
            var foundryEndpoint = getEnvironmentVariable("AZURE_FOUNDRY_ENDPOINT");
            if (string.IsNullOrWhiteSpace(foundryEndpoint))
            {
                logger.LogError("AZURE_FOUNDRY_ENDPOINT environment variable is required");
                return 1;
            }

            var modelOptions = AgentProvisioningModelOptions.FromEnvironment(getEnvironmentVariable);

            var provisioningLogger = loggerFactory.CreateLogger<AgentProvisioningService>();
            var adminOperations = createAdminOperations(new Uri(foundryEndpoint));
            var service = new AgentProvisioningService(adminOperations, modelOptions, provisioningLogger);

            logger.LogInformation(
                "Starting agent provisioning against {Endpoint} with orchestrator model candidates {OrchestratorCandidates} and subagent model {SubAgentModel}",
                foundryEndpoint,
                string.Join(",", modelOptions.OrchestratorModelCandidates),
                modelOptions.SubAgentModel);

            var agentReferences = await ProvisionWithPermissionRetryAsync(service, logger, delayAsync);

            logger.LogInformation("Provisioning complete");

            // Write agent references as JSON to stdout — captured by the pipeline
            var output = new
            {
                orchestratorAgentName = agentReferences.Orchestrator.Name,
                orchestratorAgentVersion = agentReferences.Orchestrator.Version,
                vectorSearchAgentName = agentReferences.VectorSearch.Name,
                vectorSearchAgentVersion = agentReferences.VectorSearch.Version,
                webSearchAgentName = agentReferences.WebSearch.Name,
                webSearchAgentVersion = agentReferences.WebSearch.Version,
                pdfSearchAgentName = agentReferences.PDFSearch.Name,
                pdfSearchAgentVersion = agentReferences.PDFSearch.Version,
                graphQueryAgentName = agentReferences.GraphQuery.Name,
                graphQueryAgentVersion = agentReferences.GraphQuery.Version
            };

            writeOutput(JsonSerializer.Serialize(output));
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent provisioning failed");
            return 1;
        }
    }

    internal static async Task<ProvisionedAgentReferences> ProvisionWithPermissionRetryAsync(
        AgentProvisioningService service,
        ILogger logger,
        Func<TimeSpan, Task> delayAsync)
    {
        const int maxAttempts = 6;
        var delay = TimeSpan.FromSeconds(30);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await service.ProvisionAllAgentsAsync();
            }
            catch (Exception ex) when (attempt < maxAttempts && IsPermissionPropagationFailure(ex))
            {
                logger.LogWarning(
                    ex,
                    "Foundry data-plane RBAC is not available yet. Retrying agent provisioning in {DelaySeconds} seconds ({Attempt}/{MaxAttempts}).",
                    delay.TotalSeconds,
                    attempt,
                    maxAttempts);
                await delayAsync(delay);
            }
        }

        throw new InvalidOperationException("Agent provisioning retry loop exhausted unexpectedly.");
    }

    internal static bool IsPermissionPropagationFailure(Exception ex)
    {
        if (ex is RequestFailedException requestFailed)
        {
            return requestFailed.Status == 401
                && string.Equals(requestFailed.ErrorCode, "PermissionDenied", StringComparison.OrdinalIgnoreCase);
        }

        if (ex is ClientResultException clientResult)
        {
            return clientResult.Status == 401
                && clientResult.Message.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
