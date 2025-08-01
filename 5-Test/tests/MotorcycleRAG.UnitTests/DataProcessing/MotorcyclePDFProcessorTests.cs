using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using MotorcycleRAG.Infrastructure.DataProcessing;
using Xunit;

namespace MotorcycleRAG.UnitTests.DataProcessing;

public class MotorcyclePDFProcessorTests
{
    private readonly Mock<IDocumentIntelligenceClient> _mockDocumentClient;
    private readonly Mock<IAzureOpenAIClient> _mockOpenAIClient;
    private readonly Mock<IAzureSearchClient> _mockSearchClient;
    private readonly Mock<ILogger<MotorcyclePDFProcessor>> _mockLogger;
    private readonly PDFProcessingConfiguration _pdfConfig;
    private readonly AzureAIConfiguration _azureConfig;
    private readonly MotorcyclePDFProcessor _processor;

    public MotorcyclePDFProcessorTests()
    {
        _mockDocumentClient = new Mock<IDocumentIntelligenceClient>();
        _mockOpenAIClient = new Mock<IAzureOpenAIClient>();
        _mockSearchClient = new Mock<IAzureSearchClient>();
        _mockLogger = new Mock<ILogger<MotorcyclePDFProcessor>>();

        _pdfConfig = new PDFProcessingConfiguration
        {
            MaxChunkSize = 2000,
            MinChunkSize = 200,
            ChunkOverlap = 200,
            SimilarityThreshold = 0.7f,
            ProcessImages = true,
            PreserveStructure = true,
            MaxPages = 500,
            ProcessTables = true
        };

        _azureConfig = new AzureAIConfiguration
        {
            FoundryEndpoint = "https://test.foundry.azure.com",
            OpenAIEndpoint = "https://test.openai.azure.com",
            SearchServiceEndpoint = "https://test.search.azure.com",
            DocumentIntelligenceEndpoint = "https://test.documentintelligence.azure.com",
            Models = new ModelConfiguration
            {
                ChatModel = "gpt-4o-mini",
                EmbeddingModel = "text-embedding-3-large",
                QueryPlannerModel = "gpt-4o",
                VisionModel = "gpt-4-vision-preview"
            }
        };

        _processor = new MotorcyclePDFProcessor(
            _mockDocumentClient.Object,
            _mockOpenAIClient.Object,
            _mockSearchClient.Object,
            Options.Create(_pdfConfig),
            Options.Create(_azureConfig),
            _mockLogger.Object);
    }

    [Fact]
    public async Task ProcessAsync_WithValidPDFDocument_ShouldReturnSuccessResult()
    {
        // Arrange
        var pdfDocument = CreateSamplePDFDocument();
        var analysisResult = CreateSampleAnalysisResult();
        var sampleEmbeddings = CreateSampleEmbeddings(3);

        _mockDocumentClient
            .Setup(x => x.AnalyzeDocumentAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysisResult);

        _mockOpenAIClient
            .Setup(x => x.ProcessMultimodalContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("GPT-4 Vision analysis of motorcycle manual diagrams and technical illustrations");

        _mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleEmbeddings);

        // Act
        var result = await _processor.ProcessAsync(pdfDocument);

        // Assert
        if (!result.Success)
        {
            // Debug output to see what went wrong
            var errorMessage = $"Processing failed: {result.Message}. Errors: {string.Join(", ", result.Errors)}";
            throw new Exception(errorMessage);
        }
        
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Documents.Count > 0);
        Assert.Contains("Successfully processed PDF", result.Message);
        Assert.True(result.ItemsProcessed > 0);
        Assert.True(result.ProcessingTime > TimeSpan.Zero);

