using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class McpConfigurationProviderServiceCoverageTests
{
    [Fact]
    public async Task GetEnabledToolsAsync_WhenRepositorySucceeds_ReturnsEnabledTools()
    {
        var repository = CreateRepository();
        var tools = new[] { CreateTool("search"), CreateTool("validator") };
        repository.Setup(store => store.GetEnabledAsync()).ReturnsAsync(tools);
        using var sut = CreateSut(repository.Object);

        var result = await sut.GetEnabledToolsAsync();

        result.Should().BeSameAs(tools);
    }

    [Fact]
    public async Task GetEnabledToolsAsync_WhenRepositoryFails_WrapsTheFailure()
    {
        var repository = CreateRepository();
        repository.Setup(store => store.GetEnabledAsync()).ThrowsAsync(new InvalidOperationException("store unavailable"));
        using var sut = CreateSut(repository.Object);

        var act = () => sut.GetEnabledToolsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Error retrieving enabled MCP tools");
    }

    [Fact]
    public async Task GetToolConfigurationAsync_WhenToolIsEnabled_ReturnsConfiguration()
    {
        var repository = CreateRepository();
        var tool = CreateTool("search");
        repository.Setup(store => store.GetByToolIdAsync("search")).ReturnsAsync(tool);
        using var sut = CreateSut(repository.Object);

        var result = await sut.GetToolConfigurationAsync("search");

        result.Should().BeSameAs(tool);
    }

    [Fact]
    public async Task GetToolConfigurationAsync_WhenToolIdIsBlankDisabledOrMissing_ReturnsNull()
    {
        var repository = CreateRepository();
        repository.Setup(store => store.GetByToolIdAsync("disabled"))
            .ReturnsAsync(CreateTool("disabled", enabled: false));
        repository.Setup(store => store.GetByToolIdAsync("missing")).ReturnsAsync((McpToolConfiguration?)null);
        using var sut = CreateSut(repository.Object);

        var blank = await sut.GetToolConfigurationAsync(" ");
        var disabled = await sut.GetToolConfigurationAsync("disabled");
        var missing = await sut.GetToolConfigurationAsync("missing");

        blank.Should().BeNull();
        disabled.Should().BeNull();
        missing.Should().BeNull();
        repository.Verify(store => store.GetByToolIdAsync(It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetToolConfigurationAsync_WhenRepositoryFails_WrapsTheFailure()
    {
        var repository = CreateRepository();
        repository.Setup(store => store.GetByToolIdAsync("failure"))
            .ThrowsAsync(new InvalidOperationException("store unavailable"));
        using var sut = CreateSut(repository.Object);

        var act = () => sut.GetToolConfigurationAsync("failure");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Error retrieving tool configuration for failure");
    }

    [Fact]
    public async Task GetToolsByTypeAsync_WhenToolTypeIsValid_ReturnsRepositoryValues()
    {
        var repository = CreateRepository();
        var tools = new[] { CreateTool("search") };
        repository.Setup(store => store.GetByTypeAsync("search")).ReturnsAsync(tools);
        using var sut = CreateSut(repository.Object);

        var result = await sut.GetToolsByTypeAsync("search");

        result.Should().BeSameAs(tools);
    }

    [Fact]
    public async Task GetToolsByTypeAsync_WhenTypeIsBlankOrRepositoryFails_ReturnsEmptyOrWrapsFailure()
    {
        var repository = CreateRepository();
        repository.Setup(store => store.GetByTypeAsync("failure"))
            .ThrowsAsync(new InvalidOperationException("store unavailable"));
        using var sut = CreateSut(repository.Object);

        var blank = await sut.GetToolsByTypeAsync(" ");
        var failing = () => sut.GetToolsByTypeAsync("failure");

        blank.Should().BeEmpty();
        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("Error retrieving tools by type failure");
    }

    [Fact]
    public async Task RefreshAsync_UpdatesLastRefreshedTimestamp()
    {
        using var sut = CreateSut(CreateRepository().Object);
        var before = sut.LastRefreshed;

        await sut.RefreshAsync();

        sut.LastRefreshed.Should().BeOnOrAfter(before);
    }

    [Theory]
    [InlineData(false, "https://tools.example", 30000, false)]
    [InlineData(true, null, 30000, false)]
    [InlineData(true, "/relative", 30000, false)]
    [InlineData(true, "ftp://tools.example", 30000, false)]
    [InlineData(true, "https://tools.example", 0, false)]
    [InlineData(true, "https://tools.example", 30000, true)]
    public async Task ValidateToolAsync_ReturnsExpectedResultForAccessibleConfiguration(
        bool enabled,
        string? serverUrl,
        int timeoutMs,
        bool expected)
    {
        using var sut = CreateSut(CreateRepository().Object);
        var tool = CreateTool("tool", enabled: enabled, serverUrl: serverUrl, timeoutMs: timeoutMs);

        var result = await sut.ValidateToolAsync(tool);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task ValidateToolAsync_WhenToolIsNull_ReturnsFalse()
    {
        using var sut = CreateSut(CreateRepository().Object);

        var result = await sut.ValidateToolAsync(null!);

        result.Should().BeFalse();
    }

    [Fact]
    public void ConstructorAndDispose_WhenArgumentsOrCallsAreInvalid_EnforceLifetimeRules()
    {
        var repository = CreateRepository().Object;

        ((Action)(() => new McpConfigurationProvider(null!, NullLogger<McpConfigurationProvider>.Instance)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new McpConfigurationProvider(repository, null!)))
            .Should().Throw<ArgumentNullException>();

        var sut = CreateSut(repository);
        sut.Dispose();
        sut.Invoking(provider => provider.Dispose()).Should().NotThrow();
    }

    private static Mock<IToolConfigurationRepository> CreateRepository() => new(MockBehavior.Strict);

    private static McpConfigurationProvider CreateSut(IToolConfigurationRepository repository) => new(
        repository,
        NullLogger<McpConfigurationProvider>.Instance);

    private static McpToolConfiguration CreateTool(
        string toolId,
        bool enabled = true,
        string? serverUrl = "https://tools.example",
        int? timeoutMs = 30000) => new()
    {
        Id = Guid.NewGuid(),
        ToolId = toolId,
        Name = toolId,
        ToolType = "search",
        IsEnabled = enabled,
        ServerUrl = serverUrl is null ? null! : new Uri(serverUrl, UriKind.RelativeOrAbsolute),
        TimeoutMs = timeoutMs,
    };
}
