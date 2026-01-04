using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Reliability tests for FileUploadService to ensure robust file handling
/// </summary>
public class FileUploadServiceReliabilityTests {
    private readonly Mock<ITelemetryService> _telemetryServiceMock;
    private readonly Mock<ILogger<FileUploadService>> _loggerMock;
    private readonly Mock<IOptions<FileUploadConfiguration>> _configMock;
    private readonly FileUploadService _service;

    public FileUploadServiceReliabilityTests() {
        _telemetryServiceMock = new Mock<ITelemetryService>();
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

        _service = new FileUploadService(_configMock.Object, _telemetryServiceMock.Object, _loggerMock.Object);
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
        Assert.True(File.Exists(result.FilePath));

        // Verify telemetry was tracked
        _telemetryServiceMock.Verify(x => x.TrackEvent("FileUploaded", It.IsAny<Dictionary<string, string>>()), Times.Once);

        // Cleanup
        if (File.Exists(result.FilePath))
            File.Delete(result.FilePath);
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
        Assert.True(File.Exists(result.FilePath));

        // Cleanup
        if (File.Exists(result.FilePath))
            File.Delete(result.FilePath);
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
        Assert.Contains("exceeds maximum allowed size", result.ValidationResult.Errors.First());
    }

    [Fact]
    public async Task UploadFileAsync_WithInvalidExtension_ShouldFail() {
        // Arrange
        var content = "Invalid file content";
        var (stream, metadata) = CreateMockFile("test.txt", content, "text/plain");
        var options = new FileUploadOptions();
        options.AllowedFileExtensions.Clear();
        options.AllowedFileExtensions.UnionWith(new[] { ".csv", ".pdf" });

        // Act
        var result = await _service.UploadFileAsync(stream, metadata, options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("not allowed", result.ValidationResult.Errors.First());
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
        options.AllowedFileExtensions.UnionWith(new[] { ".csv", ".pdf" });

        // Act
        var result = await _service.UploadFilesAsync(files, options);

        // Assert
        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(2, result.SuccessfulUploads);
        Assert.Equal(1, result.FailedUploads);
        Assert.False(result.AllFilesUploaded);

        // Cleanup valid uploads
        foreach (var uploadResult in result.Results.Where(r => r.IsValid)) {
            if (File.Exists(uploadResult.FilePath))
                File.Delete(uploadResult.FilePath);
        }
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
        Assert.Contains("missing PDF header", result.Errors.First());
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
        Assert.Contains("may not be a valid CSV", result.Warnings.FirstOrDefault() ?? "");
    }

    [Fact]
    public async Task DeleteFileAsync_WithExistingFile_ShouldReturnTrue() {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "test content");

        // Act
        var result = await _service.DeleteFileAsync(tempFile);

        // Assert
        Assert.True(result);
        Assert.False(File.Exists(tempFile));
    }

    [Fact]
    public async Task DeleteFileAsync_WithNonExistentFile_ShouldReturnFalse() {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), "non-existent-file.txt");

        // Act
        var result = await _service.DeleteFileAsync(nonExistentFile);

        // Assert
        Assert.False(result);
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
