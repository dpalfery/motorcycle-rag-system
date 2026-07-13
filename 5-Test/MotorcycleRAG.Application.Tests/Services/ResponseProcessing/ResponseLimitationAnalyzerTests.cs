using System;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.ResponseProcessing;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.ResponseProcessing;

public sealed class ResponseLimitationAnalyzerTests
{
    private readonly ResponseLimitationAnalyzer _sut = new(NullLogger<ResponseLimitationAnalyzer>.Instance);

    private static SearchResult MakeResult(string id, float relevanceScore)
    {
        return new SearchResult
        {
            Id = id,
            Content = "Some content",
            RelevanceScore = relevanceScore,
            GeneratedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Source = new SearchSource
            {
                AgentType = SearchAgentType.VectorSearch,
                SourceName = "Test Source",
                DocumentId = "doc-1",
                LastUpdated = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
    }

    // -------------------------------------------------------------------
    // 1. Null / empty results early-out
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_NullResults_ReturnsResponseUnchanged()
    {
        const string response = "Original response text";

        var result = _sut.InjectLimitationMessages(response, null!, null!, "query-1");

        result.Should().Be(response);
    }

    [Fact]
    public void InjectLimitationMessages_EmptyResults_ReturnsResponseUnchanged()
    {
        const string response = "Original response text";
        var results = Array.Empty<SearchResult>();

        var result = _sut.InjectLimitationMessages(response, results, null!, "query-1");

        result.Should().Be(response);
    }

    // -------------------------------------------------------------------
    // 2. Null metrics / null SearchPattern -> no pattern-based message
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_NullMetrics_MultipleHighRelevanceResults_ReturnsResponseUnchanged()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.9f), MakeResult("r2", 0.8f) };

        var result = _sut.InjectLimitationMessages(response, results, null!, "query-1");

