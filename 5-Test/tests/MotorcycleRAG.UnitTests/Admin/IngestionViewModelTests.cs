using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Admin.Processing;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.ViewModels;
using MotorcycleRAG.Domain.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Admin;

public class IngestionViewModelTests
{
    private readonly Mock<ApiClient> _apiClient;
    private readonly Mock<PdfChunker> _pdfChunker;
    private readonly Mock<CsvChunker> _csvChunker;
    private readonly Mock<OnnxEmbeddingService> _onnx;

    public IngestionViewModelTests()
    {
        _apiClient = new Mock<ApiClient>(MockBehavior.Strict, new HttpClient(), Mock.Of<IAdminAuthService>(), null);
        _pdfChunker = new Mock<PdfChunker>(MockBehavior.Strict);
        _csvChunker = new Mock<CsvChunker>(MockBehavior.Strict);
        _onnx = new Mock<OnnxEmbeddingService>(MockBehavior.Strict);
    }

    [Fact]
    public async Task ProcessFileAsync_ServerSide_WhenEmbeddingUnavailable()
    {
        // Arrange
        var vm = new IngestionViewModel(_apiClient.Object, _pdfChunker.Object, _csvChunker.Object, embeddingService: null, logger: NullLogger<IngestionViewModel>.Instance);
        vm.SelectedFilePath = Path.GetTempFileName();
        _apiClient.Setup(c => c.UploadFileAsync(It.IsAny<string>(), true, default)).ReturnsAsync(new FileUploadResult { FileId = "fid" });

        // Act
        await vm.ProcessFileCommand.ExecuteAsync(null);

        // Assert
        _apiClient.Verify(c => c.UploadFileAsync(vm.SelectedFilePath, true, default), Times.Once);
        Assert.Equal("Completed", vm.ProcessedFiles[^1].Status);
    }

    [Fact]
    public async Task ProcessLocallyAsync_Pdf_Succeeds()
    {
        // Arrange
        var vm = new IngestionViewModel(_apiClient.Object, _pdfChunker.Object, _csvChunker.Object, _onnx.Object, NullLogger<IngestionViewModel>.Instance);
        var tmp = Path.GetTempFileName() + ".pdf";
        File.WriteAllText(tmp, "pdf");
        vm.SelectedFilePath = tmp;
        var chunkResult = new PdfChunkingResult
        {
            Success = true,
            Chunks = new List<DocumentChunk> { new DocumentChunk { Text = "t", PageNumber = 1 } },
            Metadata = new PdfMetadata { PageCount = 1 }
        };
        _pdfChunker.Setup(c => c.ProcessPdfAsync(tmp, default)).ReturnsAsync(chunkResult);
        _apiClient.Setup(c => c.UploadFileAsync(tmp, true, default)).ReturnsAsync(new FileUploadResult { FileId = "fid" });
        _onnx.Setup(o => o.GenerateEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(new EmbeddingResult { Success = true, Embedding = new float[] { 0.1f } });

        // Act
        await vm.ProcessFileCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Completed", vm.ProcessedFiles[^1].Status);
        _pdfChunker.Verify(c => c.ProcessPdfAsync(tmp, default), Times.Once);
    }
}
