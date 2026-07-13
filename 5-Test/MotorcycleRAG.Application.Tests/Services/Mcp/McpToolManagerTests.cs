using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Mcp;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Mcp;

public class McpToolManagerTests
{
    private readonly Mock<IToolConfigurationService> _configServiceMock;
    private readonly McpToolManager _sut;

    public McpToolManagerTests()
    {
        _configServiceMock = new Mock<IToolConfigurationService>();
        _sut = new McpToolManager(_configServiceMock.Object, NullLogger<McpToolManager>.Instance);
    }

    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        var logger = NullLogger<McpToolManager>.Instance;
        Assert.Throws<ArgumentNullException>(() => new McpToolManager(null!, logger));
        Assert.Throws<ArgumentNullException>(() => new McpToolManager(_configServiceMock.Object, null!));
    }

    [Fact]
    public async Task GetEnabledToolsAsync_ReturnsFromService()
    {
        var configs = new[] { new McpToolConfiguration { ToolId = "1", IsEnabled = true } };
        _configServiceMock.Setup(x => x.GetEnabledToolsAsync()).ReturnsAsync(configs);

        var result = await _sut.GetEnabledToolsAsync();
        
        result.Should().HaveCount(1);
    }
}
