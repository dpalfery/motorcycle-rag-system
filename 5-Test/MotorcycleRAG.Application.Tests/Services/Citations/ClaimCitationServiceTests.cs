using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Citations;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Citations;

public sealed class ClaimCitationServiceTests
{
    private readonly ClaimCitationService _sut = new(NullLogger<ClaimCitationService>.Instance);

    private static SearchResult MakeSource(
        string id,
        string content,
        float relevanceScore,
        SearchAgentType agentType = SearchAgentType.VectorSearch,
        string sourceName = "Test Source",
        string? sourceUrl = "https://example.com/doc",
        Citation? citation = null,
        Dictionary<string, object>? metadata = null)
    {
        return new SearchResult
        {
            Id = id,
            Content = content,
            RelevanceScore = relevanceScore,
            GeneratedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Metadata = metadata ?? new Dictionary<string, object>(),
            Source = new SearchSource
            {
                AgentType = agentType,
                SourceName = sourceName,
                SourceUrl = sourceUrl,
                DocumentId = "doc-1",
                LastUpdated = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                Citation = citation
            }
        };
    }

    // -------------------------------------------------------------------
    // 1. No factual claims found
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_NoFactualClaims_ReturnsOriginalAnswerAndSources()
    {
        var sources = Array.Empty<SearchResult>();
        const string answer = "This bike looks great";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "how does it look?");

