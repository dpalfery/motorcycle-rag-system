using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.Agents;

/// <summary>
/// Integration tests for <see cref="FoundryAgentRunner"/> against a live Azure AI Foundry endpoint.
/// All tests are skipped by default. Configure AzureAI:* values through approved configuration
/// to run against a real environment.
/// Authentication uses DefaultAzureCredential (managed identity / az login).
/// </summary>
[Trait("Category", "Integration")]
public class FoundryAgentRunnerIntegrationTests
{
    private static IFoundryAgentRunner? CreateRunner()
    {
        var configuration = LoadIntegrationConfiguration();
        var endpoint = configuration["AzureAI:FoundryEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return null;

        var options = Options.Create(new AzureFoundryOptions
        {
            FoundryEndpoint = endpoint,
            SearchServiceEndpoint = "https://placeholder.search.windows.net/",
            DocumentIntelligenceEndpoint = "https://placeholder.cognitiveservices.azure.com/",
            OrchestratorAgentName = configuration["AzureAI:OrchestratorAgentName"] ?? string.Empty,
            VectorSearchAgentName = configuration["AzureAI:VectorSearchAgentName"] ?? string.Empty,
            WebSearchAgentName = configuration["AzureAI:WebSearchAgentName"] ?? string.Empty,
            PDFSearchAgentName = configuration["AzureAI:PDFSearchAgentName"] ?? string.Empty,
            Models = new ModelOptions { MaxTokens = 4096, Temperature = 0.1f },
            Retry = new RetryOptions { MaxRetries = 3 }
        });

        return new FoundryAgentRunner(
            options,
            new MotorcycleRAG.Persistence.Azure.FoundryClientFactory(),
            NullLogger<FoundryAgentRunner>.Instance);
    }

    private static IConfiguration LoadIntegrationConfiguration() {
        return new ConfigurationBuilder()
            .AddJsonFile("appsettings.Integration.json", optional: true)
            .Build();
    }

    [Fact(Skip = "Integration test - requires AzureAI:FoundryEndpoint configuration and az login")]
    public async Task CreateConversationAsync_WithLiveFoundry_ReturnsNonEmptyConversationId()
    {
        var runner = CreateRunner();
        if (runner == null)
        {
            Assert.Fail("AzureAI:FoundryEndpoint is not configured");
            return;
        }

        var conversationId = await runner.CreateConversationAsync();

        Assert.False(string.IsNullOrWhiteSpace(conversationId));

        await runner.DeleteConversationAsync(conversationId);
    }

    [Fact(Skip = "Integration test - requires AzureAI:FoundryEndpoint and AzureAI:OrchestratorAgentName configuration plus az login")]
    public async Task SendAgentMessageAsync_WithLiveOrchestratorAgent_ReturnsCompletedOrRequiresAction()
    {
        var runner = CreateRunner();
        if (runner == null)
        {
            Assert.Fail("AzureAI:FoundryEndpoint is not configured");
            return;
        }

        var orchestratorAgentName = LoadIntegrationConfiguration()["AzureAI:OrchestratorAgentName"];
        if (string.IsNullOrWhiteSpace(orchestratorAgentName))
        {
            Assert.Fail("AzureAI:OrchestratorAgentName is not configured");
            return;
        }

        var conversationId = await runner.CreateConversationAsync();
        try
        {
            var status = await runner.SendAgentMessageAsync(
                conversationId,
                orchestratorAgentName,
                "Honda CBR1000RR engine specifications");

            Assert.True(
                status.State == AgentRunState.Completed ||
                status.State == AgentRunState.RequiresAction,
                $"Expected Completed or RequiresAction but got {status.State}");

            Assert.False(string.IsNullOrWhiteSpace(status.ResponseId));
        }
        finally
        {
            await runner.DeleteConversationAsync(conversationId);
        }
    }

    [Fact(Skip = "Integration test - requires AzureAI:FoundryEndpoint configuration and az login")]
    public async Task DeleteConversationAsync_WithValidConversation_DoesNotThrow()
    {
        var runner = CreateRunner();
        if (runner == null)
        {
            Assert.Fail("AzureAI:FoundryEndpoint is not configured");
            return;
        }

        var conversationId = await runner.CreateConversationAsync();

        await runner.DeleteConversationAsync(conversationId);
    }
}
