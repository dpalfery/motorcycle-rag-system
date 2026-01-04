using System;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

/// <summary>
/// Unit tests for ModelValidationService, focusing on manual PDF citation validation.
/// Tests cover validation rules, edge cases, and error scenarios.
/// </summary>
public class ModelValidationServiceTests
{
    private readonly Mock<ILogger<ModelValidationService>> _loggerMock;
    private readonly ModelValidationService _sut;

    public ModelValidationServiceTests()
    {
        _loggerMock = new Mock<ILogger<ModelValidationService>>();
        _sut = new ModelValidationService(_loggerMock.Object);
    }

    #region Null Citation Tests

    [Fact]
    public void ValidateCitation_NullCitation_ReturnsEmptyErrors_NoException()
    {
        // Arrange
        Citation? citation = null;

        // Act
        var errors = _sut.ValidateCitation(citation!);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    #endregion

    #region Null Locator Tests

    [Fact]
    public void ValidateCitation_ManualPdfWithNullLocator_ReturnsEmptyErrors_BestEffort()
    {
        // Arrange
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = null
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    #endregion

    #region Wrong Locator Type Tests

    [Fact]
    public void ValidateCitation_ManualPdfWithWrongLocatorType_LogsWarning_ReturnsEmptyErrors()
    {
        // Arrange
        var wrongLocator = new { SomeProperty = "value" };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = wrongLocator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Citation locator is not ManualPdfCitationLocator")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region PageNumber and PageRange Validation Tests

    [Fact]
    public void ValidateCitation_ValidPageNumberOnly_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            PageRange = null
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_ValidPageRangeOnly_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 0,
            PageRange = "5-7"
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_BothPageNumberAndPageRangeValid_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            PageRange = "5-7"
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_InvalidPageNumberZeroAndEmptyPageRange_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 0,
            PageRange = null
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("must have either PageNumber (> 0) or PageRange", errors[0]);
    }

    [Fact]
    public void ValidateCitation_InvalidPageNumberNegativeAndEmptyPageRange_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = -1,
            PageRange = null
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("must have either PageNumber (> 0) or PageRange", errors[0]);
    }

    [Fact]
    public void ValidateCitation_InvalidPageNumberZeroAndWhitespacePageRange_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 0,
            PageRange = "   "
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("must have either PageNumber (> 0) or PageRange", errors[0]);
    }