        result.Answer.Should().Be(answer);
        result.Results.Should().BeSameAs(sources);
    }

    // -------------------------------------------------------------------
    // 2. Claim with matching high-relevance source
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_ClaimWithHighRelevanceSource_MarksVerifiedAndAppendsCitations()
    {
        var source = MakeSource(
            "src-1",
            "Detailed specs: The engine is powerful and reliable across RPM ranges.",
            relevanceScore: 0.9f);
        var sources = new[] { source };
        const string answer = "The engine is powerful.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "how powerful is the engine?");

        result.Answer.Should().Contain("[1-1]");
        result.Answer.Should().Contain("### Sources and Citations");
        result.Answer.Should().Contain("Verified");
        result.Answer.Should().NotContain("Requires verification");
        result.Answer.Should().NotContain("[Note: Could not verify]");
    }

    // -------------------------------------------------------------------
    // 3. Claim with matching low-relevance source
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_ClaimWithLowRelevanceSource_AppendsRequiresVerificationNote()
    {
        var source = MakeSource(
            "src-1",
            "Detailed specs: The engine is powerful and reliable across RPM ranges.",
            relevanceScore: 0.5f);
        var sources = new[] { source };
        const string answer = "The engine is powerful.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "how powerful is the engine?");

        result.Answer.Should().Contain("[Note: Requires verification]");
    }

    // -------------------------------------------------------------------
    // 4. Claim with no matching source, not qualified, not common knowledge
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_UnverifiableClaim_InjectsCouldNotVerifyNote()
    {
        var sources = Array.Empty<SearchResult>();
        const string answer = "The transmission holds 500 milliliters.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "how much fluid?");

        result.Answer.Should().Contain("[Note: Could not verify]");
    }

    // -------------------------------------------------------------------
    // 5. Claim with no matching source but qualified wording -> exempted
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_QualifiedClaimWithNoSource_DoesNotInjectCouldNotVerifyNote()
    {
        var sources = Array.Empty<SearchResult>();
        const string answer = "The chain might stretch 2 millimeters over time.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "does the chain stretch?");

        result.Answer.Should().NotContain("[Note: Could not verify]");
    }

    // -------------------------------------------------------------------
    // 6. Claim with no matching source but dominated by common-knowledge terms -> exempted
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_CommonKnowledgeClaimWithNoSource_DoesNotInjectCouldNotVerifyNote()
    {
        var sources = Array.Empty<SearchResult>();
        const string answer = "Motorcycle vehicle engine has wheels.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "what is a motorcycle?");

        result.Answer.Should().NotContain("[Note: Could not verify]");
    }

    // -------------------------------------------------------------------
    // 7. Locator creation per SearchAgentType
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_VectorSearchSource_CreatesDatasetCitationLocator()
    {
        var source = MakeSource("rec-1", "Unrelated filler content.", 0.4f, SearchAgentType.VectorSearch, "Spec Dataset");
        var sources = new[] { source };
        const string answer = "The bike is fast.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "how fast is it?");

        var citation = result.Results[0].Source.Citation;
        citation.Should().NotBeNull();
        var locator = citation!.Locator.Should().BeOfType<DatasetCitationLocator>().Subject;
        locator.DatasetName.Should().Be("Spec Dataset");
        locator.RecordId.Should().Be("rec-1");
        locator.FieldName.Should().Be("Content");
        locator.DataSourceUrl.Should().Be("https://example.com/doc");
        locator.RetrievalTimestamp.Should().Be(source.GeneratedAt);
    }

    [Fact]
    public async Task GenerateCitedResponseAsync_WebSearchSource_CreatesWebsiteCitationLocator()
    {
        var source = MakeSource("rec-2", "Unrelated filler content.", 0.4f, SearchAgentType.WebSearch, "Some Blog", "https://blog.example.com/post");
        var sources = new[] { source };
        const string answer = "The bike is fast.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "how fast is it?");

        var citation = result.Results[0].Source.Citation;
        citation.Should().NotBeNull();
        var locator = citation!.Locator.Should().BeOfType<WebsiteCitationLocator>().Subject;
        locator.Url.Should().Be("https://blog.example.com/post");
        locator.Title.Should().Be("Some Blog");
        locator.AccessedDate.Should().Be(source.GeneratedAt);
        locator.TrustTier.Should().Be(WebsiteTrustTier.Standard);
    }

    [Fact]
    public async Task GenerateCitedResponseAsync_PdfSearchSourceWithMetadata_CreatesManualPdfCitationLocatorWithMetadataFields()
    {
        var metadata = new Dictionary<string, object>
        {
            { "PageNumber", 5 },
            { "PrimarySection", "Chapter 2" }
        };
        var source = MakeSource("rec-3", "Unrelated filler content.", 0.4f, SearchAgentType.PDFSearch, "Owner Manual", metadata: metadata);
        var sources = new[] { source };
        const string answer = "The bike is fast.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "how fast is it?");

        var citation = result.Results[0].Source.Citation;
        citation.Should().NotBeNull();
        var locator = citation!.Locator.Should().BeOfType<ManualPdfCitationLocator>().Subject;
        locator.PageNumber.Should().Be(5);
        locator.PrimarySection.Should().Be("Chapter 2");
    }

    [Fact]
    public async Task GenerateCitedResponseAsync_PdfSearchSourceWithoutMetadata_DefaultsPageNumberToOne()
    {
        var source = MakeSource("rec-4", "Unrelated filler content.", 0.4f, SearchAgentType.PDFSearch, "Owner Manual");
        var sources = new[] { source };
        const string answer = "The bike is fast.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "how fast is it?");

        var citation = result.Results[0].Source.Citation;
        citation.Should().NotBeNull();
        var locator = citation!.Locator.Should().BeOfType<ManualPdfCitationLocator>().Subject;
        locator.PageNumber.Should().Be(1);
    }

    // -------------------------------------------------------------------
    // 8. EnsureAllResultsHaveCitations only fills in null citations
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_SourceWithExistingCitation_DoesNotOverwriteCitation()
    {
        var existingCitation = new Citation
        {
            SourceType = CitationSourceType.ExpertReview,
            SourceName = "Preset Citation",
            Verified = true
        };
        var source = MakeSource("rec-5", "Unrelated filler content.", 0.4f, citation: existingCitation);
        var sources = new[] { source };
        const string answer = "The bike is fast.";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "how fast is it?");

        result.Results[0].Source.Citation.Should().BeSameAs(existingCitation);
        result.Results[0].Source.Citation!.SourceName.Should().Be("Preset Citation");
    }

    // -------------------------------------------------------------------
    // 9. Exception path fallback
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_ExceptionDuringProcessing_ReturnsOriginalAnswerAndSourcesAndLogsError()
    {
        // A null element inside the sources array causes an NRE inside
        // FindMatchingSourcesForClaim (called from MapClaimsToEvidence), which is
        // NOT wrapped in its own try/catch, so it bubbles up to the outer
        // try/catch in GenerateCitedResponseAsync.
        var loggerMock = new Mock<ILogger<ClaimCitationService>>();
        var sut = new ClaimCitationService(loggerMock.Object);

        var validSource = MakeSource("rec-6", "Some content.", 0.9f);
        var sources = new[] { validSource, null! };
        const string answer = "The engine is powerful.";

        var result = await sut.GenerateCitedResponseAsync(answer, sources, "how powerful is the engine?");

        result.Answer.Should().Be(answer);
        result.Results.Should().BeSameAs(sources);

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // -------------------------------------------------------------------
    // 10. Null argument validation
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_NullOriginalAnswer_ThrowsArgumentNullException()
    {
        var sources = Array.Empty<SearchResult>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.GenerateCitedResponseAsync(null!, sources, "query"));
    }

    [Fact]
    public async Task GenerateCitedResponseAsync_NullSources_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.GenerateCitedResponseAsync("answer", null!, "query"));
    }

    [Fact]
    public async Task GenerateCitedResponseAsync_WithCancellationToken_NullOriginalAnswer_ThrowsArgumentNullException()
    {
        var sources = Array.Empty<SearchResult>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.GenerateCitedResponseAsync(null!, sources, "query", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateCitedResponseAsync_WithCancellationToken_NullSources_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.GenerateCitedResponseAsync("answer", null!, "query", CancellationToken.None));
    }

    // -------------------------------------------------------------------
    // 11. Overload delegation / smoke tests
    // -------------------------------------------------------------------

    [Fact]
    public async Task GenerateCitedResponseAsync_NoTokenOverload_DelegatesToTokenOverload()
    {
        var sources = Array.Empty<SearchResult>();
        const string answer = "This bike looks great";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "query");

        result.Answer.Should().Be(answer);
        result.Results.Should().BeSameAs(sources);
    }

    [Fact]
    public async Task GenerateCitedResponseAsync_WithExplicitCancellationToken_ProducesSameResultAsNoTokenOverload()
    {
        using var cts = new CancellationTokenSource();
        var sources = Array.Empty<SearchResult>();
        const string answer = "This bike looks great";

        var result = await _sut.GenerateCitedResponseAsync(answer, sources, "query", cts.Token);

        result.Answer.Should().Be(answer);
        result.Results.Should().BeSameAs(sources);
    }
}
