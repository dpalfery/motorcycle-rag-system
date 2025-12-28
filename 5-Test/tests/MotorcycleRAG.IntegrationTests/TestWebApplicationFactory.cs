using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Persistence.Sql;
using System.Data;
using System.Data.Common;
using MotorcycleRAG.Domain.Models;
using MotorcycleRAG.Contracts.Options;

namespace MotorcycleRAG.IntegrationTests;

/// <summary>
/// Test web application factory for integration tests
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Add test authentication scheme
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override configuration for testing
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAI:FoundryEndpoint"] = "https://test-foundry.cognitiveservices.azure.com/",
                ["AzureAI:OpenAIEndpoint"] = "https://test-openai.openai.azure.com/",
                ["AzureAI:SearchServiceEndpoint"] = "https://test-search.search.windows.net/",
                ["AzureAI:DocumentIntelligenceEndpoint"] = "https://test-document.cognitiveservices.azure.com/",
                ["AzureAI:Models:ChatModel"] = "gpt-4o-mini",
                ["AzureAI:Models:EmbeddingModel"] = "text-embedding-3-large",
                ["AzureAI:Models:QueryPlannerModel"] = "gpt-4o",
                ["AzureAI:Models:VisionModel"] = "gpt-4-vision-preview",
                ["AzureAI:Models:MaxTokens"] = "4096",
                ["AzureAI:Models:Temperature"] = "0.1",
                ["Sql:Server"] = "test-server",
                ["Sql:Database"] = "test-database",
                ["Sql:Username"] = "test-user",
                ["Sql:Password"] = "test-password",
                ["Sql:ConnectionTimeout"] = "30",
                ["Sql:CommandTimeout"] = "60",
                ["Sql:MaxPoolSize"] = "100",
                ["Sql:UseIntegratedSecurity"] = "false",
                ["Search:IndexName"] = "test-motorcycle-index",
                ["Search:BatchSize"] = "100",
                ["Search:MaxSearchResults"] = "50",
                ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=test-key",
                ["ApplicationInsights:EnableTelemetry"] = "false",
                ["ApplicationInsights:ApplicationName"] = "MotorcycleRAG-Test",
                ["Pipeline:MaxConcurrentExecutions"] = "3",
                ["Pipeline:DefaultTimeout"] = "00:30:00",
                ["Pipeline:MaxRetries"] = "3",
                ["Pipeline:TempDirectory"] = "temp-test",
                ["FileUpload:BaseUploadDirectory"] = "uploads-test",
                ["FileUpload:MaxFileSizeBytes"] = "52428800",
                ["FileUpload:MaxFilesPerBatch"] = "10",
                ["PipelineMonitoring:AlertsEnabled"] = "false",
                ["PipelineMonitoring:FailureRateThreshold"] = "0.10",
                ["PipelineMonitoring:MaxActiveExecutions"] = "10",
                ["ScheduledProcessing:DefaultCronExpression"] = "0 0 2 * * *",
                ["ScheduledProcessing:IsEnabledByDefault"] = "true",
                ["ScheduledProcessing:BaseDirectory"] = "data-test"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace Azure services with mocks for testing
            ReplaceWithMocks(services);

            // Override authentication to use test handler
            services.PostConfigureAll<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultForbidScheme = "Test";
                options.DefaultSignInScheme = "Test";
                options.DefaultSignOutScheme = "Test";
            });
        });

        builder.UseEnvironment("Testing");
    }

    private void ReplaceWithMocks(IServiceCollection services)
    {
        // Remove existing Azure service registrations and SQL repositories
        var servicesToRemove = services
            .Where(s => s.ServiceType.Namespace?.StartsWith("Azure") == true ||
                       s.ServiceType == typeof(IAzureOpenAIClient) ||
                       s.ServiceType == typeof(IAzureSearchClient) ||
                       s.ServiceType == typeof(IDocumentIntelligenceClient) ||
                       s.ServiceType == typeof(IMotorcycleIndexingService) ||
                       s.ServiceType == typeof(ISqlConnectionFactory) ||
                       s.ServiceType == typeof(IIngestionJobRepository))
            .ToList();

        foreach (var service in servicesToRemove)
        {
            services.Remove(service);
        }

        // Add mock implementations
        var mockAzureOpenAI = new Mock<IAzureOpenAIClient>();
        mockAzureOpenAI.Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var mockAzureSearch = new Mock<IAzureSearchClient>();
        mockAzureSearch.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchParameters>()))
            .ReturnsAsync(Array.Empty<SearchResult>());

        var mockDocumentIntelligence = new Mock<IDocumentIntelligenceClient>();
        mockDocumentIntelligence.Setup(x => x.AnalyzeDocumentAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentAnalysisResult
            {
                Content = "Mock extracted content",
                Pages = new[]
                {
                    new DocumentPage
                    {
                        PageNumber = 1,
                        Content = "Mock page content"
                    }
                }
            });

        var mockIndexingService = new Mock<IMotorcycleIndexingService>();
        mockIndexingService.Setup(x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .ReturnsAsync(new BatchIndexingResult
            {
                Success = true,
                DocumentsProcessed = 1,
                IndexName = "test-index",
                Message = "Mock indexing successful"
            });

        services.AddSingleton(mockAzureOpenAI.Object);
        services.AddSingleton(mockAzureSearch.Object);
        services.AddSingleton(mockDocumentIntelligence.Object);
        services.AddSingleton(mockIndexingService.Object);

        // Mock SQL connection factory for testing
        var mockSqlConnectionFactory = new Mock<ISqlConnectionFactory>();
        mockSqlConnectionFactory.Setup(x => x.CreateConnectionAsync())
            .ReturnsAsync((IDbConnection)null!);
        mockSqlConnectionFactory.Setup(x => x.CreateConnection())
            .Returns((IDbConnection)null!);
        mockSqlConnectionFactory.Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync((IDbConnection)null!);
        mockSqlConnectionFactory.Setup(x => x.CreateCommand(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction?>(), It.IsAny<string?>(), It.IsAny<CommandType>(), It.IsAny<int?>()))
            .Returns((IDbCommand)null!);
        mockSqlConnectionFactory.Setup(x => x.CreateParameter(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<DbType>(), It.IsAny<ParameterDirection>()))
            .Returns((IDataParameter)null!);
        services.AddSingleton(mockSqlConnectionFactory.Object);

        // Mock IIngestionJobRepository for testing
        var mockIngestionJobRepository = new Mock<IIngestionJobRepository>();
        mockIngestionJobRepository.Setup(x => x.CreateAsync(It.IsAny<IngestionJob>()))
            .ReturnsAsync((IngestionJob job) =>
            {
                job.Id = 1;
                return job;
            });
        mockIngestionJobRepository.Setup(x => x.GetByIdAsync(It.IsAny<long>()))
            .ReturnsAsync((long id) => new IngestionJob
            {
                Id = id,
                JobId = Guid.NewGuid().ToString(),
                Status = IngestionJobStatus.Completed,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(5),
                SourceFileName = "test-file.csv",
                SourceFilePath = "/test/path/test-file.csv"
            });
        mockIngestionJobRepository.Setup(x => x.GetByJobIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string jobId) => new IngestionJob
            {
                Id = 1,
                JobId = jobId,
                Status = IngestionJobStatus.Completed,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(5),
                SourceFileName = "test-file.csv",
                SourceFilePath = "/test/path/test-file.csv"
            });
        mockIngestionJobRepository.Setup(x => x.UpdateAsync(It.IsAny<IngestionJob>()))
            .ReturnsAsync((IngestionJob job) => job);
        mockIngestionJobRepository.Setup(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<IngestionJobStatus>(), It.IsAny<DateTime?>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        mockIngestionJobRepository.Setup(x => x.UpdateMetricsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(true);
        mockIngestionJobRepository.Setup(x => x.AddErrorAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        mockIngestionJobRepository.Setup(x => x.GetByStatusAsync(It.IsAny<IngestionJobStatus>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<IngestionJob>());
        mockIngestionJobRepository.Setup(x => x.GetByJobTypeAsync(It.IsAny<IngestionJobType>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<IngestionJob>());
        mockIngestionJobRepository.Setup(x => x.GetRecentJobsAsync(It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<IngestionJob>());
        mockIngestionJobRepository.Setup(x => x.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<IngestionJob>());
        mockIngestionJobRepository.Setup(x => x.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(Array.Empty<IngestionJob>());
        mockIngestionJobRepository.Setup(x => x.DeleteAsync(It.IsAny<long>()))
            .ReturnsAsync(true);
        services.AddSingleton(mockIngestionJobRepository.Object);

        // Mock resilience and correlation services
        var mockResilienceService = new Mock<IResilienceService>();

        mockResilienceService.Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<ProcessedData>>>(),
                It.IsAny<Func<Task<ProcessedData>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<ProcessedData>>, Func<Task<ProcessedData>>, string, CancellationToken>((key, func, fallback, corrId, token) => func());

        mockResilienceService.Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<BatchIndexingResult>>>(),
                It.IsAny<Func<Task<BatchIndexingResult>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<BatchIndexingResult>>, Func<Task<BatchIndexingResult>>, string, CancellationToken>((key, func, fallback, corrId, token) => func());

        var mockCorrelationService = new Mock<ICorrelationService>();
        mockCorrelationService.Setup(x => x.GetOrGenerateCorrelationId())
            .Returns("test-correlation-id");
        mockCorrelationService.Setup(x => x.StartActivity(It.IsAny<string>()))
            .Returns((IDisposable)null!);

        services.AddSingleton(mockResilienceService.Object);
        services.AddSingleton(mockCorrelationService.Object);

        // Ensure logging is configured for tests
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
    }
}
