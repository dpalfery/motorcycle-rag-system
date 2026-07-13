using MotorcycleRAG.Application.Features.Ingestion.Validators;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Features.Ingestion.Validators;

public sealed class IngestionJobValidatorTests
{
    private readonly IngestionJobValidator _sut = new();

    [Fact]
    public void Validate_ValidManualPdfRequestWithConfiguration_ReturnsNoErrors()
    {
        // Arrange
        var request = CreateValidManualPdfRequest();

        // Act
        var errors = _sut.Validate(request);

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_NullRequest_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.Validate(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public void Validate_MissingRequiredIdentifiers_ReturnsRequiredErrors()
    {
        // Arrange
        var request = new IngestionJobStartRequest
        {
            UploadId = "",
            DocumentType = "",
            ProcessorRunId = ""
        };

        // Act
        var errors = _sut.Validate(request);

        // Assert
        errors.Should().Equal(
            "UploadId is required.",
            "DocumentType is required.",
            "ProcessorRunId is required.");
    }

    [Fact]
    public void Validate_InvalidLengthPathAndDocumentType_ReturnsAllRelevantErrors()
    {
        // Arrange
        var request = new IngestionJobStartRequest
        {
            UploadId = $"{new string('u', 501)}/manual.pdf",
            DocumentType = "unsupported-document",
            ProcessorRunId = new string('r', 129)
        };

        // Act
        var errors = _sut.Validate(request);

        // Assert
        errors.Should().Equal(
            "UploadId must not exceed 500 characters.",
            "UploadId must not contain path separators ('/' or '\\').",
            "DocumentType must be 'manual-pdf', 'spec-dataset', or 'bike-graph'.",
            "ProcessorRunId must not exceed 128 characters.");
    }

    [Fact]
    public void Validate_ManualPdfWithoutConfiguration_ReturnsConfigurationError()
    {
        // Arrange
        var request = CreateValidManualPdfRequest() with { Configuration = null };

        // Act
        var errors = _sut.Validate(request);

        // Assert
        errors.Should().Equal("Configuration is required when DocumentType is 'manual-pdf'.");
    }

    private static IngestionJobStartRequest CreateValidManualPdfRequest() => new()
    {
        UploadId = "upload-123",
        DocumentType = "manual-pdf",
        ProcessorRunId = "processor-run-123",
        Configuration = new IngestionJobConfiguration()
    };
}
