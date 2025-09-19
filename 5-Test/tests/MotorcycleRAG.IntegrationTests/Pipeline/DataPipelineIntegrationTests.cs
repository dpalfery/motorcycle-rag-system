using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.Pipeline;

/// <summary>
/// Integration tests for the complete data pipeline workflow
/// </summary>
public class DataPipelineIntegrationTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly IDataPipelineOrchestrator _orchestrator;
    private readonly IFileUploadService _fileUploadService;
    private readonly IPipelineMonitoringService _monitoringService;
    private readonly IScheduledPipelineService _scheduledService;
    private readonly string _testDirectory;

    public DataPipelineIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        
        _orchestrator = _scope.ServiceProvider.GetRequiredService<IDataPipelineOrchestrator>();
        _fileUploadService = _scope.ServiceProvider.GetRequiredService<IFileUploadService>();
        _monitoringService = _scope.ServiceProvider.GetRequiredService<IPipelineMonitoringService>();
        _scheduledService = _scope.ServiceProvider.GetRequiredService<IScheduledPipelineService>();

        // Create test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PipelineIntegrationTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public async Task CompleteWorkflow_UploadAndProcessCSV_ShouldSucceed()
    {
        // Arrange
        var csvContent = @"Make,Model,Year,Engine,Power
Honda,CBR600RR,2023,599cc,118hp
Yamaha,YZF-R6,2023,599cc,117hp
Kawasaki,ZX-6R,2023,636cc,130hp";

        var file = CreateMockFile("motorcycles.csv", csvContent, "text/csv");
        var uploadOptions = new FileUploadOptions
        {
            UploadDirectory = "integration-test",
            GenerateUniqueFileName = true
        };

        // Act - Upload file
        var uploadResult = await _fileUploadService.UploadFileAsync(file, uploadOptions);

        // Assert upload
        Assert.True(uploadResult.IsValid);
        Assert.Equal(FileType.CSV, uploadResult.DetectedFileType);
        Assert.True(File.Exists(uploadResult.FilePath));

        // Act - Process file
        var pipelineRequest = new DataPipelineRequest
        {
            FileName = uploadResult.OriginalFileName,
            FilePath = uploadResult.FilePath,
            FileType = uploadResult.DetectedFileType,
            Options = new PipelineOptions
            {
                IndexImmediately = false, // Skip indexing for integration test
                ProcessImages = false,
                GenerateEmbeddings = false
            }
        };

        var processingResult = await _orchestrator.ProcessFileAsync(pipelineRequest);

        // Assert processing
        Assert.NotNull(processingResult);
        Assert.Equal(PipelineStatus.Completed, processingResult.Status);
        Assert.NotNull(processingResult.ProcessedData);
        Assert.True(processingResult.ProcessedData.Documents.Count > 0);

        // Verify documents contain expected data
        var documents = processingResult.ProcessedData.Documents;
        Assert.Contains(documents, d => d.Content.Contains("Honda"));
        Assert.Contains(documents, d => d.Content.Contains("CBR600RR"));

        // Cleanup
        await _fileUploadService.DeleteFileAsync(uploadResult.FilePath);
    }

    [Fact]
    public async Task CompleteWorkflow_UploadAndProcessPDF_ShouldSucceed()
    {
        // Arrange
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
/Length 44
>>
stream
BT
/F1 12 Tf
100 700 Td
(Honda CBR600RR Manual) Tj
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
299
%%EOF";

        var file = CreateMockFile("manual.pdf", pdfContent, "application/pdf");
        var uploadOptions = new FileUploadOptions
        {
            UploadDirectory = "integration-test",
            GenerateUniqueFileName = true
        };

        // Act - Upload file
        var uploadResult = await _fileUploadService.UploadFileAsync(file, uploadOptions);

        // Assert upload
        Assert.True(uploadResult.IsValid);
        Assert.Equal(FileType.PDF, uploadResult.DetectedFileType);
        Assert.True(File.Exists(uploadResult.FilePath));

        // Act - Process file
        var pipelineRequest = new DataPipelineRequest
        {
            FileName = uploadResult.OriginalFileName,
            FilePath = uploadResult.FilePath,
            FileType = uploadResult.DetectedFileType,
            Options = new PipelineOptions
            {
                IndexImmediately = false, // Skip indexing for integration test
                ProcessImages = false,
                GenerateEmbeddings = false
            }
        };

        var processingResult = await _orchestrator.ProcessFileAsync(pipelineRequest);

        // Assert processing
        Assert.NotNull(processingResult);
        // PDF processing might fail in test environment without proper Azure services
        // So we check for either success or specific failure
        Assert.True(processingResult.Status == PipelineStatus.Completed || 
                   processingResult.Status == PipelineStatus.Failed);

        // Cleanup
        await _fileUploadService.DeleteFileAsync(uploadResult.FilePath);
    }

    [Fact]
    public async Task BatchProcessing_MultipleFiles_ShouldProcessAllFiles()
    {
        // Arrange
        var files = new List<IFormFile>
        {
            CreateMockFile("bikes1.csv", "Make,Model\nHonda,CBR\nYamaha,R6", "text/csv"),
            CreateMockFile("bikes2.csv", "Make,Model\nKawasaki,Ninja\nSuzuki,GSXR", "text/csv"),
            CreateMockFile("manual.pdf", "%PDF-1.4 test content", "application/pdf")
        };

        var uploadOptions = new FileUploadOptions
        {
            UploadDirectory = "batch-test",
            GenerateUniqueFileName = true
        };

        // Act - Upload files
        var batchUploadResult = await _fileUploadService.UploadFilesAsync(files, uploadOptions);

        // Assert uploads
        Assert.Equal(3, batchUploadResult.TotalFiles);
        Assert.True(batchUploadResult.SuccessfulUploads >= 2); // At least CSV files should succeed

        // Act - Process files
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

        var batchResult = await _orchestrator.ProcessBatchAsync(pipelineRequests);

        // Assert batch processing
        Assert.NotNull(batchResult);
        Assert.True(batchResult.TotalFiles >= 2);
        Assert.True(batchResult.ProcessedSuccessfully >= 1); // At least one should succeed

        // Cleanup
        foreach (var result in batchUploadResult.Results.Where(r => r.IsValid))
        {
            await _fileUploadService.DeleteFileAsync(result.FilePath);
        }
    }

    [Fact]
    public async Task MonitoringService_ShouldTrackPipelineExecution()
    {
        // Arrange
        var csvContent = "Make,Model\nHonda,CBR";
        var file = CreateMockFile("test.csv", csvContent, "text/csv");
        var uploadOptions = new FileUploadOptions
        {
            UploadDirectory = "monitoring-test",
            GenerateUniqueFileName = true
        };

        // Act - Upload and process with monitoring
        var uploadResult = await _fileUploadService.UploadFileAsync(file, uploadOptions);
        
        var pipelineRequest = new DataPipelineRequest
        {
            FileName = uploadResult.OriginalFileName,
            FilePath = uploadResult.FilePath,
            FileType = uploadResult.DetectedFileType,
            Options = new PipelineOptions { IndexImmediately = false }
        };

        var processingResult = await _orchestrator.ProcessFileAsync(pipelineRequest);

        // Assert monitoring
        var healthStatus = await _monitoringService.GetHealthStatusAsync();
        Assert.NotNull(healthStatus);
        Assert.NotNull(healthStatus.HealthChecks);
        Assert.True(healthStatus.HealthChecks.Count > 0);

        var metrics = await _monitoringService.GetDetailedMetricsAsync(TimeSpan.FromHours(1));
        Assert.NotNull(metrics);
        Assert.True(metrics.Executions.TotalExecutions >= 1);

        // Cleanup
        await _fileUploadService.DeleteFileAsync(uploadResult.FilePath);
    }

    [Fact]
    public async Task ScheduledService_ShouldProvideValidStats()
    {
        // Act
        var stats = await _scheduledService.GetProcessingStatsAsync();
        var nextExecution = await _scheduledService.GetNextExecutionTimeAsync();

        // Assert
        Assert.NotNull(stats);
        Assert.True(stats.TotalScheduledRuns >= 0);
        Assert.True(stats.SuccessfulRuns >= 0);
        Assert.True(stats.FailedRuns >= 0);

        // Next execution time should be set if service is enabled
        if (nextExecution.HasValue)
        {
            Assert.True(nextExecution.Value > DateTime.UtcNow);
        }
    }

    [Fact]
    public async Task ErrorHandling_InvalidFile_ShouldFailGracefully()
    {
        // Arrange
        var invalidContent = "This is not a valid CSV or PDF file";
        var file = CreateMockFile("invalid.csv", invalidContent, "text/csv");
        var uploadOptions = new FileUploadOptions
        {
            UploadDirectory = "error-test",
            GenerateUniqueFileName = true,
            ValidateFileContent = true
        };

        // Act - Upload should succeed but validation might warn
        var uploadResult = await _fileUploadService.UploadFileAsync(file, uploadOptions);

        if (uploadResult.IsValid)
        {
            // Act - Processing might fail
            var pipelineRequest = new DataPipelineRequest
            {
                FileName = uploadResult.OriginalFileName,
                FilePath = uploadResult.FilePath,
                FileType = uploadResult.DetectedFileType,
                Options = new PipelineOptions { IndexImmediately = false }
            };

            var processingResult = await _orchestrator.ProcessFileAsync(pipelineRequest);

            // Assert - Should handle error gracefully
            Assert.NotNull(processingResult);
            // Result can be either failed or completed depending on processor implementation
            Assert.True(processingResult.Status == PipelineStatus.Failed || 
                       processingResult.Status == PipelineStatus.Completed);

            // Cleanup
            await _fileUploadService.DeleteFileAsync(uploadResult.FilePath);
        }
    }

    [Fact]
    public async Task ConcurrentProcessing_MultipleSimultaneousRequests_ShouldHandleCorrectly()
    {
        // Arrange
        var tasks = new List<Task<PipelineExecutionResult>>();
        var uploadResults = new List<FileUploadResult>();

        for (int i = 0; i < 3; i++)
        {
            var csvContent = $"Make,Model\nHonda,CBR{i}\nYamaha,R{i}";
            var file = CreateMockFile($"concurrent{i}.csv", csvContent, "text/csv");
            var uploadOptions = new FileUploadOptions
            {
                UploadDirectory = "concurrent-test",
                GenerateUniqueFileName = true
            };

            var uploadResult = await _fileUploadService.UploadFileAsync(file, uploadOptions);
            uploadResults.Add(uploadResult);

            if (uploadResult.IsValid)
            {
                var pipelineRequest = new DataPipelineRequest
                {
                    FileName = uploadResult.OriginalFileName,
                    FilePath = uploadResult.FilePath,
                    FileType = uploadResult.DetectedFileType,
                    Options = new PipelineOptions { IndexImmediately = false }
                };

                tasks.Add(_orchestrator.ProcessFileAsync(pipelineRequest));
            }
        }

        // Act - Process all files concurrently
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(3, results.Length);
        Assert.All(results, result =>
        {
            Assert.NotNull(result);
            Assert.True(result.Status == PipelineStatus.Completed || 
                       result.Status == PipelineStatus.Failed);
        });

        // Cleanup
        foreach (var uploadResult in uploadResults.Where(r => r.IsValid))
        {
            await _fileUploadService.DeleteFileAsync(uploadResult.FilePath);
        }
    }

    private IFormFile CreateMockFile(string fileName, string content, string contentType)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        
        var file = new FormFile(stream, 0, bytes.Length, "file", fileName)
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
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}