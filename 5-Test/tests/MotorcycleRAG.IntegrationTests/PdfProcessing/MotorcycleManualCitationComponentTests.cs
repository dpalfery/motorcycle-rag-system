using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Caching;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.DataProcessing;
using MotorcycleRAG.Persistence.Search;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.PdfProcessing;

/// <summary>
/// True component/slice test for manual PDF citation locator flow.
/// Tests the real processing pipeline: PDF processing â†’ indexing payload creation â†’ citation mapping.
/// 
/// What's REAL (not mocked):
/// - MotorcyclePdfProcessor chunking + locator enrichment logic
/// - MotorcycleRagService citation mapping into Citation.Locator
/// 
/// What's MOCKED (external dependencies):
/// - IDocumentIntelligenceClient - returns deterministic DocumentAnalysisResult
/// - IAzureFoundryClient - returns mock embeddings
/// - IAzureSearchClient - mocks Azure Search upload
/// </summary>
public class MotorcycleManualCitationComponentTests {
    [Fact]
    public async Task PdfProcessingToCitationMapping_FullFlow_PreservesLocatorMetadata() {
        // Arrange
        var mockDocumentClient = new Mock<IDocumentIntelligenceClient>();
        var mockOpenAIClient = new Mock<IAzureFoundryClient>();
        var mockSearchClient = new Mock<IAzureSearchClient>();
        var mockLogger = new Mock<ILogger<MotorcyclePdfProcessor>>();
        var mockIndexingLogger = new Mock<ILogger<MotorcycleIndexingService>>();
        var mockRagLogger = new Mock<ILogger<MotorcycleRagService>>();
        var mockOrchestrator = new Mock<IAgentOrchestrator>();
        var mockTelemetryService = new Mock<ITelemetryService>();
        var mockCacheService = new Mock<IQueryCacheService>();
        var mockSearchIndexClient = new Mock<Azure.Search.Documents.Indexes.SearchIndexClient>();

        // Create real instances of the service classes (they are concrete, not interfaces)
        var mockCitationLogger = new Mock<ILogger<MotorcycleRAG.Application.Services.Citations.ClaimCitationService>>();
        var mockRefinementLogger = new Mock<ILogger<MotorcycleRAG.Application.Services.QueryProcessing.QueryRefinementService>>();
        var mockLimitationLogger = new Mock<ILogger<MotorcycleRAG.Application.Services.ResponseProcessing.ResponseLimitationAnalyzer>>();

        var citationService = new MotorcycleRAG.Application.Services.Citations.ClaimCitationService(
            mockCitationLogger.Object);
        var refinementService = new MotorcycleRAG.Application.Services.QueryProcessing.QueryRefinementService(
            mockRefinementLogger.Object);
        var limitationAnalyzer = new MotorcycleRAG.Application.Services.ResponseProcessing.ResponseLimitationAnalyzer(
            mockLimitationLogger.Object);
        var costCalculator = new MotorcycleRAG.Application.Services.Metrics.QueryCostCalculator();

        // Create deterministic DocumentAnalysisResult with locator metadata
        var analysisResult = CreateDeterministicDocumentAnalysisResult();

        mockDocumentClient
            .Setup(x => x.AnalyzeDocumentAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(analysisResult);

        // Mock embeddings (avoiding actual Azure OpenAI call)
        mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateMockEmbedding(), CreateMockEmbedding(), CreateMockEmbedding() });

