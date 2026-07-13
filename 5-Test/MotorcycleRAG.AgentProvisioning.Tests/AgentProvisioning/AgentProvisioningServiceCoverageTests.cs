using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.Projects.Agents;
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
    public void Constructor_MissingEndpoint_Throws()
    {
        var options = AgentProvisioningModelOptions.Default;
        var logger = NullLogger<AgentProvisioningService>.Instance;
        Assert.Throws<InvalidOperationException>(() => new AgentProvisioningService("", options, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var options = AgentProvisioningModelOptions.Default;
        Assert.Throws<ArgumentNullException>(() => new AgentProvisioningService("endpoint", options, null!));
    }
    
    [Fact]
    public async Task CreateAgentVersionWithFallbackAsync_ThrowsIfNoCandidates()
    {
        var mockOps = new Mock<IAgentAdminOperations>();
        var logger = NullLogger<AgentProvisioningService>.Instance;
        var service = new AgentProvisioningService(mockOps.Object, logger);

        // Reflection to call private method CreateAgentVersionWithFallbackAsync
        var method = typeof(AgentProvisioningService).GetMethod("CreateAgentVersionWithFallbackAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = (Task<ProvisionedAgentReference>)method.Invoke(service, new object[] { 
            "name", new List<string>(), "inst", Array.Empty<ResponseTool>(), CancellationToken.None 
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Contains("No model deployment candidates configured", ex.Message);
    }
    
    [Fact]
    public void IsModelRejection_Works()
    {
        var method = typeof(AgentProvisioningService).GetMethod("IsModelRejection", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.True((bool)method.Invoke(null, new object[] { 400, "model not found" }));
        Assert.True((bool)method.Invoke(null, new object[] { 404, "deployment invalid" }));
        Assert.False((bool)method.Invoke(null, new object[] { 500, "model" }));
        Assert.False((bool)method.Invoke(null, new object[] { 400, "other error" }));
    }

    [Fact]
    public void ShouldTryNextModel_Works()
    {
        var method = typeof(AgentProvisioningService).GetMethod("ShouldTryNextModel", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var reqFailed = new RequestFailedException(404, "model not found");
        Assert.True((bool)method.Invoke(null, new object[] { reqFailed }));
        
        var otherEx = new Exception("model not found");
        Assert.False((bool)method.Invoke(null, new object[] { otherEx }));
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
        var service = new AgentProvisioningService(mockOps.Object, logger, options);

        var result = await service.ProvisionAllAgentsAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Orchestrator);
        Assert.NotNull(result.VectorSearch);
    }
}
