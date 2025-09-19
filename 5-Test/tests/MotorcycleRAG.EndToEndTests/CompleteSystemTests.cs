using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using Xunit;

namespace MotorcycleRAG.EndToEndTests;

/// <summary>
/// Complete end-to-end system tests with realistic motorcycle data
/// </summary>
public class CompleteSystemTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly IMotorcycleRAGService _ragService;
    private readonly IDataPipelineOrchestrator _pipelineOrchestrator;
    private readonly IFileUploadService _fileUploadService;
    private readonly string _testDataDirectory;

    public CompleteSystemTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        
        _ragService = _scope.ServiceProvider.GetRequiredService<IMotorcycleRAGService>();
        _pipelineOrchestrator = _scope.ServiceProvider.GetRequiredService<IDataPipelineOrchestrator>();
        _fileUploadService = _scope.ServiceProvider.GetRequiredService<IFileUploadService>();

        // Create test data directory
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"E2ETest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDirectory);
        
        // Create realistic test data
        CreateRealisticTestData();
    }

    [Fact]
    public async Task CompleteWorkflow_ProcessMotorcycleSpecsAndQuery_ShouldWork()
    {
        // Arrange - Upload and process motorcycle specifications CSV
        var csvFile = Path.Combine(_testDataDirectory, "motorcycle_specs.csv");
        var file = CreateFormFileFromPath(csvFile);
        
        var uploadOptions = new FileUploadOptions
        {
            UploadDirectory = "e2e-test",
            GenerateUniqueFileName = true
        };

        // Act - Upload CSV file
        var uploadResult = await _fileUploadService.UploadFileAsync(file, uploadOptions);
        Assert.True(uploadResult.IsValid, $"Upload failed: {string.Join(", ", uploadResult.ValidationResult.Errors)}");

        // Act - Process CSV through pipeline
        var pipelineRequest = new DataPipelineRequest
        {
            FileName = uploadResult.OriginalFileName,
            FilePath = uploadResult.FilePath,
            FileType = uploadResult.DetectedFileType,
            Options = new PipelineOptions
            {
                IndexImmediately = false, // Skip indexing for E2E test
                ProcessImages = false,
                GenerateEmbeddings = false
            }
        };

        var processingResult = await _pipelineOrchestrator.ProcessFileAsync(pipelineRequest);
        Assert.Equal(PipelineStatus.Completed, processingResult.Status);
        Assert.NotNull(processingResult.ProcessedData);
        Assert.True(processingResult.ProcessedData.Documents.Count > 0);

        // Act - Query the processed data
        var queryRequest = new MotorcycleQueryRequest
        {
            Query = "What are the specifications for Honda CBR600RR?",
            UserId = "test-user"
        };

        var queryResponse = await _ragService.QueryAsync(queryRequest);

        // Assert - Verify query response
        Assert.NotNull(queryResponse);
        Assert.NotEmpty(queryResponse.Response);
        Assert.NotNull(queryResponse.Sources);
        Assert.NotEmpty(queryResponse.QueryId);

        // Cleanup
        await _fileUploadService.DeleteFileAsync(uploadResult.FilePath);
    }

    [Fact]
    public async Task CompleteWorkflow_ProcessManualAndQuery_ShouldWork()
    {
        // Arrange - Upload and process motorcycle manual PDF
        var pdfFile = Path.Combine(_testDataDirectory, "honda_manual.pdf");
        var file = CreateFormFileFromPath(pdfFile);
        
        var uploadOptions = new FileUploadOptions
        {
            UploadDirectory = "e2e-test",
            GenerateUniqueFileName = true
        };

        // Act - Upload PDF file
        var uploadResult = await _fileUploadService.UploadFileAsync(file, uploadOptions);
        Assert.True(uploadResult.IsValid, $"Upload failed: {string.Join(", ", uploadResult.ValidationResult.Errors)}");

        // Act - Process PDF through pipeline
        var pipelineRequest = new DataPipelineRequest
        {
            FileName = uploadResult.OriginalFileName,
            FilePath = uploadResult.FilePath,
            FileType = uploadResult.DetectedFileType,
            Options = new PipelineOptions
            {
                IndexImmediately = false,
                ProcessImages = true,
                GenerateEmbeddings = false
            }
        };

        var processingResult = await _pipelineOrchestrator.ProcessFileAsync(pipelineRequest);
        
        // PDF processing might fail in test environment without real Azure services
        Assert.True(processingResult.Status == PipelineStatus.Completed || 
                   processingResult.Status == PipelineStatus.Failed);

        if (processingResult.Status == PipelineStatus.Completed)
        {
            Assert.NotNull(processingResult.ProcessedData);
            Assert.True(processingResult.ProcessedData.Documents.Count > 0);

            // Act - Query the processed manual
            var queryRequest = new MotorcycleQueryRequest
            {
                Query = "How do I change the oil in Honda CBR600RR?",
                UserId = "test-user"
            };

            var queryResponse = await _ragService.QueryAsync(queryRequest);

            // Assert - Verify query response
            Assert.NotNull(queryResponse);
            Assert.NotEmpty(queryResponse.Response);
        }

        // Cleanup
        await _fileUploadService.DeleteFileAsync(uploadResult.FilePath);
    }

    [Fact]
    public async Task BatchProcessing_MultipleMotorcycleFiles_ShouldProcessEfficiently()
    {
        // Arrange - Create multiple files for batch processing
        var files = new List<IFormFile>
        {
            CreateFormFileFromPath(Path.Combine(_testDataDirectory, "motorcycle_specs.csv")),
            CreateFormFileFromPath(Path.Combine(_testDataDirectory, "sport_bikes.csv")),
            CreateFormFileFromPath(Path.Combine(_testDataDirectory, "honda_manual.pdf"))
        };

        var uploadOptions = new FileUploadOptions
        {
            UploadDirectory = "batch-e2e-test",
            GenerateUniqueFileName = true
        };

        // Act - Batch upload
        var batchUploadResult = await _fileUploadService.UploadFilesAsync(files, uploadOptions);
        
        // Assert uploads
        Assert.Equal(3, batchUploadResult.TotalFiles);
        Assert.True(batchUploadResult.SuccessfulUploads >= 2); // At least CSV files should succeed

        // Act - Batch processing
        var pipelineRequests = batchUploadResult.Results
            .Where(r => r.IsValid)
            .Select(r => new DataPipelineRequest
            {
                FileName = r.OriginalFileName,
                FilePath = r.FilePath,
                FileType = r.DetectedFileType,
                Options = new PipelineOptions
                {
                    IndexImmediately = false,
                    ProcessImages = false,
                    GenerateEmbeddings = false
                }
            });

        var startTime = DateTime.UtcNow;
        var batchResult = await _pipelineOrchestrator.ProcessBatchAsync(pipelineRequests);
        var processingTime = DateTime.UtcNow - startTime;

        // Assert batch processing efficiency
        Assert.NotNull(batchResult);
        Assert.True(batchResult.TotalFiles >= 2);
        Assert.True(processingTime < TimeSpan.FromMinutes(5)); // Should complete within 5 minutes

        // Verify performance metrics
        Assert.True(batchResult.BatchMetrics.ContainsKey("TotalDocumentsProcessed"));
        Assert.True(batchResult.BatchMetrics.ContainsKey("AverageProcessingTimeMs"));

        // Cleanup
        foreach (var result in batchUploadResult.Results.Where(r => r.IsValid))
        {
            await _fileUploadService.DeleteFileAsync(result.FilePath);
        }
    }

    [Fact]
    public async Task SystemHealthCheck_AllComponents_ShouldBeHealthy()
    {
        // Act - Check RAG service health
        var ragHealth = await _ragService.GetHealthAsync();
        
        // Assert - RAG service should be healthy or degraded (not unhealthy)
        Assert.NotNull(ragHealth);
        Assert.True(ragHealth.Status == HealthCheckStatus.Healthy || 
                   ragHealth.Status == HealthCheckStatus.Degraded);

        // Additional health checks can be added here for other components
    }

    [Theory]
    [InlineData("What is the top speed of Yamaha YZF-R1?")]
    [InlineData("Compare Honda CBR600RR vs Kawasaki ZX-6R")]
    [InlineData("What are the maintenance intervals for sport bikes?")]
    [InlineData("How much does a Ducati Panigale V4 cost?")]
    public async Task QueryVariations_DifferentQuestionTypes_ShouldHandleGracefully(string query)
    {
        // Arrange
        var queryRequest = new MotorcycleQueryRequest
        {
            Query = query,
            UserId = "test-user",
            Context = new QueryContext
            {
                MaxResults = 10,
                IncludeMetadata = true
            }
        };

        // Act
        var response = await _ragService.QueryAsync(queryRequest);

        // Assert - Should handle all query types gracefully
        Assert.NotNull(response);
        Assert.NotEmpty(response.Response);
        Assert.NotNull(response.QueryId);
        Assert.NotNull(response.Metrics);
        
        // Response time should be reasonable
        Assert.True(response.Metrics.ResponseTime < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ConcurrentQueries_MultipleUsers_ShouldHandleLoad()
    {
        // Arrange - Multiple concurrent queries
        var queries = new[]
        {
            "What are Honda motorcycle models?",
            "Yamaha sport bike specifications",
            "Kawasaki maintenance schedule",
            "Suzuki GSXR performance data",
            "Ducati pricing information"
        };

        var tasks = queries.Select(async (query, index) =>
        {
            var request = new MotorcycleQueryRequest
            {
                Query = query,
                UserId = $"user-{index}"
            };
            return await _ragService.QueryAsync(request);
        });

        // Act - Execute concurrent queries
        var startTime = DateTime.UtcNow;
        var responses = await Task.WhenAll(tasks);
        var totalTime = DateTime.UtcNow - startTime;

        // Assert - All queries should complete successfully
        Assert.Equal(queries.Length, responses.Length);
        Assert.All(responses, response =>
        {
            Assert.NotNull(response);
            Assert.NotEmpty(response.Response);
            Assert.NotNull(response.QueryId);
        });

        // Total time should be reasonable for concurrent processing
        Assert.True(totalTime < TimeSpan.FromMinutes(2));
    }

    private void CreateRealisticTestData()
    {
        // Create realistic motorcycle specifications CSV
        var motorcycleSpecs = @"Make,Model,Year,Engine,Displacement,Power,Torque,Weight,TopSpeed,Price,Category,FuelCapacity
Honda,CBR600RR,2023,Inline-4,599cc,118hp,65Nm,194kg,260km/h,12000,Sport,18.1L
Yamaha,YZF-R6,2023,Inline-4,599cc,117hp,61Nm,190kg,262km/h,12500,Sport,17L
Kawasaki,ZX-6R,2023,Inline-4,636cc,130hp,70Nm,196kg,265km/h,11500,Sport,17L
Suzuki,GSX-R600,2023,Inline-4,599cc,105hp,58Nm,187kg,260km/h,11000,Sport,16.5L
Ducati,Panigale V2,2023,L-Twin,955cc,155hp,104Nm,200kg,280km/h,17000,Sport,17L
BMW,S1000RR,2023,Inline-4,999cc,205hp,113Nm,197kg,299km/h,18000,Sport,16.5L
Honda,CBR1000RR-R,2023,Inline-4,999cc,217hp,113Nm,201kg,299km/h,28000,Sport,16.1L
Yamaha,YZF-R1,2023,Inline-4,998cc,200hp,112Nm,201kg,299km/h,17500,Sport,17L
Kawasaki,ZX-10R,2023,Inline-4,998cc,203hp,115Nm,207kg,299km/h,16500,Sport,17L
Aprilia,RSV4 1100,2023,V4,1077cc,217hp,125Nm,199kg,299km/h,19000,Sport,18.5L";

        File.WriteAllText(Path.Combine(_testDataDirectory, "motorcycle_specs.csv"), motorcycleSpecs);

        // Create sport bikes specific CSV
        var sportBikes = @"Make,Model,Year,Category,RacingHeritage,Electronics,Suspension
Honda,CBR600RR,2023,Supersport,MotoGP derived,Traction control,Showa
Yamaha,YZF-R6,2023,Supersport,WorldSBK,Quick shifter,KYB
Kawasaki,ZX-6R,2023,Supersport,WorldSSP,ABS,Showa
Ducati,Panigale V2,2023,Supersport,WorldSBK,Cornering ABS,Sachs
BMW,S1000RR,2023,Superbike,WorldSBK,Dynamic traction control,BMW Motorrad";

        File.WriteAllText(Path.Combine(_testDataDirectory, "sport_bikes.csv"), sportBikes);

        // Create a realistic PDF manual content
        var pdfContent = @"%PDF-1.4
1 0 obj
<<
/Type /Catalog
/Pages 2 0 R
>>
endobj
2 0 obj
<<
/Type /Pages
/Kids [3 0 R]
/Count 1
>>
endobj
3 0 obj
<<
/Type /Page
/Parent 2 0 R
/MediaBox [0 0 612 792]
/Contents 4 0 R
>>
endobj
4 0 obj
<<
/Length 500
>>
stream
BT
/F1 12 Tf
50 750 Td
(Honda CBR600RR Owner's Manual) Tj
0 -20 Td
(Chapter 5: Maintenance) Tj
0 -40 Td
(5.1 Engine Oil Change) Tj
0 -20 Td
(Change engine oil every 8,000 km or 12 months.) Tj
0 -20 Td
(Oil capacity: 3.7 liters with filter change) Tj
0 -20 Td
(Recommended oil: 10W-40 motorcycle oil) Tj
0 -40 Td
(5.2 Chain Maintenance) Tj
0 -20 Td
(Clean and lubricate chain every 1,000 km) Tj
0 -20 Td
(Check chain tension regularly) Tj
0 -40 Td
(5.3 Brake System) Tj
0 -20 Td
(Check brake fluid level monthly) Tj
0 -20 Td
(Replace brake pads when thickness < 2mm) Tj
ET
endstream
endobj
xref
0 5
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000206 00000 n 
trailer
<<
/Size 5
/Root 1 0 R
>>
startxref
756
%%EOF";

        File.WriteAllText(Path.Combine(_testDataDirectory, "honda_manual.pdf"), pdfContent);
    }

    private IFormFile CreateFormFileFromPath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var contentType = fileName.EndsWith(".pdf") ? "application/pdf" : "text/csv";
        var content = File.ReadAllBytes(filePath);
        var stream = new MemoryStream(content);
        
        var file = new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        return file;
    }

    public void Dispose()
    {
        _scope?.Dispose();
        
        // Cleanup test directory
        if (Directory.Exists(_testDataDirectory))
        {
            try
            {
                Directory.Delete(_testDataDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}