        // Mock multimodal content processing
        mockOpenAIClient
            .Setup(x => x.ProcessMultimodalContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Visual analysis: Technical diagrams showing engine components.");

        // Mock Azure Search upload (we're testing payload creation, not actual upload)
        mockSearchClient
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .Returns(Task.CompletedTask);

        // Setup options
        var pdfConfig = Options.Create(new PDFProcessingConfiguration {
            MaxChunkSize = 1000,
            MinChunkSize = 200,
            ChunkOverlap = 50,
            SimilarityThreshold = 0.85f,
            ProcessImages = false,
            PreserveStructure = true
        });

        var azureConfig = Options.Create(new AzureFoundryOptions {
            Models = new ModelOptions {
                EmbeddingModel = "text-embedding-3-large",
                VisionModel = "gpt-4-vision"
            }
        });

        var searchOptions = Options.Create(new SearchOptions {
            IndexName = "motorcycle-pdf-index",
            BatchSize = 100
        });

        var cacheConfig = Options.Create(new MotorcycleRAG.Application.Caching.CacheConfiguration {
            EnableCaching = false
        });

        // Create real processor with mocked dependencies
        var pdfProcessor = new MotorcyclePdfProcessor(
            mockDocumentClient.Object,
            mockOpenAIClient.Object,
            mockSearchClient.Object,
            pdfConfig,
            azureConfig,
            mockLogger.Object);

        var indexingService = new MotorcycleIndexingService(
            mockSearchClient.Object,
            mockSearchIndexClient.Object,
            searchOptions,
            mockIndexingLogger.Object);

        // Create test PDF document
        var pdfDocument = new PDFDocument {
            FileName = "Honda_CBR1000RR_Service_Manual_2024.pdf",
            Content = new MemoryStream(Encoding.UTF8.GetBytes("Test PDF content")),
            FileSizeBytes = 1_024_000,
            Make = "Honda",
            Model = "CBR1000RR",
            Year = "2024",
            DocumentType = PdfDocumentType.Manual,
            Language = "en",
            UploadedAt = DateTime.UtcNow,
            Source = "https://manuals.honda.com/cbr1000rr-2024",
            ContainsImages = false
        };

        // Act - Step 1: Process PDF through real processor
        var processedData = await pdfProcessor.ProcessAsync(pdfDocument);

        // Act - Step 2: Create indexing payload through real indexing service
        var indexingResult = await indexingService.IndexDocumentsAsync(processedData.Documents);

        // Act - Step 3: Simulate search results that would come from Azure Search
        // (using the same MotorcycleDocument objects that were created)
        var searchResults = CreateSimulatedSearchResultsFromDocuments(processedData.Documents);

        // Act - Step 4: Map search results to citations through real MotorcycleRagService logic
        // We'll use reflection to invoke the private CreateManualPdfLocator method
        var dependencies = new MotorcycleRagServiceDependencies(
            mockTelemetryService.Object,
            mockCacheService.Object,
            cacheConfig,
            citationService,
            refinementService,
            limitationAnalyzer,
            costCalculator);

        var ragService = new MotorcycleRagService(
            mockOrchestrator.Object,
            mockRagLogger.Object,
            dependencies);

        // Use reflection to access private method for testing
        var createLocatorMethod = typeof(MotorcycleRagService)
            .GetMethod("CreateLocatorForSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Assert - Step 1: Verify PDF processor created documents with locator metadata
        processedData.Documents.Should().NotBeEmpty("PDF processor should create documents");

        var pageDocuments = processedData.Documents.Where(d => d.Type == MotorcycleRAG.Domain.Enums.DocumentType.Manual).ToList();
        pageDocuments.Should().HaveCountGreaterThanOrEqualTo(2, "Should have at least 2 page chunks");

        // Assert - Step 2: Verify page documents have locator fields populated
        foreach (var doc in pageDocuments) {
            doc.PageNumber.Should().BeGreaterThan(0, "PageNumber should be populated");
            doc.PageRange.Should().NotBeNullOrEmpty("PageRange should be populated");
            doc.PrimarySection.Should().NotBeNullOrEmpty("PrimarySection should be extracted");
            doc.SectionLevel.Should().BeGreaterThan(0, "SectionLevel should be determined");
            doc.SectionHeadings.Should().NotBeEmpty("SectionHeadings array should contain entries");
            doc.ChunkIndex.Should().BeGreaterThanOrEqualTo(0, "ChunkIndex should be set");
        }

        // Assert - Step 3: Verify table document has correct page range shape
        var tableDocument = processedData.Documents.FirstOrDefault(d => d.Metadata.AdditionalProperties.ContainsKey("ChunkType") &&
            d.Metadata.AdditionalProperties["ChunkType"]?.ToString() == "Table");

        if (tableDocument != null) {
            tableDocument.TableCaption.Should().NotBeNullOrEmpty("Table caption should be extracted");

            // Only require a dash if this is actually a multi-page table
            var isMultiPage = tableDocument.Metadata.AdditionalProperties.ContainsKey("IsMultiPageTable") &&
                              tableDocument.Metadata.AdditionalProperties["IsMultiPageTable"] is bool b && b;

            if (isMultiPage) {
                tableDocument.PageRange.Should().Contain("-", "Multi-page table should have page range with dash");
            }
        }

        // Assert - Step 4: Verify indexing service processed documents
        indexingResult.Success.Should().BeTrue("Indexing should succeed");
        indexingResult.DocumentsIndexed.Should().Be(processedData.Documents.Count, "All documents should be indexed");

        // Assert - Step 5: Verify simulated search results have metadata
        searchResults.Should().NotBeEmpty("Search results should be created");

        foreach (var result in searchResults) {
            result.Metadata.Should().NotBeEmpty("Search result should have metadata");
            result.Source.AgentType.Should().Be(SearchAgentType.PDFSearch, "Should be PDF search agent type");
        }

        // Assert - Step 6: Verify citation mapping preserves locator metadata
        foreach (var result in searchResults) {
            var locator = createLocatorMethod?.Invoke(ragService, new object[] { result }) as ManualPdfCitationLocator;

            locator.Should().NotBeNull("Locator should be created");
            locator!.DocumentId.Should().Be(result.Source.DocumentId, "DocumentId should match");
            locator.PageNumber.Should().BeGreaterThan(0, "PageNumber should be preserved");
            locator.PageRange.Should().NotBeNullOrEmpty("PageRange should be preserved");
            locator.PrimarySection.Should().NotBeNullOrEmpty("PrimarySection should be preserved");
            locator.SectionLevel.Should().BeGreaterThan(0, "SectionLevel should be preserved");
            locator.SectionHeadings.Should().NotBeEmpty("SectionHeadings should be preserved");
            locator.ChunkIndex.Should().BeGreaterThanOrEqualTo(0, "ChunkIndex should be preserved");

            // Verify PrimarySection matches first section heading
            if (locator.SectionHeadings.Length > 0) {
                locator.PrimarySection.Should().Be(locator.SectionHeadings[0],
                    "PrimarySection should match first entry in SectionHeadings");
            }
        }

        // Assert - Step 7: Verify multi-page table has correct page range in citation
        var tableResult = searchResults.FirstOrDefault(r => r.Metadata.ContainsKey("TableCaption"));
        if (tableResult != null) {
            var tableLocator = createLocatorMethod?.Invoke(ragService, new object[] { tableResult }) as ManualPdfCitationLocator;
            tableLocator!.TableCaption.Should().NotBeNullOrEmpty("Table caption should be in locator");
            tableLocator.PageRange.Should().MatchRegex(@"^\d+(-\d+)?$",
                "Table page range should be in format 'N' or 'N-M'");
        }
    }

    [Fact]
    public async Task PdfProcessing_WithMultiPageTable_CreatesCorrectPageRangeLocator() {
        // Arrange
        var mockDocumentClient = new Mock<IDocumentIntelligenceClient>();
        var mockOpenAIClient = new Mock<IAzureFoundryClient>();
        var mockSearchClient = new Mock<IAzureSearchClient>();
        var mockLogger = new Mock<ILogger<MotorcyclePdfProcessor>>();

        // Create DocumentAnalysisResult with multi-page table
        var analysisResult = CreateDocumentAnalysisResultWithMultiPageTable();

        mockDocumentClient
            .Setup(x => x.AnalyzeDocumentAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(analysisResult);

        mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateMockEmbedding() });

        mockOpenAIClient
            .Setup(x => x.ProcessMultimodalContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Visual analysis completed.");

        mockSearchClient
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .Returns(Task.CompletedTask);

        var pdfConfig = Options.Create(new PDFProcessingConfiguration {
            MaxChunkSize = 1000,
            MinChunkSize = 200,
            ChunkOverlap = 50,
            SimilarityThreshold = 0.85f,
            ProcessImages = false,
            PreserveStructure = true
        });

        var azureConfig = Options.Create(new AzureFoundryOptions {
            Models = new ModelOptions {
                EmbeddingModel = "text-embedding-3-large",
                VisionModel = "gpt-4-vision"
            }
        });

        var pdfProcessor = new MotorcyclePdfProcessor(
            mockDocumentClient.Object,
            mockOpenAIClient.Object,
            mockSearchClient.Object,
            pdfConfig,
            azureConfig,
            mockLogger.Object);

        var pdfDocument = new PDFDocument {
            FileName = "Ducati_Panigale_V4_Manual.pdf",
            Content = new MemoryStream(Encoding.UTF8.GetBytes("Test PDF content")),
            FileSizeBytes = 2_048_000,
            Make = "Ducati",
            Model = "Panigale V4",
            Year = "2024",
            DocumentType = PdfDocumentType.Manual,
            Language = "en",
            UploadedAt = DateTime.UtcNow,
            Source = "https://manuals.ducati.com/panigale-v4-2024",
            ContainsImages = false
        };

        // Act
        var processedData = await pdfProcessor.ProcessAsync(pdfDocument);
        var tableDocument = processedData.Documents
            .FirstOrDefault(d => d.Metadata.AdditionalProperties.ContainsKey("ChunkType") &&
                d.Metadata.AdditionalProperties["ChunkType"]?.ToString() == "Table");

        // Assert
        tableDocument.Should().NotBeNull("Table document should be created");
        tableDocument!.PageNumber.Should().Be(5, "Table should start on page 5");
        tableDocument.PageRange.Should().Be("5-7", "Table should span pages 5-7");
        tableDocument.TableCaption.Should().Be("Torque Specifications", "Table caption should be extracted");
        tableDocument.PrimarySection.Should().Be("Engine Maintenance", "Table section should be determined");

        // Verify metadata contains multi-page table flag
        tableDocument.Metadata.AdditionalProperties.Should().ContainKey("IsMultiPageTable");
        tableDocument.Metadata.AdditionalProperties["IsMultiPageTable"].Should().Be(true);
    }

    [Fact]
    public async Task PdfProcessing_WithSectionHierarchy_CreatesCorrectSectionLevelLocator() {
        // Arrange
        var mockDocumentClient = new Mock<IDocumentIntelligenceClient>();
        var mockOpenAIClient = new Mock<IAzureFoundryClient>();
        var mockSearchClient = new Mock<IAzureSearchClient>();
        var mockLogger = new Mock<ILogger<MotorcyclePdfProcessor>>();

        // Create DocumentAnalysisResult with hierarchical sections
        var analysisResult = CreateDocumentAnalysisResultWithSectionHierarchy();

        mockDocumentClient
            .Setup(x => x.AnalyzeDocumentAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(analysisResult);

        mockOpenAIClient
            .Setup(x => x.GetEmbeddingsAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateMockEmbedding(), CreateMockEmbedding() });

        mockOpenAIClient
            .Setup(x => x.ProcessMultimodalContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Visual analysis completed.");

        mockSearchClient
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .Returns(Task.CompletedTask);

        var pdfConfig = Options.Create(new PDFProcessingConfiguration {
            MaxChunkSize = 1000,
            MinChunkSize = 200,
            ChunkOverlap = 50,
            SimilarityThreshold = 0.85f,
            ProcessImages = false,
            PreserveStructure = true
        });

        var azureConfig = Options.Create(new AzureFoundryOptions {
            Models = new ModelOptions {
                EmbeddingModel = "text-embedding-3-large",
                VisionModel = "gpt-4-vision"
            }
        });

        var pdfProcessor = new MotorcyclePdfProcessor(
            mockDocumentClient.Object,
            mockOpenAIClient.Object,
            mockSearchClient.Object,
            pdfConfig,
            azureConfig,
            mockLogger.Object);

        var pdfDocument = new PDFDocument {
            FileName = "Yamaha_YZF_R1_Manual.pdf",
            Content = new MemoryStream(Encoding.UTF8.GetBytes("Test PDF content")),
            FileSizeBytes = 1_536_000,
            Make = "Yamaha",
            Model = "YZF-R1",
            Year = "2024",
            DocumentType = PdfDocumentType.Manual,
            Language = "en",
            UploadedAt = DateTime.UtcNow,
            Source = "https://manuals.yamaha.com/yzf-r1-2024",
            ContainsImages = false
        };

        // Act
        var processedData = await pdfProcessor.ProcessAsync(pdfDocument);

        // Assert - Verify section hierarchy is captured
        var chapterDoc = processedData.Documents.FirstOrDefault(d => d.PrimarySection == "CHAPTER 1: Engine");
        chapterDoc.Should().NotBeNull("Chapter document should exist");
        chapterDoc!.SectionLevel.Should().Be(1, "Chapter should have level 1");
        chapterDoc.SectionHeadings.Should().Contain("CHAPTER 1: Engine");

        var sectionDoc = processedData.Documents.FirstOrDefault(d => d.PrimarySection == "Engine Maintenance");
        sectionDoc.Should().NotBeNull("Section document should exist");
        sectionDoc!.SectionLevel.Should().Be(2, "Section should have level 2");
        sectionDoc.SectionHeadings.Should().Contain("Engine Maintenance");

        var subsectionDoc = processedData.Documents.FirstOrDefault(d => d.PrimarySection == "Oil Change Procedure");
        subsectionDoc.Should().NotBeNull("Subsection document should exist");
        subsectionDoc!.SectionLevel.Should().Be(3, "Subsection should have level 3");
        subsectionDoc.SectionHeadings.Should().Contain("Oil Change Procedure");
    }

    /// <summary>
    /// Creates a deterministic DocumentAnalysisResult with pages, sections, and tables
    /// </summary>
    private static DocumentAnalysisResult CreateDeterministicDocumentAnalysisResult() {
        return new DocumentAnalysisResult {
            Content = "Honda CBR1000RR Service Manual 2024",
            Pages = new[]
            {
                new DocumentPage
                {
                    PageNumber = 1,
                    Content = """
                        CHAPTER 1: Engine
                        
                        1. Engine Maintenance
                        
                        This chapter covers the engine maintenance procedures for the Honda CBR1000RR.
                        Follow all safety precautions when working on the engine.
                        """,
                    Width = 595.0f,
                    Height = 842.0f,
                    PrimarySection = "Engine Maintenance",
                    SectionHeadings = new[] { "CHAPTER 1: Engine", "Engine Maintenance" },
                    SectionLevel = 2
                },
                new DocumentPage
                {
                    PageNumber = 2,
                    Content = """
                        2. Oil Change Procedure
                        
                        Step 1: Remove the drain plug and allow oil to drain completely.
                        Step 2: Replace the oil filter with a new OEM filter.
                        Step 3: Refill with recommended oil grade and quantity.
                        """,
                    Width = 595.0f,
                    Height = 842.0f,
                    PrimarySection = "Oil Change Procedure",
                    SectionHeadings = new[] { "CHAPTER 1: Engine", "Oil Change Procedure" },
                    SectionLevel = 3
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
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 0, Content = "Table 1.1: Torque Specifications", PageNumber = 1, IsHeader = true },
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 1, Content = "Torque (Nm)", PageNumber = 1, IsHeader = true },
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 2, Content = "Notes", PageNumber = 1, IsHeader = true },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 0, Content = "Oil drain plug", PageNumber = 1, IsHeader = false },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 1, Content = "30", PageNumber = 1, IsHeader = false },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 2, Content = "Use threadlocker", PageNumber = 1, IsHeader = false },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 0, Content = "Oil filter", PageNumber = 1, IsHeader = false },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 1, Content = "20", PageNumber = 1, IsHeader = false },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 2, Content = "OEM filter only", PageNumber = 1, IsHeader = false }
                    },
                    StartPageNumber = 1,
                    EndPageNumber = 1,
                    Caption = "Torque Specifications",
                    Section = "Engine Maintenance"
                }
            },
            Metadata = new Dictionary<string, object> {
                ["Title"] = "Honda CBR1000RR Service Manual 2024",
                ["Author"] = "Honda Motor Co., Ltd.",
                ["PageCount"] = 2
            }
        };
    }

    /// <summary>
    /// Creates DocumentAnalysisResult with multi-page table spanning pages 5-7
    /// </summary>
    private static DocumentAnalysisResult CreateDocumentAnalysisResultWithMultiPageTable() {
        return new DocumentAnalysisResult {
            Content = "Ducati Panigale V4 Service Manual",
            Pages = new[]
            {
                new DocumentPage
                {
                    PageNumber = 5,
                    Content = "Engine specifications and maintenance procedures.",
                    Width = 595.0f,
                    Height = 842.0f,
                    PrimarySection = "Engine Maintenance",
                    SectionHeadings = new[] { "Engine Maintenance" },
                    SectionLevel = 2
                }
            },
            Tables = new[]
            {
                new DocumentTable
                {
                    RowCount = 6,
                    ColumnCount = 3,
                    Cells = new[]
                    {
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 0, Content = "Table 2.3: Torque Specifications", PageNumber = 5, IsHeader = true },
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 1, Content = "Torque (Nm)", PageNumber = 5, IsHeader = true },
                        new DocumentTableCell { RowIndex = 0, ColumnIndex = 2, Content = "Notes", PageNumber = 5, IsHeader = true },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 0, Content = "Cylinder head bolts", PageNumber = 5, IsHeader = false },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 1, Content = "45", PageNumber = 5, IsHeader = false },
                        new DocumentTableCell { RowIndex = 1, ColumnIndex = 2, Content = "Torque in sequence", PageNumber = 5, IsHeader = false },
                        // Cells on page 6
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 0, Content = "Camshaft bolts", PageNumber = 6, IsHeader = false },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 1, Content = "25", PageNumber = 6, IsHeader = false },
                        new DocumentTableCell { RowIndex = 2, ColumnIndex = 2, Content = "Check clearances", PageNumber = 6, IsHeader = false },
                        new DocumentTableCell { RowIndex = 3, ColumnIndex = 0, Content = "Valve cover bolts", PageNumber = 6, IsHeader = false },
                        new DocumentTableCell { RowIndex = 3, ColumnIndex = 1, Content = "12", PageNumber = 6, IsHeader = false },
                        new DocumentTableCell { RowIndex = 3, ColumnIndex = 2, Content = "Use new gasket", PageNumber = 6, IsHeader = false },
                        // Cells on page 7
                        new DocumentTableCell { RowIndex = 4, ColumnIndex = 0, Content = "Oil pan bolts", PageNumber = 7, IsHeader = false },
                        new DocumentTableCell { RowIndex = 4, ColumnIndex = 1, Content = "18", PageNumber = 7, IsHeader = false },
                        new DocumentTableCell { RowIndex = 4, ColumnIndex = 2, Content = "Sealant required", PageNumber = 7, IsHeader = false },
                        new DocumentTableCell { RowIndex = 5, ColumnIndex = 0, Content = "Drain plug", PageNumber = 7, IsHeader = false },
                        new DocumentTableCell { RowIndex = 5, ColumnIndex = 1, Content = "35", PageNumber = 7, IsHeader = false },
                        new DocumentTableCell { RowIndex = 5, ColumnIndex = 2, Content = "Replace washer", PageNumber = 7, IsHeader = false }
                    },
                    StartPageNumber = 5,
                    EndPageNumber = 7,
                    Caption = "Torque Specifications",
                    Section = "Engine Maintenance"
                }
            },
            Metadata = new Dictionary<string, object> {
                ["Title"] = "Ducati Panigale V4 Service Manual",
                ["PageCount"] = 7
            }
        };
    }

    /// <summary>
    /// Creates DocumentAnalysisResult with hierarchical sections (chapter, section, subsection)
    /// </summary>
    private static DocumentAnalysisResult CreateDocumentAnalysisResultWithSectionHierarchy() {
        return new DocumentAnalysisResult {
            Content = "Yamaha YZF-R1 Service Manual",
            Pages = new[]
            {
                new DocumentPage
                {
                    PageNumber = 1,
                    Content = """
                        CHAPTER 1: Engine
                        
                        This chapter covers engine specifications and maintenance.
                        """,
                    Width = 595.0f,
                    Height = 842.0f,
                    PrimarySection = "CHAPTER 1: Engine",
                    SectionHeadings = new[] { "CHAPTER 1: Engine" },
                    SectionLevel = 1
                },
                new DocumentPage
                {
                    PageNumber = 2,
                    Content = """
                        1. Engine Maintenance
                        
                        Regular maintenance is essential for optimal performance.
                        """,
                    Width = 595.0f,
                    Height = 842.0f,
                    PrimarySection = "Engine Maintenance",
                    SectionHeadings = new[] { "CHAPTER 1: Engine", "Engine Maintenance" },
                    SectionLevel = 2
                },
                new DocumentPage
                {
                    PageNumber = 3,
                    Content = """
                        1.1 Oil Change Procedure
                        
                        Follow these steps to change the engine oil:
                        1. Warm up the engine
                        2. Drain the oil
                        3. Replace the filter
                        4. Refill with new oil
                        """,
                    Width = 595.0f,
                    Height = 842.0f,
                    PrimarySection = "Oil Change Procedure",
                    SectionHeadings = new[] { "CHAPTER 1: Engine", "Engine Maintenance", "Oil Change Procedure" },
                    SectionLevel = 3
                }
            },
            Tables = Array.Empty<DocumentTable>(),
            Metadata = new Dictionary<string, object> {
                ["Title"] = "Yamaha YZF-R1 Service Manual",
                ["PageCount"] = 3
            }
        };
    }

    /// <summary>
    /// Creates simulated search results from processed documents
    /// Simulates what would come back from Azure Search
    /// </summary>
    private static SearchResult[] CreateSimulatedSearchResultsFromDocuments(IEnumerable<MotorcycleDocument> documents) {
        var results = new List<SearchResult>();
        var timestamp = DateTime.UtcNow;

        var selection = documents
            .OrderByDescending(d => !string.IsNullOrWhiteSpace(d.TableCaption)) // Prefer a real table chunk first
            .ThenBy(d => d.Id)
            .Take(3)
            .ToList();

        foreach (var doc in selection) {
            var metadata = new Dictionary<string, object> {
                ["PageNumber"] = doc.PageNumber ?? 1,
                ["PageRange"] = doc.PageRange ?? "1",
                ["PrimarySection"] = doc.PrimarySection ?? "",
                ["SectionLevel"] = doc.SectionLevel ?? 0,
                ["SectionHeadings"] = doc.SectionHeadings?.ToArray() ?? Array.Empty<string>(),
                ["ChunkIndex"] = doc.ChunkIndex ?? 0
            };

            if (!string.IsNullOrWhiteSpace(doc.TableCaption)) {
                metadata["TableCaption"] = doc.TableCaption;
            }

            var result = new SearchResult {
                Id = doc.Id,
                Content = doc.Content,
                RelevanceScore = 0.85f + (results.Count * 0.05f),
                Source = new SearchSource {
                    AgentType = SearchAgentType.PDFSearch,
                    SourceName = doc.Title,
                    SourceUrl = doc.Metadata.SourceUrl?.ToString(),
                    DocumentId = doc.Id,
                    LastUpdated = timestamp
                },
                Metadata = metadata,
                GeneratedAt = timestamp
            };

            results.Add(result);
        }

        return results.ToArray();
    }

    /// <summary>
    /// Creates a mock embedding vector
    /// </summary>
    private static float[] CreateMockEmbedding() {
        var random = new Random(42); // Fixed seed for determinism
        var embedding = new float[1536];
        for (int i = 0; i < embedding.Length; i++) {
            embedding[i] = (float)random.NextDouble();
        }
        return embedding;
    }
}


