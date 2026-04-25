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
            OrchestratorAgentId = configuration["AzureAI:OrchestratorAgentId"] ?? string.Empty,
            VectorSearchAgentId = configuration["AzureAI:VectorSearchAgentId"] ?? string.Empty,
            WebSearchAgentId = configuration["AzureAI:WebSearchAgentId"] ?? string.Empty,
            PDFSearchAgentId = configuration["AzureAI:PDFSearchAgentId"] ?? string.Empty,
            Models = new ModelOptions { MaxTokens = 4096, Temperature = 0.1f },
            Retry = new RetryOptions { MaxRetries = 3 }
        });

        return new FoundryAgentRunner(options, NullLogger<FoundryAgentRunner>.Instance);
    }

    private static IConfiguration LoadIntegrationConfiguration() {
        return new ConfigurationBuilder()
            .AddJsonFile("appsettings.Integration.json", optional: true)
            .Build();
    }

    [Fact(Skip = "Integration test - requires AzureAI:FoundryEndpoint configuration and az login")]
    public async Task CreateThreadAsync_WithLiveFoundry_ReturnsNonEmptyThreadId()
    {
        var runner = CreateRunner();
        if (runner == null)
        {
            Assert.Fail("AzureAI:FoundryEndpoint is not configured");
            return;
        }

        // Act
        var threadId = await runner.CreateThreadAsync();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(threadId));

        // Cleanup
        await runner.DeleteThreadAsync(threadId);
    }

    [Fact(Skip = "Integration test - requires AzureAI:FoundryEndpoint configuration and az login")]
    public async Task AddUserMessageAsync_WithLiveFoundry_DoesNotThrow()
    {
        var runner = CreateRunner();
        if (runner == null)
        {
            Assert.Fail("AzureAI:FoundryEndpoint is not configured");
            return;
        }

        var threadId = await runner.CreateThreadAsync();
        try
        {
            // Act & Assert — no exception
            await runner.AddUserMessageAsync(threadId, "What is the oil capacity of a Honda CBR1000RR?");
        }
        finally
        {
            await runner.DeleteThreadAsync(threadId);
        }
    }

    [Fact(Skip = "Integration test - requires AzureAI:FoundryEndpoint and AzureAI:OrchestratorAgentId configuration plus az login")]
    public async Task CreateRunAsync_WithLiveOrchestratorAgent_ReturnsCompletedOrRequiresAction()
    {
        var runner = CreateRunner();
        if (runner == null)
        {
            Assert.Fail("AzureAI:FoundryEndpoint is not configured");
            return;
        }

        var orchestratorAgentId = LoadIntegrationConfiguration()["AzureAI:OrchestratorAgentId"];
        if (string.IsNullOrWhiteSpace(orchestratorAgentId))
        {
            Assert.Fail("AzureAI:OrchestratorAgentId is not configured");
            return;
        }

        var threadId = await runner.CreateThreadAsync();
        try
        {
            await runner.AddUserMessageAsync(threadId, "Honda CBR1000RR engine specifications");
            var status = await runner.CreateRunAsync(threadId, orchestratorAgentId);

            // Assert — run must be in a valid terminal or action-required state
            Assert.True(
                status.State == AgentRunState.Completed ||
                status.State == AgentRunState.RequiresAction,
                $"Expected Completed or RequiresAction but got {status.State}");

            Assert.False(string.IsNullOrWhiteSpace(status.RunId));
        }
        finally
        {
            await runner.DeleteThreadAsync(threadId);
        }
    }

    [Fact(Skip = "Integration test - requires AzureAI:FoundryEndpoint configuration and az login")]
    public async Task DeleteThreadAsync_WithValidThread_DoesNotThrow()
    {
        var runner = CreateRunner();
        if (runner == null)
        {
            Assert.Fail("AzureAI:FoundryEndpoint is not configured");
            return;
        }

        var threadId = await runner.CreateThreadAsync();

        // Act & Assert — deletion should not throw
        await runner.DeleteThreadAsync(threadId);
    }
}