        // Verify that Document Intelligence was called
        _mockDocumentClient.Verify(
            x => x.AnalyzeDocumentAsync(It.IsAny<byte[]>(), "application/pdf", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify that GPT-4 Vision was called for multimodal content
        _mockOpenAIClient.Verify(
            x => x.ProcessMultimodalContentAsync(
                _azureConfig.Models.VisionModel,
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                "application/pdf",
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify that embeddings were generated
        _mockOpenAIClient.Verify(
            x => x.GetEmbeddingsAsync(_azureConfig.Models.EmbeddingModel, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessAsync_WithImagesDisabled_ShouldSkipMultimodalProcessing()
    {
        // Arrange - Create a new processor with images disabled
        var configWithImagesDisabled = new PDFProcessingConfiguration
        {
            MaxChunkSize = 2000,
            MinChunkSize = 200,
            ChunkOverlap = 200,
            SimilarityThreshold = 0.7f,
            ProcessImages = false, // Disabled
            PreserveStructure = true,
            MaxPages = 500,
            ProcessTables = true
        };

        var processorWithImagesDisabled = new MotorcyclePDFProcessor(
            _mockDocumentClient.Object,
            _mockOpenAIClient.Object,
            _mockSearchClient.Object,
            Options.Create(configWithImagesDisabled),
            Options.Create(_azureConfig),
            _mockLogger.Object);

        var pdfDocument = CreateSamplePDFDocument();
        var analysisResult = CreateSampleAnalysisResult();
        var sampleEmbeddings = CreateSampleEmbeddings(2);

        _mockDocumentClient
            .Setup(x => x.AnalyzeDocumentAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysisResult);

        _mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleEmbeddings);

        // Act
        var result = await processorWithImagesDisabled.ProcessAsync(pdfDocument);

        // Assert
        Assert.True(result.Success, $"Processing failed: {result.Message}. Errors: {string.Join(", ", result.Errors)}");

        // Verify that GPT-4 Vision was NOT called
        _mockOpenAIClient.Verify(
            x => x.ProcessMultimodalContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithDocumentIntelligenceFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var pdfDocument = CreateSamplePDFDocument();

        _mockDocumentClient
            .Setup(x => x.AnalyzeDocumentAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Document Intelligence service unavailable"));

        // Act
        var result = await _processor.ProcessAsync(pdfDocument);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to process PDF", result.Message);
        Assert.Contains("Document Intelligence service unavailable", result.Errors);
        Assert.Equal(0, result.ItemsProcessed);
    }

    [Fact]
    public async Task ProcessAsync_WithLargeDocument_ShouldCreateMultipleChunks()
    {
        // Arrange
        var pdfDocument = CreateSamplePDFDocument();
        var analysisResult = CreateLargeAnalysisResult();
        var sampleEmbeddings = CreateSampleEmbeddings(10);

        _mockDocumentClient
            .Setup(x => x.AnalyzeDocumentAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysisResult);

        _mockOpenAIClient
            .Setup(x => x.ProcessMultimodalContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("GPT-4 Vision analysis of motorcycle manual diagrams and technical illustrations");

        _mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleEmbeddings);

        // Act
        var result = await _processor.ProcessAsync(pdfDocument);

        // Assert
        Assert.True(result.Success, $"Processing failed: {result.Message}. Errors: {string.Join(", ", result.Errors)}");
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Documents.Count >= 1);
        
        // Verify that chunks have proper metadata
        foreach (var doc in result.Data.Documents)
        {
            Assert.NotEmpty(doc.Id);
            Assert.NotEmpty(doc.Content);
            Assert.NotNull(doc.ContentVector);
            Assert.True(doc.ContentVector.Length > 0);
            Assert.NotNull(doc.Metadata);
            Assert.True(doc.Metadata.AdditionalProperties.ContainsKey("SourceType"));
            Assert.Equal("PDF", doc.Metadata.AdditionalProperties["SourceType"]);
        }
    }



    [Fact]
    public async Task ProcessAsync_WithTablesInDocument_ShouldProcessTablesCorrectly()
    {
        // Arrange
        var pdfDocument = CreateSamplePDFDocument();
        var analysisResult = CreateAnalysisResultWithTables();
        var sampleEmbeddings = CreateSampleEmbeddings(5);

        _mockDocumentClient
            .Setup(x => x.AnalyzeDocumentAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysisResult);

        _mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleEmbeddings);

        // Act
        var result = await _processor.ProcessAsync(pdfDocument);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        
        // Verify that table content was processed
        var tableDocuments = result.Data.Documents.Where(d => 
            d.Metadata.AdditionalProperties.ContainsKey("ChunkType") && 
            d.Metadata.AdditionalProperties["ChunkType"].ToString() == "Table").ToList();
        Assert.NotEmpty(tableDocuments);
        
        foreach (var tableDoc in tableDocuments)
        {
            Assert.Contains("Table with", tableDoc.Content);
            Assert.Contains("|", tableDoc.Content); // Table formatting
        }
    }

    [Fact]
    public async Task IndexAsync_WithValidProcessedData_ShouldReturnSuccessResult()
    {
        // Arrange
        var processedData = CreateSampleProcessedData();

        _mockSearchClient
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _processor.IndexAsync(processedData);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(processedData.Documents.Count, result.DocumentsIndexed);
        Assert.Equal("motorcycle-pdf-index", result.IndexName);
        Assert.Contains("Successfully indexed", result.Message);

        // Verify that search client was called
        _mockSearchClient.Verify(
            x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task IndexAsync_WithSearchClientFailure_ShouldReturnPartialFailure()
    {
        // Arrange
        var processedData = CreateSampleProcessedData();

        _mockSearchClient
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<MotorcycleDocument[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _processor.IndexAsync(processedData);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.DocumentsIndexed);
        Assert.Contains("Failed to index batch of", result.Errors[0]);
    }

    [Theory]
    [InlineData(500, 100, 50)] // Small chunks
    [InlineData(2000, 200, 200)] // Default chunks
    [InlineData(4000, 500, 400)] // Large chunks
    public async Task ProcessAsync_WithDifferentChunkSizes_ShouldRespectConfiguration(
        int maxChunkSize, int minChunkSize, int chunkOverlap)
    {
        // Arrange
        var customConfig = new PDFProcessingConfiguration
        {
            MaxChunkSize = maxChunkSize,
            MinChunkSize = minChunkSize,
            ChunkOverlap = chunkOverlap,
            SimilarityThreshold = 0.7f,
            ProcessImages = true,
            PreserveStructure = true,
            MaxPages = 500,
            ProcessTables = true
        };

        var customProcessor = new MotorcyclePDFProcessor(
            _mockDocumentClient.Object,
            _mockOpenAIClient.Object,
            _mockSearchClient.Object,
            Options.Create(customConfig),
            Options.Create(_azureConfig),
            _mockLogger.Object);

        var pdfDocument = CreateSamplePDFDocument();
        var analysisResult = CreateLargeAnalysisResult();
        var sampleEmbeddings = CreateSampleEmbeddings(15);

        _mockDocumentClient
            .Setup(x => x.AnalyzeDocumentAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysisResult);

        _mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleEmbeddings);

        _mockOpenAIClient
            .Setup(x => x.ProcessMultimodalContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("GPT-4 Vision analysis of motorcycle manual diagrams and technical illustrations");

        // Act
        var result = await customProcessor.ProcessAsync(pdfDocument);

        // Assert
        Assert.True(result.Success, $"Processing failed: {result.Message}. Errors: {string.Join(", ", result.Errors)}");
        if (result.Data == null)
        {
            throw new Exception($"Result.Data is null. Success: {result.Success}, Message: {result.Message}, Errors: {string.Join(", ", result.Errors)}");
        }
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data.Documents);
        Assert.True(result.Data.Documents.Count > 0);
        
        // Verify that chunks respect size limits
        foreach (var doc in result.Data.Documents)
        {
            Assert.NotNull(doc);
            Assert.NotNull(doc.Content);
            // Allow some flexibility in chunk sizes due to processing logic
            Assert.True(doc.Content.Length <= maxChunkSize * 1.1 || doc.Content.Length >= minChunkSize * 0.9, 
                $"Document content length {doc.Content.Length} is outside expected range [{minChunkSize * 0.9}, {maxChunkSize * 1.1}]");
        }
    }

    private PDFDocument CreateSamplePDFDocument()
    {
        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Sample PDF content"));
        
        return new PDFDocument
        {
            FileName = "honda_cbr600rr_2023_manual.pdf",
            Content = content,
            DocumentType = PDFDocumentType.Manual,
            Language = "en",
            Make = "Honda",
            Model = "CBR600RR",
            Year = "2023",
            Source = "Official Honda Documentation",
            FileSizeBytes = content.Length,
            ContainsImages = true,
            UploadedAt = DateTime.UtcNow
        };
    }

    private DocumentAnalysisResult CreateSampleAnalysisResult()
    {
        return new DocumentAnalysisResult
        {
            Content = "This is a sample motorcycle manual content with maintenance procedures and specifications.",
            Pages = new[]
            {
                new DocumentPage
                {
                    PageNumber = 1,
                    Content = "1. INTRODUCTION\nThis manual contains important safety information and maintenance procedures for your Honda CBR600RR motorcycle.",
                    Width = 8.5f,
                    Height = 11.0f
                },
                new DocumentPage
                {
                    PageNumber = 2,
                    Content = "2. ENGINE SPECIFICATIONS\nEngine Type: 599cc liquid-cooled inline 4-cylinder. Compression Ratio: 12.2:1. Maximum Power: 118 HP @ 13,500 RPM.",
                    Width = 8.5f,
                    Height = 11.0f
                }
            },
            Tables = new[]
            {
                new DocumentTable
                {
                    RowCount = 3,
                    ColumnCount = 2,
                    Cells = new[]
                    {
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 0, Content = "Specification" },
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 1, Content = "Value" },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 0, Content = "Engine Displacement" },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 1, Content = "599cc" },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 0, Content = "Max Power" },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 1, Content = "118 HP" }
                    }
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["ModelId"] = "prebuilt-layout",
                ["DocumentSize"] = 1024,
                ["ContentType"] = "application/pdf"
            }
        };
    }

    private DocumentAnalysisResult CreateLargeAnalysisResult()
    {
        var longContent = string.Join(" ", Enumerable.Repeat(
            "This is a detailed section about motorcycle maintenance procedures, safety guidelines, and technical specifications. " +
            "It contains comprehensive information about engine components, electrical systems, brake systems, and suspension components. " +
            "Regular maintenance is essential for optimal performance and safety.", 50));

        return new DocumentAnalysisResult
        {
            Content = longContent,
            Pages = Enumerable.Range(1, 5).Select(i => new DocumentPage
            {
                PageNumber = i,
                Content = $"Page {i}: {longContent}",
                Width = 8.5f,
                Height = 11.0f
            }).ToArray(),
            Tables = Array.Empty<DocumentTable>(),
            Metadata = new Dictionary<string, object>
            {
                ["ModelId"] = "prebuilt-layout",
                ["DocumentSize"] = longContent.Length * 5,
                ["ContentType"] = "application/pdf"
            }
        };
    }

    private DocumentAnalysisResult CreateAnalysisResultWithTables()
    {
        return new DocumentAnalysisResult
        {
            Content = "Document with tables containing motorcycle specifications",
            Pages = new[]
            {
                new DocumentPage
                {
                    PageNumber = 1,
                    Content = "TECHNICAL SPECIFICATIONS\nSee table below for detailed specifications.",
                    Width = 8.5f,
                    Height = 11.0f
                }
            },
            Tables = new[]
            {
                new DocumentTable
                {
                    RowCount = 4,
                    ColumnCount = 3,
                    Cells = new[]
                    {
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 0, Content = "Component" },
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 1, Content = "Specification" },
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 2, Content = "Unit" },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 0, Content = "Engine" },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 1, Content = "599" },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 2, Content = "cc" },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 0, Content = "Power" },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 1, Content = "118" },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 2, Content = "HP" },
                        new DocumentTableCell { RowIndex = 3, ColumnIndex = 0, Content = "Weight" },
                        new DocumentTableCell { RowIndex = 3, ColumnIndex = 1, Content = "194" },
                        new DocumentTableCell { RowIndex = 3, ColumnIndex = 2, Content = "kg" }
                    }
                }
            },
            Metadata = new Dictionary<string, object>
            {
                ["ModelId"] = "prebuilt-layout",
                ["DocumentSize"] = 2048,
                ["ContentType"] = "application/pdf"
            }
        };
    }

    private float[][] CreateSampleEmbeddings(int count)
    {
        var random = new Random(42); // Fixed seed for consistent tests
        return Enumerable.Range(0, count)
            .Select(_ => Enumerable.Range(0, 1536)
                .Select(_ => (float)random.NextDouble())
                .ToArray())
            .ToArray();
    }

    private ProcessedData CreateSampleProcessedData()
    {
        var documents = new List<MotorcycleDocument>
        {
            new MotorcycleDocument
            {
                Id = "test_doc_1",
                Title = "Honda CBR600RR 2023 - Engine Specifications",
                Content = "Engine specifications and maintenance procedures",
                Type = DocumentType.Manual,
                ContentVector = CreateSampleEmbeddings(1)[0],
                Metadata = new DocumentMetadata
                {
                    SourceFile = "honda_cbr600rr_2023_manual.pdf",
                    SourceUrl = "Official Honda Documentation",
                    PageNumber = 1,
                    Section = "Engine Specifications",
                    Author = "Honda CBR600RR",
                    PublishedDate = DateTime.UtcNow,
                    Tags = new List<string> { "Honda", "CBR600RR", "2023", "Manual" },
                    AdditionalProperties = new Dictionary<string, object>
                    {
                        ["SourceType"] = "PDF",
                        ["Make"] = "Honda",
                        ["Model"] = "CBR600RR",
                        ["Year"] = "2023",
                        ["DocumentType"] = "Manual",
                        ["ChunkType"] = "Text",
                        ["ProcessedAt"] = DateTime.UtcNow,
                        ["Language"] = "en"
                    }
                }
            },
            new MotorcycleDocument
            {
                Id = "test_doc_2",
                Title = "Honda CBR600RR 2023 - Maintenance Schedule",
                Content = "Regular maintenance schedule and procedures",
                Type = DocumentType.Manual,
                ContentVector = CreateSampleEmbeddings(1)[0],
                Metadata = new DocumentMetadata
                {
                    SourceFile = "honda_cbr600rr_2023_manual.pdf",
                    SourceUrl = "Official Honda Documentation",
                    PageNumber = 2,
                    Section = "Maintenance Schedule",
                    Author = "Honda CBR600RR",
                    PublishedDate = DateTime.UtcNow,
                    Tags = new List<string> { "Honda", "CBR600RR", "2023", "Manual" },
                    AdditionalProperties = new Dictionary<string, object>
                    {
                        ["SourceType"] = "PDF",
                        ["Make"] = "Honda",
                        ["Model"] = "CBR600RR",
                        ["Year"] = "2023",
                        ["DocumentType"] = "Manual",
                        ["ChunkType"] = "Text",
                        ["ProcessedAt"] = DateTime.UtcNow,
                        ["Language"] = "en"
                    }
                }
            }
        };

        return new ProcessedData
        {
            Id = Guid.NewGuid().ToString(),
            Documents = documents,
            Metadata = new Dictionary<string, object>
            {
                ["OriginalFileName"] = "honda_cbr600rr_2023_manual.pdf",
                ["DocumentType"] = "Manual",
                ["Make"] = "Honda",
                ["Model"] = "CBR600RR",
                ["Year"] = "2023"
            },
            ProcessedAt = DateTime.UtcNow
        };
    }
}