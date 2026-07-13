using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services.TrustedSources;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.TrustedSources;

public class DatabaseTrustedSourcesLoaderTests
{
    private readonly Mock<IWebSourceRepository> _mockRepo = new(MockBehavior.Strict);
    private readonly Mock<ILogger<DatabaseTrustedSourcesLoader>> _mockLogger = new();

    private DatabaseTrustedSourcesLoader CreateLoader() =>
        new(_mockRepo.Object, _mockLogger.Object);

    [Fact]
    public async Task LoadAsync_ReturnsOnlyEnabledAndIncludeInSearch()
    {
        // Arrange
        var sources = new[]
        {
            new WebSource { Id = 1, Name = "Enabled", Url = "https://a.com", IsEnabled = true, IncludeInSearch = true, TrustTier = 1 },
            new WebSource { Id = 2, Name = "Disabled", Url = "https://b.com", IsEnabled = false, IncludeInSearch = true, TrustTier = 1 },
            new WebSource { Id = 3, Name = "ExcludedFromSearch", Url = "https://c.com", IsEnabled = true, IncludeInSearch = false, TrustTier = 1 },
        };
        _mockRepo.Setup(r => r.GetAllWebSourcesAsync()).ReturnsAsync(sources);

        var loader = CreateLoader();

        // Act
        var result = await loader.LoadAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Enabled", result[0].Name);
    }

    [Theory]
    [InlineData(1, 0.95f)]
    [InlineData(2, 0.80f)]
    [InlineData(3, 0.65f)]
    [InlineData(4, 0.50f)]
    [InlineData(5, 0.35f)]
    public async Task LoadAsync_MapsTrustTierToCredibilityScore(int tier, float expectedScore)
    {
        // Arrange
        var sources = new[]
        {
            new WebSource { Id = 1, Name = "Source", Url = "https://a.com", IsEnabled = true, IncludeInSearch = true, TrustTier = tier }
        };
        _mockRepo.Setup(r => r.GetAllWebSourcesAsync()).ReturnsAsync(sources);
        var loader = CreateLoader();

        // Act
        var result = await loader.LoadAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal(expectedScore, result[0].CredibilityScore, precision: 2);
    }

    [Fact]
    public async Task LoadAsync_NoSourcesExist_ReturnsEmptyArray()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllWebSourcesAsync()).ReturnsAsync([]);
        var loader = CreateLoader();

        // Act
        var result = await loader.LoadAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAsync_SetsDefaultContentSelector()
    {
        // Arrange
        var sources = new[]
        {
            new WebSource { Id = 1, Name = "Source", Url = "https://a.com", IsEnabled = true, IncludeInSearch = true, TrustTier = 1 }
        };
        _mockRepo.Setup(r => r.GetAllWebSourcesAsync()).ReturnsAsync(sources);
        var loader = CreateLoader();

        // Act
        var result = await loader.LoadAsync();

        // Assert
        Assert.Equal("//p|//article|//div[@class='content']", result[0].ContentSelector);
    }

    [Fact]
    public async Task LoadAsync_SetsSearchUrlTemplate()
    {
        // Arrange
        var sources = new[]
        {
            new WebSource { Id = 1, Name = "Source", Url = "https://moto.com", IsEnabled = true, IncludeInSearch = true, TrustTier = 2 }
        };
        _mockRepo.Setup(r => r.GetAllWebSourcesAsync()).ReturnsAsync(sources);
        var loader = CreateLoader();

        // Act
        var result = await loader.LoadAsync();

        // Assert
        Assert.NotNull(result[0].SearchUrlTemplate);
        Assert.Contains("moto.com", result[0].SearchUrlTemplate!.ToString());
    }

    [Fact]
    public async Task LoadAsync_DbException_PropagatesWithLog()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllWebSourcesAsync())
            .ThrowsAsync(new InvalidOperationException("DB error"));
        var loader = CreateLoader();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync());
    }
}
