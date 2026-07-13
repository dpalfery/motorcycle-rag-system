using MotorcycleRAG.Domain.InternalDTOs;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Domian.Tests.Domain;

public sealed class DomainDtoAndMetadataTests
{
    [Fact]
    public void DocumentMetadata_WhenCreated_ProvidesIndependentTagAndAdditionalPropertyCollections()
    {
        // Arrange
        var first = new DocumentMetadata();
        var second = new DocumentMetadata();

        // Act
        first.Tags.Add("service-manual");
        first.AdditionalProperties.Add("Language", "en-US");

        // Assert
        first.SourceFile.Should().BeEmpty();
        first.SourceUrl.Should().BeNull();
        first.PageNumber.Should().Be(0);
        first.Section.Should().BeEmpty();
        first.Author.Should().BeEmpty();
        first.Tags.Should().ContainSingle().Which.Should().Be("service-manual");
        first.AdditionalProperties.Should().ContainKey("Language").WhoseValue.Should().Be("en-US");
        second.Tags.Should().BeEmpty();
        second.AdditionalProperties.Should().BeEmpty();
    }

    [Fact]
    public void DocumentMetadata_WhenPublished_RetainsPublicationDate()
    {
        // Arrange
        var publishedDate = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var metadata = new DocumentMetadata { PublishedDate = publishedDate };

        // Assert
        metadata.PublishedDate.Should().Be(publishedDate);
    }

    [Fact]
    public void DomainPipelineNotification_WhenCreated_ProvidesIdentifierAndIndependentRecipientState()
    {
        // Arrange
        var first = new DomainPipelineNotification();
        var second = new DomainPipelineNotification();

        // Act
        first.Properties.Add("jobId", "job-123");
        first.Recipients.Add("ops@example.test");

        // Assert
        first.Id.Should().NotBeNullOrWhiteSpace();
        first.Title.Should().BeEmpty();
        first.Message.Should().BeEmpty();
        first.ExecutionId.Should().BeEmpty();
        first.Timestamp.Should().BeAfter(DateTime.UnixEpoch);
        first.Properties.Should().ContainKey("jobId").WhoseValue.Should().Be("job-123");
        first.Recipients.Should().ContainSingle().Which.Should().Be("ops@example.test");
        second.Properties.Should().BeEmpty();
        second.Recipients.Should().BeEmpty();
    }

    [Fact]
    public void DomainPipelineNotification_WhenPipelineFails_RetainsRoutingAndSeverityState()
    {
        // Arrange
        var notification = new DomainPipelineNotification();

        // Act
        notification.Type = DomainPipelineNotificationType.ExecutionFailed;
        notification.PipelineType = DomainPipelineType.PDF;
        notification.Severity = DomainNotificationSeverity.Error;

        // Assert
        notification.Type.Should().Be(DomainPipelineNotificationType.ExecutionFailed);
        notification.PipelineType.Should().Be(DomainPipelineType.PDF);
        notification.Severity.Should().Be(DomainNotificationSeverity.Error);
    }

    [Fact]
    public void DomainSearchResult_WhenConfiguredWithCitation_RetainsEvidenceAndPresentationState()
    {
        // Arrange
        var generatedAt = DateTime.Parse("2026-07-12T12:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var verifiedAt = DateTime.Parse("2026-07-12T12:05:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var citation = new DomainCitation
        {
            SourceType = DomainCitationSourceType.ManualPdf,
            SourceName = "CBR600RR service manual",
            SourceUrl = new Uri("https://docs.example.test/cbr600rr.pdf"),
            PageNumber = 18,
            Section = "Chain maintenance",
            ConfidenceScore = 0.92f,
            Verified = true,
            VerificationMethod = "manual-review",
            VerifiedAt = verifiedAt,
            Locator = "page-18"
        };
        var source = new DomainSearchSource
        {
            AgentType = DomainSearchAgentType.PDFSearch,
            SourceName = "CBR600RR service manual",
            SourceUrl = new Uri("https://docs.example.test/cbr600rr.pdf"),
            DocumentId = "manual-123",
            LastUpdated = generatedAt,
            Citation = citation
        };

        // Act
        var result = new DomainSearchResult
        {
            Id = "result-123",
            Content = "Inspect and lubricate the drive chain.",
            RelevanceScore = 0.91f,
            Source = source,
            GeneratedAt = generatedAt
        };
        result.Metadata.Add("language", "en-US");
        result.Highlights.Add("lubricate the drive chain");
        citation.Metadata.Add("chapter", "Maintenance");

        // Assert
        result.Id.Should().Be("result-123");
        result.Content.Should().Be("Inspect and lubricate the drive chain.");
        result.RelevanceScore.Should().Be(0.91f);
        result.Source.Should().BeSameAs(source);
        result.GeneratedAt.Should().Be(generatedAt);
        result.Metadata.Should().ContainKey("language").WhoseValue.Should().Be("en-US");
        result.Highlights.Should().ContainSingle().Which.Should().Be("lubricate the drive chain");
        result.Source.Citation.Should().BeEquivalentTo(new
        {
            SourceType = DomainCitationSourceType.ManualPdf,
            SourceName = "CBR600RR service manual",
            SourceUrl = new Uri("https://docs.example.test/cbr600rr.pdf"),
            PageNumber = 18,
            Section = "Chain maintenance",
            ConfidenceScore = 0.92f,
            Verified = true,
            VerificationMethod = "manual-review",
            VerifiedAt = (DateTime?)verifiedAt,
            Locator = "page-18"
        });
        citation.Metadata.Should().ContainKey("chapter").WhoseValue.Should().Be("Maintenance");
    }

    [Fact]
    public void DomainSearchResult_WhenCreated_ProvidesIndependentPresentationCollections()
    {
        // Arrange
        var first = new DomainSearchResult();
        var second = new DomainSearchResult();

        // Act
        first.Highlights.Add("matched phrase");
        first.Metadata.Add("scoreSource", "vector");

        // Assert
        first.Id.Should().BeEmpty();
        first.Content.Should().BeEmpty();
        first.Source.Should().NotBeNull();
        first.Highlights.Should().ContainSingle().Which.Should().Be("matched phrase");
        first.Metadata.Should().ContainKey("scoreSource").WhoseValue.Should().Be("vector");
        second.Highlights.Should().BeEmpty();
        second.Metadata.Should().BeEmpty();
    }
}
