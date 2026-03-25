using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Validator for Azure AI configuration
/// </summary>
internal class AzureAIConfigurationValidator : IValidateOptions<AzureFoundryOptions>
{
    ValidateOptionsResult IValidateOptions<AzureFoundryOptions>.Validate(string? name, AzureFoundryOptions options)
    {
        var failures = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.FoundryEndpoint) && !Uri.TryCreate(options.FoundryEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:FoundryEndpoint must be a valid URL if provided");

        if (string.IsNullOrWhiteSpace(options.SearchServiceEndpoint))
            failures.Add("AzureAI:SearchServiceEndpoint is required");
        else if (!Uri.TryCreate(options.SearchServiceEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:SearchServiceEndpoint must be a valid URL");

        if (string.IsNullOrWhiteSpace(options.DocumentIntelligenceEndpoint))
            failures.Add("AzureAI:DocumentIntelligenceEndpoint is required");
        else if (!Uri.TryCreate(options.DocumentIntelligenceEndpoint, UriKind.Absolute, out _))
            failures.Add("AzureAI:DocumentIntelligenceEndpoint must be a valid URL");

        if (options.Models == null)
            failures.Add("AzureAI:Models configuration is required");
        else {
            if (string.IsNullOrWhiteSpace(options.Models.ChatModel))
                failures.Add("AzureAI:Models:ChatModel is required");
            if (string.IsNullOrWhiteSpace(options.Models.EmbeddingModel))
                failures.Add("AzureAI:Models:EmbeddingModel is required");
            if (options.Models.MaxTokens <= 0)
                failures.Add("AzureAI:Models:MaxTokens must be greater than 0");
            if (options.Models.Temperature < 0 || options.Models.Temperature > 2)
                failures.Add("AzureAI:Models:Temperature must be between 0 and 2");
        }

        // Foundry agent IDs — written by the deploy pipeline to Key Vault
        // and injected via environment config at runtime. Fail-fast on startup if missing.
        if (string.IsNullOrWhiteSpace(options.OrchestratorAgentId))
            failures.Add("AzureAI:OrchestratorAgentId is required (written by deploy pipeline to Key Vault)");

        if (string.IsNullOrWhiteSpace(options.VectorSearchAgentId))
            failures.Add("AzureAI:VectorSearchAgentId is required (written by deploy pipeline to Key Vault)");

        if (string.IsNullOrWhiteSpace(options.WebSearchAgentId))
            failures.Add("AzureAI:WebSearchAgentId is required (written by deploy pipeline to Key Vault)");

        if (string.IsNullOrWhiteSpace(options.PDFSearchAgentId))
            failures.Add("AzureAI:PDFSearchAgentId is required (written by deploy pipeline to Key Vault)");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}