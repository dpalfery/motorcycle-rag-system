using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.AgentProvisioning.Azure;
using Xunit;
using OpenAI.Responses;
using System.ClientModel;

namespace MotorcycleRAG.UnitTests.AgentProvisioning;

public class AgentProvisioningServiceCoverageTests
{
    [Fact]
    public void Constructor_NullAdminOperations_Throws()
    {
        var options = AgentProvisioningModelOptions.Default;
        var logger = NullLogger<AgentProvisioningService>.Instance;
        Assert.Throws<ArgumentNullException>(() => new AgentProvisioningService(null!, options, logger));
    }

    [Fact]
    public void Constructor_NullModelOptions_Throws()
    {
        var mockOps = new Mock<IAgentAdminOperations>();
        var logger = NullLogger<AgentProvisioningService>.Instance;
        Assert.Throws<ArgumentNullException>(() => new AgentProvisioningService(mockOps.Object, null!, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var mockOps = new Mock<IAgentAdminOperations>();
        Assert.Throws<ArgumentNullException>(() => new AgentProvisioningService(
            mockOps.Object,
            AgentProvisioningModelOptions.Default,
            null!));
    }

    [Fact]
    public void Composition_CreateAdminOperations_ReturnsFoundryAdapter()
    {
        var adminOperations = AgentProvisioningComposition.CreateAdminOperations(new Uri("https://example.services.ai.azure.com"));

        Assert.IsType<FoundryAgentAdminClientAdapter>(adminOperations);
    }
    
    [Fact]
    public async Task CreateAgentVersionWithFallbackAsync_ThrowsIfNoCandidates()
    {
        var mockOps = new Mock<IAgentAdminOperations>();
        var logger = NullLogger<AgentProvisioningService>.Instance;
        var service = new AgentProvisioningService(mockOps.Object, AgentProvisioningModelOptions.Default, logger);

        // Reflection to call private method CreateAgentVersionWithFallbackAsync
        var method = typeof(AgentProvisioningService).GetMethod(
            "CreateAgentVersionWithFallbackAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("Expected fallback method was not found.");

        var task = method.Invoke(service, [
            "name", new List<string>(), "inst", Array.Empty<ResponseTool>(), CancellationToken.None
        ]) as Task<ProvisionedAgentReference>
            ?? throw new InvalidOperationException("Expected fallback method to return a provisioning task.");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Contains("No model deployment candidates configured", ex.Message);
    }
    
    [Fact]
    public void IsModelRejection_Works()
    {
        var method = typeof(AgentProvisioningService).GetMethod(
            "IsModelRejection",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Expected model-rejection method was not found.");

        Assert.True(Assert.IsType<bool>(method.Invoke(null, [400, "model not found"])));
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [404, "deployment invalid"])));
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [500, "model"])));
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [400, "other error"])));
    }

    [Fact]
    public void ShouldTryNextModel_Works()
    {
        var method = typeof(AgentProvisioningService).GetMethod(
            "ShouldTryNextModel",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Expected model-fallback method was not found.");

        var reqFailed = new RequestFailedException(404, "model not found");
        Assert.True(Assert.IsType<bool>(method.Invoke(null, [reqFailed])));
        
        var otherEx = new InvalidOperationException("model not found");
        Assert.False(Assert.IsType<bool>(method.Invoke(null, [otherEx])));
    }

    [Fact]
    public async Task ProvisionAllAgentsAsync_Success_CoversMethods()
    {
        var mockOps = new Mock<IAgentAdminOperations>();
        mockOps.Setup(x => x.GetAgentNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "ExistingAgent" });
            
        mockOps.Setup(x => x.CreateAgentVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ResponseTool[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProvisionedAgentReference("AgentName", "1.0"));

        var options = AgentProvisioningModelOptions.Default;
        var logger = NullLogger<AgentProvisioningService>.Instance;
        var service = new AgentProvisioningService(mockOps.Object, options, logger);

        var result = await service.ProvisionAllAgentsAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Orchestrator);
        Assert.NotNull(result.VectorSearch);
    }
}
