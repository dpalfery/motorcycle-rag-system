using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Reliability tests for FileUploadService to ensure robust file handling
/// </summary>
public class FileUploadServiceReliabilityTests {
    private static readonly string[] AllowedExtensionsArray = { ".csv", ".pdf" };

    private readonly Mock<ITelemetryService> _telemetryServiceMock;
    private readonly Mock<ILocalFileStore> _localFileStoreMock;
    private readonly Mock<ILogger<FileUploadService>> _loggerMock;
    private readonly Mock<IOptions<FileUploadConfiguration>> _configMock;
    private readonly FileUploadService _service;

    private sealed class ThrowingReadStream : MemoryStream {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new InvalidOperationException("content read failed"));
    }

    public FileUploadServiceReliabilityTests() {
        _telemetryServiceMock = new Mock<ITelemetryService>();
        _localFileStoreMock = new Mock<ILocalFileStore>();
        _loggerMock = new Mock<ILogger<FileUploadService>>();
        _configMock = new Mock<IOptions<FileUploadConfiguration>>();

        var config = new FileUploadConfiguration {
            BaseUploadDirectory = Path.GetTempPath(),
            MaxFileSizeBytes = 50 * 1024 * 1024, // 50MB
            MaxFilesPerBatch = 10,
            AllowedExtensions = new[] { ".csv", ".pdf" },
            AllowedContentTypes = new[] { "text/csv", "application/csv", "application/pdf" }
        };

        _configMock.Setup(x => x.Value).Returns(config);
        _localFileStoreMock
            .Setup(x => x.EnsureDirectoryExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _localFileStoreMock
            .Setup(x => x.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new FileUploadService(
            _configMock.Object,
            _localFileStoreMock.Object,
            _telemetryServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task UploadFileAsync_WithValidCSVFile_ShouldSucceed() {
        // Arrange
        var content = "Make,Model,Year\nHonda,CBR600RR,2023";
        var (stream, metadata) = CreateMockFile("test.csv", content, "text/csv");
        var options = new FileUploadOptions {
            UploadDirectory = "test-uploads",
            GenerateUniqueFileName = true,
            ValidateFileContent = false
        };

        // Act
        var result = await _service.UploadFileAsync(stream, metadata, options);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(FileType.CSV, result.DetectedFileType);
        Assert.Equal("test.csv", result.OriginalFileName);
        _localFileStoreMock.Verify(
            x => x.WriteAsync(result.FilePath, stream, It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify telemetry was tracked
        _telemetryServiceMock.Verify(x => x.TrackEvent("FileUploaded", It.IsAny<Dictionary<string, string>>()), Times.Once);

    }

    [Fact]
    public async Task UploadFileAsync_WithValidPDFFile_ShouldSucceed() {
        // Arrange
        var content = "%PDF-1.4\n1 0 obj\n<<\n/Type /Catalog\n/Pages 2 0 R\n>>\nendobj";
        var (stream, metadata) = CreateMockFile("manual.pdf", content, "application/pdf");
        var options = new FileUploadOptions {
            UploadDirectory = "test-uploads",
            GenerateUniqueFileName = true,
            ValidateFileContent = false
        };

        // Act
        var result = await _service.UploadFileAsync(stream, metadata, options);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(FileType.PDF, result.DetectedFileType);
        Assert.Equal("manual.pdf", result.OriginalFileName);
        _localFileStoreMock.Verify(
            x => x.WriteAsync(result.FilePath, stream, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadFileAsync_WithOversizedFile_ShouldFail() {
        // Arrange
        var largeContent = new string('x', 100 * 1024 * 1024); // 100MB content
        var (stream, metadata) = CreateMockFile("large.csv", largeContent, "text/csv");
        var options = new FileUploadOptions {
            MaxFileSizeBytes = 50 * 1024 * 1024 // 50MB limit
        };

        // Act
        var result = await _service.UploadFileAsync(stream, metadata, options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("exceeds maximum allowed size", result.ValidationResult.Errors[0]);
    }

    [Fact]
    public async Task UploadFileAsync_WithInvalidExtension_ShouldFail() {
        // Arrange
        var content = "Invalid file content";
        var (stream, metadata) = CreateMockFile("test.txt", content, "text/plain");
        var options = new FileUploadOptions();
        options.AllowedFileExtensions.Clear();
        options.AllowedFileExtensions.UnionWith(AllowedExtensionsArray);

        // Act
        var result = await _service.UploadFileAsync(stream, metadata, options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("not allowed", result.ValidationResult.Errors[0]);
    }

    [Fact]
    public async Task UploadFileAsync_WithEmptyFile_ShouldFail() {
        // Arrange
        var (stream, metadata) = CreateMockFile("empty.csv", "", "text/csv");
        var options = new FileUploadOptions();

        // Act
        var result = await _service.UploadFileAsync(stream, metadata, options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("File is empty", result.ValidationResult.Errors);
    }

    [Fact]
    public async Task UploadFilesAsync_WithMixedValidAndInvalidFiles_ShouldProcessAll() {
        // Arrange
        var files = new List<(Stream stream, FileMetadata metadata)>
        {
            CreateMockFile("valid.csv", "Make,Model\nHonda,CBR", "text/csv"),
            CreateMockFile("invalid.txt", "Invalid content", "text/plain"),
            CreateMockFile("valid.pdf", "%PDF-1.4 content", "application/pdf")
        };

        var options = new FileUploadOptions {
            UploadDirectory = "batch-test",
            ValidateFileContent = false
        };
        options.AllowedFileExtensions.Clear();
        options.AllowedFileExtensions.UnionWith(AllowedExtensionsArray);

        // Act
        var result = await _service.UploadFilesAsync(files, options);

        // Assert
        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(2, result.SuccessfulUploads);
        Assert.Equal(1, result.FailedUploads);
        Assert.False(result.AllFilesUploaded);

        _localFileStoreMock.Verify(
            x => x.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task UploadFilesAsync_WhenCancellationIsRequested_ReturnsBatchFailureWithoutWritingFiles() {
        var (stream, metadata) = CreateMockFile("manual.pdf", "%PDF-1.4 content", "application/pdf");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _service.UploadFilesAsync([(stream, metadata)], new FileUploadOptions(), cts.Token);

        result.TotalFiles.Should().Be(1);
        result.SuccessfulUploads.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Batch upload failed:");
        _localFileStoreMock.Verify(x => x.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateFileAsync_WithCorruptedPDF_ShouldDetectIssue() {
        // Arrange
        var corruptedContent = "This is not a PDF file";
        var (stream, metadata) = CreateMockFile("corrupted.pdf", corruptedContent, "application/pdf");
        var options = new FileUploadOptions {
            ValidateFileContent = true
        };

        // Act
        var result = await _service.ValidateFileAsync(stream, metadata, options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("missing PDF header", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateFileAsync_WithMalformedCSV_ShouldProvideWarning() {
        // Arrange
        var malformedContent = "NoCommasOrSemicolonsHere";
        var (stream, metadata) = CreateMockFile("malformed.csv", malformedContent, "text/csv");
        var options = new FileUploadOptions {
            ValidateFileContent = true
        };

        // Act
        var result = await _service.ValidateFileAsync(stream, metadata, options);

        // Assert
        Assert.True(result.IsValid); // Should still be valid but with warnings
        Assert.Contains("may not be a valid CSV", result.Warnings.Count > 0 ? result.Warnings[0] : "");
    }

    [Fact]
    public async Task ValidateFileAsync_WhenContentReadFails_ReturnsValidResultWithContentWarning() {
        await using var stream = new ThrowingReadStream();
        var metadata = new FileMetadata { FileName = "manual.pdf", ContentType = "application/pdf", ContentLength = 1 };

        var result = await _service.ValidateFileAsync(stream, metadata, new FileUploadOptions { ValidateFileContent = true });

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Be("Content validation failed: content read failed");
    }

    [Fact]
    public async Task ValidateFileAsync_WhenFileHasNoExtension_ReturnsValidationErrorAndSkipsContentInspection() {
        var (stream, metadata) = CreateMockFile("manual", "%PDF-1.4", "application/pdf");

        var result = await _service.ValidateFileAsync(stream, metadata, new FileUploadOptions { ValidateFileContent = true });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Be("File has no extension");
    }

    [Fact]
    public async Task DeleteFileAsync_WithExistingFile_ShouldReturnTrue() {
        // Arrange
        const string filePath = "/uploads/manual.pdf";
        _localFileStoreMock
            .Setup(x => x.DeleteIfExistsAsync(filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.DeleteFileAsync(filePath);

        // Assert
        Assert.True(result);
        _localFileStoreMock.Verify(
            x => x.DeleteIfExistsAsync(filePath, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteFileAsync_WithNonExistentFile_ShouldReturnFalse() {
        // Arrange
        const string nonExistentFile = "/uploads/non-existent-file.txt";
        _localFileStoreMock
            .Setup(x => x.DeleteIfExistsAsync(nonExistentFile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _service.DeleteFileAsync(nonExistentFile);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteFileAsync_WhenPathIsBlank_ReturnsFalseWithoutCallingStore() {
        var result = await _service.DeleteFileAsync("  ");

        result.Should().BeFalse();
        _localFileStoreMock.Verify(x => x.DeleteIfExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteFileAsync_WhenStoreThrows_ReturnsFalse() {
        _localFileStoreMock
            .Setup(x => x.DeleteIfExistsAsync("/uploads/manual.pdf", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("delete failed"));

        var result = await _service.DeleteFileAsync("/uploads/manual.pdf");

        result.Should().BeFalse();
        _localFileStoreMock.Verify(x => x.DeleteIfExistsAsync("/uploads/manual.pdf", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GetUploadConstraints_ShouldReturnValidConstraints() {
        // Act
        var constraints = _service.GetUploadConstraints();

        // Assert
        Assert.NotNull(constraints);
        Assert.True(constraints.MaxFileSizeBytes > 0);
        Assert.Contains("CSV", constraints.SupportedFileTypes);
        Assert.Contains("PDF", constraints.SupportedFileTypes);
        Assert.Contains(".csv", constraints.SupportedExtensions);
        Assert.Contains(".pdf", constraints.SupportedExtensions);
    }

    [Theory]
    [InlineData("test.csv", "text/csv", FileType.CSV)]
    [InlineData("manual.pdf", "application/pdf", FileType.PDF)]
    [InlineData("unknown.txt", "text/plain", FileType.Unknown)]
    public async Task ValidateFileAsync_ShouldDetectCorrectFileType(string fileName, string contentType, FileType expectedType) {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(contentType);

        // Arrange
        var content = fileName.EndsWith(".pdf") ? "%PDF-1.4 content" : "test,content";
        var (stream, metadata) = CreateMockFile(fileName, content, contentType);
        var options = new FileUploadOptions();

        // Act
        var result = await _service.ValidateFileAsync(stream, metadata, options);

        // Assert
        Assert.Equal(expectedType, result.DetectedFileType);
    }

    private (Stream stream, FileMetadata metadata) CreateMockFile(string fileName, string content, string contentType) {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        var metadata = new FileMetadata {
            FileName = fileName,
            ContentType = contentType,
            ContentLength = bytes.Length
        };
        return (stream, metadata);
    }
}
