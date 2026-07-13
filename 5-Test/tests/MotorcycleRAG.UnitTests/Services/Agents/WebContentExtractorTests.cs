using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using AgentWebContentExtractor = MotorcycleRAG.Application.Services.Agents.WebContentExtractor;

namespace MotorcycleRAG.UnitTests.Services.Agents;

public sealed class WebContentExtractorTests
{
    private static readonly TrustedSourceOptions Source = new()
    {
        Name = "Motorcycle test source",
        BaseUrl = new Uri("https://example.test"),
        ContentSelector = "//p",
        CredibilityScore = 0.9f,
    };

    [Fact]
    public void ExtractSearchResults_WithRelevantHtml_MapsMetadataHighlightsAndLimitsResults()
    {
        // Arrange
        var sut = CreateSut();
        var longContent = "Honda CBR engine horsepower specifications " + new string('x', 600);
        var html = string.Concat(Enumerable.Repeat($"<p>{longContent}</p>", 6));

        // Act
        var results = sut.ExtractSearchResults(html, "Honda CBR", Source);

        // Assert
        results.Should().HaveCount(5);
        results.Should().OnlyContain(result => result.Source.AgentType == SearchAgentType.WebSearch);
        results.Should().OnlyContain(result => result.Source.SourceName == Source.Name);
        results.Should().OnlyContain(result => result.Content.EndsWith("...", StringComparison.Ordinal));
        results.Should().OnlyContain(result => result.RelevanceScore > 0.3f);
        foreach (var result in results)
        {
            result.Metadata["searchTerm"].Should().Be("Honda CBR");
            result.Highlights.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void ExtractSearchResults_WithNoRelevantNodes_ReturnsFallbackContent()
    {
        // Arrange
        var sut = CreateSut();
        var html = $"<html><body><p>{string.Join(" ", Enumerable.Repeat("neutral document content", 10))}</p></body></html>";

        // Act
        var results = sut.ExtractSearchResults(html, "Honda CBR", Source);

        // Assert
        results.Should().ContainSingle();
        results[0].RelevanceScore.Should().Be(0.6f);
        results[0].Metadata["fallbackContent"].Should().Be(true);
        results[0].Metadata["credibilityScore"].Should().Be(Source.CredibilityScore);
    }

    [Fact]
    public void ExtractSearchResults_WithEmptyHtmlOrInvalidSelector_ReturnsNoResults()
    {
        // Arrange
        var sut = CreateSut();
        var invalidSelectorSource = new TrustedSourceOptions
        {
            Name = Source.Name,
            BaseUrl = Source.BaseUrl,
            ContentSelector = "//*[",
            CredibilityScore = Source.CredibilityScore,
        };

        // Act
        var emptyResults = sut.ExtractSearchResults(string.Empty, "Honda", Source);
        var invalidSelectorResults = sut.ExtractSearchResults("<p>Honda motorcycle engine</p>", "Honda", invalidSelectorSource);

        // Assert
        emptyResults.Should().BeEmpty();
        invalidSelectorResults.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSearchResults_WithInvalidArguments_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var nullSource = () => sut.ExtractSearchResults("<p>Honda motorcycle</p>", "Honda", null!);
        var nullSearchTerm = () => sut.ExtractSearchResults("<p>Honda motorcycle</p>", null!, Source);
        var nullLogger = () => new AgentWebContentExtractor(null!);

        // Assert
        nullSource.Should().Throw<ArgumentNullException>();
        nullSearchTerm.Should().Throw<ArgumentNullException>();
        nullLogger.Should().Throw<ArgumentNullException>();
    }

    private static AgentWebContentExtractor CreateSut() =>
        new(NullLogger.Instance);
}