    [Fact]
    public void ValidateCitation_InvalidPageNumberZeroAndEmptyStringPageRange_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 0,
            PageRange = string.Empty
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("must have either PageNumber (> 0) or PageRange", errors[0]);
    }

    #endregion

    #region SectionHeadings Validation Tests

    [Fact]
    public void ValidateCitation_ValidSectionHeadings_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionHeadings = new[] { "Chapter 1", "Section 1.1", "Subsection 1.1.1" }
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_EmptySectionHeadingsArray_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionHeadings = Array.Empty<string>()
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_NullSectionHeadings_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionHeadings = null!
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_SectionHeadingsWithEmptyString_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionHeadings = new[] { "Chapter 1", "", "Subsection 1.1.1" }
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("SectionHeadings contains empty or whitespace-only strings at indices: 1", errors[0]);
    }

    [Fact]
    public void ValidateCitation_SectionHeadingsWithWhitespaceOnly_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionHeadings = new[] { "Chapter 1", "   ", "Subsection 1.1.1" }
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("SectionHeadings contains empty or whitespace-only strings at indices: 1", errors[0]);
    }

    [Fact]
    public void ValidateCitation_SectionHeadingsWithMultipleEmptyStrings_ReturnsErrorWithAllIndices()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionHeadings = new[] { "", "Chapter 1", "   ", "Subsection 1.1.1", "" }
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("SectionHeadings contains empty or whitespace-only strings at indices: 0, 2, 4", errors[0]);
    }

    [Fact]
    public void ValidateCitation_SectionHeadingsWithTabAndNewline_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionHeadings = new[] { "Chapter 1", "\t\n", "Subsection 1.1.1" }
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("SectionHeadings contains empty or whitespace-only strings at indices: 1", errors[0]);
    }

    #endregion

    #region SectionLevel Validation Tests

    [Fact]
    public void ValidateCitation_ValidSectionLevelZero_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionLevel = 0
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_ValidSectionLevelOne_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionLevel = 1
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_ValidSectionLevelTwo_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionLevel = 2
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_ValidSectionLevelThree_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionLevel = 3
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_NullSectionLevel_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionLevel = null
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_InvalidSectionLevelNegativeOne_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionLevel = -1
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("SectionLevel must be between 0 and 3", errors[0]);
        Assert.Contains("Actual: -1", errors[0]);
    }

    [Fact]
    public void ValidateCitation_InvalidSectionLevelFour_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionLevel = 4
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("SectionLevel must be between 0 and 3", errors[0]);
        Assert.Contains("Actual: 4", errors[0]);
    }

    [Fact]
    public void ValidateCitation_InvalidSectionLevelTen_ReturnsError()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5,
            SectionLevel = 10
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("SectionLevel must be between 0 and 3", errors[0]);
        Assert.Contains("Actual: 10", errors[0]);
    }

    #endregion

    #region Multiple Validation Errors Tests

    [Fact]
    public void ValidateCitation_MultipleValidationErrors_ReturnsAllErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 0,
            PageRange = null,
            SectionHeadings = new[] { "Chapter 1", "", "Subsection 1.1.1" },
            SectionLevel = 5
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("must have either PageNumber (> 0) or PageRange"));
        Assert.Contains(errors, e => e.Contains("SectionHeadings contains empty or whitespace-only strings"));
        Assert.Contains(errors, e => e.Contains("SectionLevel must be between 0 and 3"));
    }

    #endregion

    #region SourceIndex Error Reporting Tests

    [Fact]
    public void ValidateCitation_WithSourceIndex_IncludesSourceIndexInErrorMessage()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 0,
            PageRange = null
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };
        const int sourceIndex = 3;

        // Act
        var errors = _sut.ValidateCitation(citation, sourceIndex);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("Source[3]:", errors[0]);
    }

    [Fact]
    public void ValidateCitation_WithNegativeSourceIndex_DoesNotIncludeSourceIndex()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 0,
            PageRange = null
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };
        const int sourceIndex = -1;

        // Act
        var errors = _sut.ValidateCitation(citation, sourceIndex);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.DoesNotContain("Source[-1]:", errors[0]);
        Assert.DoesNotContain("Source[", errors[0]);
    }

    #endregion

    #region Non-ManualPdf Source Type Tests

    [Fact]
    public void ValidateCitation_NonManualPdfSourceWithLocator_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.Website,
            SourceName = "Test Website",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCitation_DatasetSourceWithLocator_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "doc-001",
            PageNumber = 5
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.Dataset,
            SourceName = "Test Dataset",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    #endregion

    #region Complete Valid Citation Tests

    [Fact]
    public void ValidateCitation_CompleteValidManualPdfCitation_ReturnsEmptyErrors()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = "honda-cb500f-2023-manual",
            Title = "Honda CB500F 2023 Owner's Manual",
            PageNumber = 42,
            PageRange = "42-45",
            PrimarySection = "Maintenance",
            SectionLevel = 2,
            SectionHeadings = new[] { "Chapter 3: Maintenance", "Section 3.2: Oil Change", "Subsection 3.2.1: Oil Selection" },
            TableCaption = null,
            ChunkIndex = 3,
            Section = "Oil Change",
            FigureReference = "Fig 3.1",
            Version = "1.0",
            PublicationDate = new DateTime(2023, 1, 1),
            SourceUrl = "https://example.com/manuals/honda-cb500f-2023.pdf"
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Honda CB500F 2023 Owner's Manual",
            SourceUrl = "https://example.com/manuals/honda-cb500f-2023.pdf",
            PageNumber = 42,
            Section = "Oil Change",
            ConfidenceScore = 0.95f,
            Verified = true,
            VerificationMethod = "Document Intelligence OCR",
            VerifiedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                { "extractionMethod", "azure-document-intelligence" },
                { "chunkId", "chunk-123" }
            },
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    #endregion

    #region DocumentId Sanitization Tests

    [Fact]
    public void ValidateCitation_WithLongDocumentId_TruncatesInErrorMessage()
    {
        // Arrange
        var longDocumentId = new string('a', 100); // 100 characters
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = longDocumentId,
            PageNumber = 0,
            PageRange = null
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        // DocumentId should be truncated to 50 chars + "..."
        Assert.Contains("DocumentId: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa...", errors[0]);
        Assert.Contains("must have either PageNumber (> 0) or PageRange", errors[0]);
        Assert.DoesNotContain(longDocumentId, errors[0]);
    }

    [Fact]
    public void ValidateCitation_WithEmptyDocumentId_ShowsEmptyPlaceholder()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = string.Empty,
            PageNumber = 0,
            PageRange = null
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("DocumentId: [empty]", errors[0]);
    }

    [Fact]
    public void ValidateCitation_WithNullDocumentId_ShowsEmptyPlaceholder()
    {
        // Arrange
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = null!,
            PageNumber = 0,
            PageRange = null
        };
        var citation = new Citation
        {
            SourceType = CitationSourceType.ManualPdf,
            SourceName = "Test Manual",
            Locator = locator
        };

        // Act
        var errors = _sut.ValidateCitation(citation);

        // Assert
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Contains("DocumentId: [empty]", errors[0]);
    }

    #endregion
}
