using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Pipeline;

public sealed class DataPipelineOrchestratorTests : IDisposable
{
    private readonly Mock<IDataProcessor<PDFDocument>> _pdfProcessorMock = new();
    private readonly Mock<IDataProcessor<CSVFile>> _csvProcessorMock = new();
    private readonly Mock<IFileUploadService> _fileUploadServiceMock = new();
    private readonly Mock<IAzureSearchDocumentService> _searchServiceMock = new();
    private readonly string _tempDirectory;
    private readonly ILogger<DataPipelineOrchestrator> _logger = NullLogger<DataPipelineOrchestrator>.Instance;

    public DataPipelineOrchestratorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"DataPipelineOrchestratorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _fileUploadServiceMock
            .Setup(x => x.ValidateFileAsync(It.IsAny<Stream>(), It.IsAny<FileMetadata>(), It.IsAny<FileUploadOptions>()))
            .ReturnsAsync(new FileValidationResult { IsValid = true });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenPdfProcessorIsNull()
    {
        var act = () => new DataPipelineOrchestrator(
            null!,
            _csvProcessorMock.Object,
            _fileUploadServiceMock.Object,
            _searchServiceMock.Object,
            Options.Create(new PipelineConfiguration()),
            _logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("pdfProcessor");
    }

    [Fact]
    public async Task ProcessFileAsync_WhenPdfProcessingAndIndexingSucceed_ReturnsCompleted()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        var request = new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF,
            Metadata = new Dictionary<string, object>
            {
                ["DocumentType"] = "ServiceGuide",
                ["Make"] = "Honda",
                ["Model"] = "Africa Twin",
                ["Year"] = "2024",
                ["Source"] = "blob://manual.pdf"
            }
        };
        PDFDocument? capturedDocument = null;
        _pdfProcessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<PDFDocument>()))
            .Callback<PDFDocument>(document => capturedDocument = document)
            .ReturnsAsync(CreateProcessedData(2));
        _searchServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(request);

        result.Status.Should().Be(PipelineStatus.Completed);
        result.Message.Should().Be("Successfully processed and indexed 2 documents.");
        result.IndexingResult.Should().NotBeNull();
        result.IndexingResult!.Success.Should().BeTrue();
        result.Metrics["DocumentsExtracted"].Should().Be(2);
        result.Metrics["DocumentsIndexed"].Should().Be(2);
        result.Metrics["FileExtension"].Should().Be(".pdf");
        capturedDocument.Should().NotBeNull();
        capturedDocument!.DocumentType.Should().Be(PdfDocumentType.ServiceGuide);
        capturedDocument.Make.Should().Be("Honda");
        capturedDocument.Model.Should().Be("Africa Twin");
        capturedDocument.Year.Should().Be("2024");
        capturedDocument.Source.Should().Be("blob://manual.pdf");
    }

    [Fact]
    public async Task ProcessFileAsync_WhenCsvProcessingSucceedsWithoutIndexing_ReturnsCompletedAndMapsMetadata()
    {
        var filePath = CreateFile("motorcycles.csv", "Make,Model\nBMW,R1250GS");
        var request = new DataPipelineRequest
        {
            FileName = "motorcycles.csv",
            FilePath = filePath,
            FileType = FileType.CSV,
            Options = new PipelineOptions { IndexImmediately = false },
            Metadata = new Dictionary<string, object>
            {
                ["Source"] = "ingest://csv",
                ["Batch"] = 12,
                ["Region"] = "EU"
            }
        };
        CSVFile? capturedFile = null;
        _csvProcessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<CSVFile>()))
            .Callback<CSVFile>(file => capturedFile = file)
            .ReturnsAsync(CreateProcessedData(1));

        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(request);

        result.Status.Should().Be(PipelineStatus.Completed);
        result.Message.Should().Be("Successfully processed 1 documents. Indexing skipped.");
        result.IndexingResult.Should().BeNull();
        _searchServiceMock.Verify(
            x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
        capturedFile.Should().NotBeNull();
        capturedFile!.Source.Should().Be("ingest://csv");
        capturedFile.Metadata.Should().Equal(new Dictionary<string, string>
        {
            ["Batch"] = "12",
            ["Region"] = "EU",
            ["Source"] = "ingest://csv"
        });
    }

    [Fact]
    public async Task ProcessFileAsync_WhenIndexingReturnsFalse_ReturnsPartiallyCompleted()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        var request = new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF
        };
        _pdfProcessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<PDFDocument>()))
            .ReturnsAsync(CreateProcessedData(2));
        _searchServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(request);

        result.Status.Should().Be(PipelineStatus.PartiallyCompleted);
        result.Message.Should().Contain("indexing failed");
        result.Errors.Should().Contain("Azure AI Search indexing failed.");
        result.IndexingResult.Should().NotBeNull();
        result.IndexingResult!.Success.Should().BeFalse();
        result.IndexingResult.DocumentsIndexed.Should().Be(2);
    }

    [Fact]
    public async Task ProcessFileAsync_WhenValidationFails_ReturnsFailedWithoutProcessing()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        var validationResult = new FileValidationResult();
        validationResult.AddError("bad upload");
        _fileUploadServiceMock
            .Setup(x => x.ValidateFileAsync(It.IsAny<Stream>(), It.IsAny<FileMetadata>(), It.IsAny<FileUploadOptions>()))
            .ReturnsAsync(validationResult);

        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF
        });

        result.Status.Should().Be(PipelineStatus.Failed);
        result.Message.Should().Be("File validation failed: bad upload");
        result.Errors.Should().ContainSingle().Which.Should().Be("bad upload");
        _pdfProcessorMock.Verify(x => x.ProcessAsync(It.IsAny<PDFDocument>()), Times.Never);
    }

    [Fact]
    public async Task ProcessFileAsync_WhenFileIsMissing_ReturnsFailed()
    {
        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "missing.pdf",
            FilePath = Path.Combine(_tempDirectory, "missing.pdf"),
            FileType = FileType.PDF
        });

        result.Status.Should().Be(PipelineStatus.Failed);
        result.Message.Should().Contain("File validation failed");
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("File not found at path");
    }

    [Fact]
    public async Task ProcessFileAsync_WhenProcessorReturnsNull_ReturnsFailed()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        _pdfProcessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<PDFDocument>()))
            .ReturnsAsync((ProcessedData?)null);

        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF
        });

        result.Status.Should().Be(PipelineStatus.Failed);
        result.Message.Should().Be("Processing returned no data.");
    }

    [Fact]
    public async Task ProcessFileAsync_WhenCancellationIsRequestedBeforeStart_ReturnsCancelled()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF
        }, cts.Token);

        result.Status.Should().Be(PipelineStatus.Cancelled);
        result.Message.Should().Be("Pipeline execution was cancelled before processing started.");
        _pdfProcessorMock.Verify(x => x.ProcessAsync(It.IsAny<PDFDocument>()), Times.Never);
    }

    [Fact]
    public async Task ProcessFileAsync_WhenCancellationOccursAfterProcessing_ReturnsCancelled()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        using var cts = new CancellationTokenSource();
        _pdfProcessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<PDFDocument>()))
            .ReturnsAsync(() =>
            {
                cts.Cancel();
                return CreateProcessedData(1);
            });
        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF
        }, cts.Token);

        result.Status.Should().Be(PipelineStatus.Cancelled);
        result.Message.Should().Be("Pipeline execution was cancelled after processing.");
        _searchServiceMock.Verify(
            x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessFileAsync_WhenCancellationOccursAfterFileReading_ReturnsCancelled()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        using var cts = new CancellationTokenSource();
        _fileUploadServiceMock
            .Setup(x => x.ValidateFileAsync(It.IsAny<Stream>(), It.IsAny<FileMetadata>(), It.IsAny<FileUploadOptions>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(new FileValidationResult { IsValid = true });
        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF
        }, cts.Token);

        result.Status.Should().Be(PipelineStatus.Cancelled);
        result.Message.Should().Be("Pipeline execution was cancelled after file reading.");
        _pdfProcessorMock.Verify(x => x.ProcessAsync(It.IsAny<PDFDocument>()), Times.Never);
    }

    [Fact]
    public async Task ProcessFileAsync_WhenFileTypeIsUnsupported_ReturnsFailed()
    {
        var filePath = CreateFile("manual.bin", "binary");
        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "manual.bin",
            FilePath = filePath,
            FileType = (FileType)999
        });

        result.Status.Should().Be(PipelineStatus.Failed);
        result.Message.Should().Be("Pipeline execution failed: File type 999 is not supported");
        result.Errors.Should().ContainSingle().Which.Should().Be("File type 999 is not supported");
    }

    [Fact]
    public async Task ProcessFileAsync_WhenProcessorThrowsOperationCanceledException_ReturnsCancelled()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        _pdfProcessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<PDFDocument>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF
        });

        result.Status.Should().Be(PipelineStatus.Cancelled);
        result.Message.Should().Be("Pipeline execution was cancelled.");
    }

    [Fact]
    public async Task ProcessFileAsync_WhenValidationThrows_ReturnsFailedWithReadError()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        _fileUploadServiceMock
            .Setup(x => x.ValidateFileAsync(It.IsAny<Stream>(), It.IsAny<FileMetadata>(), It.IsAny<FileUploadOptions>()))
            .ThrowsAsync(new InvalidOperationException("validation exploded"));
        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF
        });

        result.Status.Should().Be(PipelineStatus.Failed);
        result.Message.Should().Be("File validation failed: Failed to read file: validation exploded");
        result.Errors.Should().ContainSingle().Which.Should().Be("Failed to read file: validation exploded");
    }

    [Fact]
    public async Task ProcessFileAsync_WhenIndexingThrows_ReturnsPartiallyCompleted()
    {
        var filePath = CreateFile("manual.pdf", "%PDF-1.4 test");
        _pdfProcessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<PDFDocument>()))
            .ReturnsAsync(CreateProcessedData(2));
        _searchServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search down"));
        var sut = CreateSut();

        var result = await sut.ProcessFileAsync(new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = filePath,
            FileType = FileType.PDF
        });

        result.Status.Should().Be(PipelineStatus.PartiallyCompleted);
        result.Message.Should().Be("Processing completed but indexing failed: Indexing failed: search down");
        result.Errors.Should().Contain("search down");
        result.IndexingResult.Should().NotBeNull();
        result.IndexingResult!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessBatchAsync_AggregatesCompletedPartialAndFailedResults()
    {
        var completedRequest = new DataPipelineRequest
        {
            FileName = "complete.csv",
            FilePath = CreateFile("complete.csv", "Make,Model\nHonda,CBR"),
            FileType = FileType.CSV,
            Options = new PipelineOptions { IndexImmediately = false }
        };
        var partialRequest = new DataPipelineRequest
        {
            FileName = "partial.pdf",
            FilePath = CreateFile("partial.pdf", "%PDF-1.4 test"),
            FileType = FileType.PDF
        };
        var failedRequest = new DataPipelineRequest
        {
            FileName = "missing.pdf",
            FilePath = Path.Combine(_tempDirectory, "missing.pdf"),
            FileType = FileType.PDF
        };
        _csvProcessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<CSVFile>()))
            .ReturnsAsync(CreateProcessedData(1));
        _pdfProcessorMock
            .Setup(x => x.ProcessAsync(It.Is<PDFDocument>(doc => doc.FileName == "partial.pdf")))
            .ReturnsAsync(CreateProcessedData(2));
        _searchServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut(maxConcurrentProcessing: 2);

        var result = await sut.ProcessBatchAsync([completedRequest, partialRequest, failedRequest]);

        result.TotalFiles.Should().Be(3);
        result.ProcessedSuccessfully.Should().Be(1);
        result.ProcessedWithErrors.Should().Be(1);
        result.Failed.Should().Be(1);
        result.Results.Should().HaveCount(3);
        result.HasErrors.Should().BeTrue();
        result.BatchMetrics["AverageProcessingTimeMs"].Should().BeOfType<long>();
    }

    [Fact]
    public async Task GetPipelineMetricsAsync_AggregatesExecutionsStatusesAndRecentItems()
    {
        var successfulRequest = new DataPipelineRequest
        {
            FileName = "manual.pdf",
            FilePath = CreateFile("manual.pdf", "%PDF-1.4 test"),
            FileType = FileType.PDF,
            Options = new PipelineOptions { IndexImmediately = false }
        };
        var failedRequest = new DataPipelineRequest
        {
            FileName = "missing.csv",
            FilePath = Path.Combine(_tempDirectory, "missing.csv"),
            FileType = FileType.CSV
        };
        _pdfProcessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<PDFDocument>()))
            .ReturnsAsync(CreateProcessedData(1));

        var sut = CreateSut();

        var successfulResult = await sut.ProcessFileAsync(successfulRequest);
        var failedResult = await sut.ProcessFileAsync(failedRequest);
        var metrics = await sut.GetPipelineMetricsAsync();
        var knownStatus = await sut.GetPipelineStatusAsync(successfulResult.ExecutionId);
        var unknownStatus = await sut.GetPipelineStatusAsync("missing-execution");
        var cancelled = await sut.CancelPipelineAsync("missing-execution");

        knownStatus.Should().Be(PipelineStatus.Completed);
        unknownStatus.Should().Be(PipelineStatus.Failed);
        cancelled.Should().BeFalse();
        metrics.TotalExecutions.Should().Be(2);
        metrics.SuccessfulExecutions.Should().Be(1);
        metrics.FailedExecutions.Should().Be(1);
        metrics.TotalDocumentsProcessed.Should().Be(1);
        metrics.TotalDocumentsIndexed.Should().Be(0);
        metrics.ProcessingByFileType[FileType.PDF].Should().Be(1);
        metrics.ProcessingByFileType[FileType.CSV].Should().Be(1);
        metrics.ExecutionsByStatus[PipelineStatus.Completed].Should().Be(1);
        metrics.ExecutionsByStatus[PipelineStatus.Failed].Should().Be(1);
        metrics.RecentExecutions.Should().Contain(x => x.ExecutionId == successfulResult.ExecutionId);
        metrics.RecentExecutions.Should().Contain(x => x.ExecutionId == failedResult.ExecutionId);
    }

    private DataPipelineOrchestrator CreateSut(bool enableAutoIndexing = true, int maxConcurrentProcessing = 5)
    {
        return new DataPipelineOrchestrator(
            _pdfProcessorMock.Object,
            _csvProcessorMock.Object,
            _fileUploadServiceMock.Object,
            _searchServiceMock.Object,
            Options.Create(new PipelineConfiguration
            {
                EnableAutoIndexing = enableAutoIndexing,
                MaxConcurrentProcessing = maxConcurrentProcessing
            }),
            _logger);
    }

    private string CreateFile(string fileName, string content)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static ProcessedData CreateProcessedData(int documentCount)
    {
        var processedData = new ProcessedData();
        for (var i = 0; i < documentCount; i++)
        {
            processedData.Documents.Add(new MotorcycleDocument
            {
                Id = $"doc-{i}",
                Title = $"Document {i}",
                Content = $"Content {i}",
                Type = DocumentType.Manual
            });
        }

        return processedData;
    }
}
