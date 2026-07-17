using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.AgentProvisioning.Azure;

namespace MotorcycleRAG.UnitTests.AgentProvisioning;

public sealed class AgentProvisioningServiceCoverageTests
{
    [Fact]
    public void Constructor_WhenAdminOperationsIsNull_Throws()
    {
        // Arrange
        var options = AgentProvisioningModelOptions.Default;

        // Act
        var act = () => new AgentProvisioningService(null!, options, NullLogger<AgentProvisioningService>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("adminOps");
    }

    [Fact]
    public void Constructor_WhenModelOptionsIsNull_Throws()
    {
        // Arrange
        var operations = new Mock<IAgentAdminOperations>();

        // Act
        var act = () => new AgentProvisioningService(operations.Object, null!, NullLogger<AgentProvisioningService>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("modelOptions");
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_Throws()
    {
        // Arrange
        var operations = new Mock<IAgentAdminOperations>();

        // Act
        var act = () => new AgentProvisioningService(operations.Object, AgentProvisioningModelOptions.Default, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Composition_WhenEndpointIsValid_CreatesFoundryAdapter()
    {
        // Act
        var operations = AgentProvisioningComposition.CreateAdminOperations(new Uri("https://example.services.ai.azure.com"));

        // Assert
        operations.Should().BeOfType<FoundryAgentAdminClientAdapter>();
    }
}