        result.Should().Be(response);
    }

    [Fact]
    public void InjectLimitationMessages_MetricsWithNullSearchPattern_MultipleHighRelevanceResults_ReturnsResponseUnchanged()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.9f), MakeResult("r2", 0.8f) };
        var metrics = new QueryMetrics { SearchPattern = null };

        var result = _sut.InjectLimitationMessages(response, results, metrics, "query-1");

        result.Should().Be(response);
    }

    // -------------------------------------------------------------------
    // 3. Partial pattern failure -> "Partial Results" + LogInformation
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_PartialSourceFailure_AddsPartialResultsMessageAndLogsInformation()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.9f), MakeResult("r2", 0.8f) };
        var metrics = new QueryMetrics
        {
            SearchPattern = new SearchPatternMetrics
            {
                VectorSearchExecuted = true,
                VectorResultsFound = 3,
                WebSearchExecuted = true,
                WebResultsFound = 0,
                PDFSearchExecuted = false,
                PDFResultsFound = 0
            }
        };
        var loggerMock = new Mock<ILogger<ResponseLimitationAnalyzer>>();
        var sut = new ResponseLimitationAnalyzer(loggerMock.Object);

        var result = sut.InjectLimitationMessages(response, results, metrics, "query-1");

        result.Should().Contain("⚠️ **Partial Results**");
        result.Should().Contain("web search (trusted sources)");
        result.Should().NotContain("vector search (indexed specifications)");
        result.Should().NotContain("PDF manual search");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // -------------------------------------------------------------------
    // 4. All sources failed -> "Service Degradation" + LogWarning
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_AllSourcesFailed_AddsServiceDegradationMessageAndLogsWarning()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.9f), MakeResult("r2", 0.8f) };
        var metrics = new QueryMetrics
        {
            SearchPattern = new SearchPatternMetrics
            {
                VectorSearchExecuted = true,
                VectorResultsFound = 0,
                WebSearchExecuted = true,
                WebResultsFound = 0,
                PDFSearchExecuted = true,
                PDFResultsFound = 0
            }
        };
        var loggerMock = new Mock<ILogger<ResponseLimitationAnalyzer>>();
        var sut = new ResponseLimitationAnalyzer(loggerMock.Object);

        var result = sut.InjectLimitationMessages(response, results, metrics, "query-1");

        result.Should().Contain("❌ **Service Degradation**");
        result.Should().NotContain("**Partial Results**");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // -------------------------------------------------------------------
    // 5. All sources succeeded -> no pattern-based message, and no other
    //    message fires either (response returned unchanged)
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_AllSourcesSucceeded_ReturnsResponseUnchanged()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.9f), MakeResult("r2", 0.8f) };
        var metrics = new QueryMetrics
        {
            SearchPattern = new SearchPatternMetrics
            {
                VectorSearchExecuted = true,
                VectorResultsFound = 5,
                WebSearchExecuted = true,
                WebResultsFound = 2,
                PDFSearchExecuted = true,
                PDFResultsFound = 1
            }
        };

        var result = _sut.InjectLimitationMessages(response, results, metrics, "query-1");

        result.Should().Be(response);
    }

    // -------------------------------------------------------------------
    // 6. Single result, high relevance -> only "Limited Results"
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_SingleResultHighRelevance_AddsOnlyLimitedResultsMessage()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.8f) };

        var result = _sut.InjectLimitationMessages(response, results, null!, "query-1");

        result.Should().Contain("ℹ️ **Limited Results**: Only one result found.");
        result.Should().NotContain("**Low Confidence**");
    }

    // -------------------------------------------------------------------
    // 7. Single result, low relevance -> BOTH "Limited Results" AND
    //    "Low Confidence" fire together (compound case)
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_SingleResultLowRelevance_AddsBothLimitedResultsAndLowConfidenceMessages()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.3f) };

        var result = _sut.InjectLimitationMessages(response, results, null!, "query-1");

        result.Should().Contain("ℹ️ **Limited Results**: Only one result found.");
        result.Should().Contain("⚠️ **Low Confidence**: Low relevance scores. Consider rephrasing.");
    }

    // -------------------------------------------------------------------
    // 8. Multi-result, all low relevance -> only "Low Confidence"
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_MultipleResultsAllLowRelevance_AddsOnlyLowConfidenceMessage()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.2f), MakeResult("r2", 0.4f), MakeResult("r3", 0.1f) };

        var result = _sut.InjectLimitationMessages(response, results, null!, "query-1");

        result.Should().Contain("⚠️ **Low Confidence**: Low relevance scores. Consider rephrasing.");
        result.Should().NotContain("**Limited Results**");
    }

    // -------------------------------------------------------------------
    // 9. Multi-result, mixed relevance -> neither message fires
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_MultipleResultsMixedRelevance_ReturnsResponseUnchanged()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.2f), MakeResult("r2", 0.6f), MakeResult("r3", 0.1f) };

        var result = _sut.InjectLimitationMessages(response, results, null!, "query-1");

        result.Should().Be(response);
    }

    // -------------------------------------------------------------------
    // 10. Message structure/ordering: blank-line-joined messages, then
    //     "---", then the original response, exactly matching the raw
    //     string template in the production source.
    // -------------------------------------------------------------------

    [Fact]
    public void InjectLimitationMessages_MultipleMessagesFire_ProducesExpectedTemplateStructure()
    {
        const string response = "Original response text";
        var results = new[] { MakeResult("r1", 0.3f) };
        var metrics = new QueryMetrics
        {
            SearchPattern = new SearchPatternMetrics
            {
                VectorSearchExecuted = true,
                VectorResultsFound = 0,
                WebSearchExecuted = true,
                WebResultsFound = 2,
                PDFSearchExecuted = false,
                PDFResultsFound = 0
            }
        };

        var result = _sut.InjectLimitationMessages(response, results, metrics, "query-1");

        var expectedMessageText = string.Join(
            "\n\n",
            "⚠️ **Partial Results**: Some sources (vector search (indexed specifications)) unavailable.",
            "ℹ️ **Limited Results**: Only one result found.",
            "⚠️ **Low Confidence**: Low relevance scores. Consider rephrasing.");

        var expected = $"""
{expectedMessageText}

---

{response}
""";

        result.Should().Be(expected);
    }
}
