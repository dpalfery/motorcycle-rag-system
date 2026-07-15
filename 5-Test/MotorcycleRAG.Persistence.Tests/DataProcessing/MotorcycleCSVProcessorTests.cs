using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Domain.ValueObjects;
using MotorcycleRAG.Persistence.DataProcessing;
using Microsoft.Extensions.Options;

using MotorcycleRAG.Contracts.Models.DTOs.Search;
namespace MotorcycleRAG.Persistence.Tests.DataProcessing;

public class MotorcycleCSVProcessorTests
{
    private static readonly float[] DefaultEmbedding = { 0.1f, 0.2f, 0.3f };

    private static Mock<IAzureFoundryClient> CreateFoundryMock()
    {
        var mock = new Mock<IAzureFoundryClient>();
        mock.Setup(c => c.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultEmbedding);
        return mock;
    }

    private static Mock<IAzureSearchClient> CreateSearchMock()
    {
        var mock = new Mock<IAzureSearchClient>();
        mock.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocumentDto>>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static CSVProcessingConfiguration CreateTestConfig()
    {
        return new CSVProcessingConfiguration
        {
            MaxRows = 1000,
            ChunkSize = 50,
            Delimiter = ',',
            HasHeader = true,
            PreserveRelationalIntegrity = true
        };
    }

    private static MotorcycleCsvProcessor CreateSut(
        Mock<IAzureFoundryClient>? foundryMock = null,
        Mock<IAzureSearchClient>? searchMock = null)
    {
        foundryMock ??= CreateFoundryMock();
        searchMock ??= CreateSearchMock();
        return new MotorcycleCsvProcessor(
            foundryMock.Object,
            searchMock.Object,
            TestHelpers.CreateNullLogger<MotorcycleCsvProcessor>(),
            Options.Create(CreateTestConfig()));
    }

    private static CSVFile CreateValidCsvFile(string csvContent = "Make,Model,Year\nHonda,CBR,2024\n")
    {
        var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(csvContent);
            writer.Flush();
        }
        stream.Position = 0;
        return new CSVFile
        {
            FileName = "test.csv",
            Content = stream,
            HasHeaders = true,
            Delimiter = ",",
            Encoding = "UTF-8",
            Category = MotorcycleRAG.Domain.ValueObjects.MotorcycleCategory.Sport
        };
    }

    // ---- Constructor tests ----

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenFoundryClientIsNull()
    {
        var act = () => new MotorcycleCsvProcessor(
            null!, CreateSearchMock().Object,
            TestHelpers.CreateNullLogger<MotorcycleCsvProcessor>(),
            Options.Create(CreateTestConfig()));
        act.Should().Throw<ArgumentNullException>().WithParameterName("openAIClient");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenSearchClientIsNull()
    {
        var act = () => new MotorcycleCsvProcessor(
            CreateFoundryMock().Object, null!,
            TestHelpers.CreateNullLogger<MotorcycleCsvProcessor>(),
            Options.Create(CreateTestConfig()));
        act.Should().Throw<ArgumentNullException>().WithParameterName("searchClient");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new MotorcycleCsvProcessor(
            CreateFoundryMock().Object, CreateSearchMock().Object, null!, Options.Create(CreateTestConfig()));
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldSucceed_WithValidDependencies()
    {
        var act = () => CreateSut();
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConfigurationIsNull()
    {
        var act = () => new MotorcycleCsvProcessor(
            CreateFoundryMock().Object,
            CreateSearchMock().Object,
            TestHelpers.CreateNullLogger<MotorcycleCsvProcessor>(),
            null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    // ---- ProcessAsync tests ----

    [Fact]
    public async Task ProcessAsync_ShouldThrowArgumentNullException_WhenInputIsNull()
    {
        var sut = CreateSut();
        var act = () => sut.ProcessAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("input");
    }

    [Fact]
    public async Task ProcessAsync_ShouldReturnProcessedData_WithValidCsv()
    {
        var sut = CreateSut();
        var input = CreateValidCsvFile();
        var result = await sut.ProcessAsync(input);
        result.Should().NotBeNull();
        result.Documents.Should().NotBeEmpty();
        result.Metadata.Should().ContainKey("SourceFile");
        result.Metadata["SourceFile"].Should().Be("test.csv");
    }

    [Fact]
    public async Task ProcessAsync_ShouldThrow_WhenContentIsNull()
    {
        var sut = CreateSut();
        var input = new CSVFile { FileName = "test.csv", Content = null };
        var act = () => sut.ProcessAsync(input);
        await act.Should().ThrowAsync<InvalidOperationException>().Where(ex => ex.Message.Contains("File content is required"));
    }

    [Fact]
    public async Task ProcessAsync_ShouldThrow_WhenContentIsStreamNull()
    {
        var sut = CreateSut();
        var input = new CSVFile { FileName = "test.csv", Content = Stream.Null };
        var act = () => sut.ProcessAsync(input);
        await act.Should().ThrowAsync<InvalidOperationException>().Where(ex => ex.Message.Contains("File content is required"));
    }

    [Fact]
    public async Task ProcessAsync_ShouldThrow_WhenFileNameIsEmpty()
    {
        var sut = CreateSut();
        var input = new CSVFile { FileName = "", Content = new MemoryStream("a,b\n1,2"u8.ToArray()) };
        var act = () => sut.ProcessAsync(input);
        await act.Should().ThrowAsync<InvalidOperationException>().Where(ex => ex.Message.Contains("File name is required"));
    }

    [Fact]
    public async Task ProcessAsync_ShouldProcessHeaderlessCsv()
    {
        var sut = CreateSut();
        var input = CreateValidCsvFile("Honda,CBR600,2024\nKawasaki,Ninja,2024\n");
        input.HasHeaders = false;
        var result = await sut.ProcessAsync(input);
        result.Should().NotBeNull();
        result.Documents.Should().NotBeEmpty();
    }

    // ---- IndexAsync tests ----

    [Fact]
    public async Task IndexAsync_ShouldThrowArgumentNullException_WhenDataIsNull()
    {
        var sut = CreateSut();
        var act = () => sut.IndexAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("data");
    }

    [Fact]
    public async Task IndexAsync_ShouldReturnSuccessfulResult_WithValidDocuments()
    {
        var sut = CreateSut();
        var data = new ProcessedData { Id = "test-id" };
        data.Documents.Add(new MotorcycleDocumentDto
        {
            Id = "doc-1",
            Title = "Test",
            Content = "Test content",
            Type = DocumentType.Specification
        });
        var result = await sut.IndexAsync(data);
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.DocumentsIndexed.Should().Be(1);
    }

    [Fact]
    public async Task IndexAsync_ShouldReturnFailure_WhenNoDocumentsIndexed()
    {
        var sut = CreateSut();
        var data = new ProcessedData { Id = "test-id" };
        var result = await sut.IndexAsync(data);
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.DocumentsIndexed.Should().Be(0);
    }

    [Fact]
    public void Implements_IDataProcessorOfCSVFile()
    {
        var sut = CreateSut();
        sut.Should().BeAssignableTo<IDataProcessor<CSVFile>>();
    }

    // ---- Additional edge-case tests ----

    [Fact]
    public async Task ProcessAsync_ShouldThrow_WhenContentIsEmptyStream()
    {
        var sut = CreateSut();
        var input = new CSVFile { FileName = "test.csv", Content = new MemoryStream() };
        var act = () => sut.ProcessAsync(input);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        // The message is wrapped by the outer catch but should contain the validation error
        ex.Which.Message.Should().Contain("File content cannot be empty");
    }

    [Fact]
    public async Task ProcessAsync_ShouldThrow_WhenAllChunksFailProcessing()
    {
        var foundryMock = new Mock<IAzureFoundryClient>();
        foundryMock.Setup(c => c.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Embedding failed"));
        var sut = CreateSut(foundryMock: foundryMock);
        var input = CreateValidCsvFile();
        var act = () => sut.ProcessAsync(input);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("No documents processed"));
    }

    [Fact]
    public async Task ProcessAsync_ShouldProcessWithNullCategory()
    {
        var sut = CreateSut();
        var input = CreateValidCsvFile();
        input.Category = null;
        var result = await sut.ProcessAsync(input);
        result.Should().NotBeNull();
        result.Documents.Should().NotBeEmpty();
        result.Metadata["Category"].Should().Be(string.Empty);
    }

    [Fact]
    public async Task ProcessAsync_ShouldChunkBySize_WhenNotPreservingIntegrity()
    {
        // Config with small chunk size and relational integrity disabled
        var config = new CSVProcessingConfiguration
        {
            MaxRows = 1000,
            ChunkSize = 1,
            Delimiter = ',',
            HasHeader = true,
            PreserveRelationalIntegrity = false
        };
        var sut = new MotorcycleCsvProcessor(
            CreateFoundryMock().Object,
            CreateSearchMock().Object,
            TestHelpers.CreateNullLogger<MotorcycleCsvProcessor>(),
            Options.Create(config));

        var csvContent = "Make,Model,Year\nHonda,CBR,2024\nKawasaki,Ninja,2024\n";
        var input = CreateValidCsvFile(csvContent);

        var result = await sut.ProcessAsync(input);
        result.Should().NotBeNull();
        result.Documents.Should().HaveCount(2);
        result.Metadata["ChunksCreated"].Should().Be(2);
    }

    [Fact]
    public async Task ProcessAsync_ShouldHandleHeaderlessCsv_WithColumnGeneration()
    {
        var sut = CreateSut();
        var csvContent = "Honda,CBR600,2024\nYamaha,R1,2024\n";
        var input = CreateValidCsvFile(csvContent);
        input.HasHeaders = false;

        var result = await sut.ProcessAsync(input);
        result.Should().NotBeNull();
        result.Documents.Should().NotBeEmpty();
        // Column headers should have been auto-generated as Column1, Column2, etc.
        var chunksCreated = result.Metadata["ChunksCreated"];
        Convert.ToInt32(chunksCreated).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IndexAsync_ShouldCollectBatchErrors_WhenSearchClientThrows()
    {
        var searchMock = new Mock<IAzureSearchClient>();
        searchMock.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocumentDto>>()))
            .ThrowsAsync(new InvalidOperationException("Search index error"));
        var sut = CreateSut(searchMock: searchMock);

        var data = new ProcessedData { Id = "test-id" };
        data.Documents.Add(new MotorcycleDocumentDto
        {
            Id = "doc-1",
            Title = "Test",
            Content = "Test content",
            Type = DocumentType.Specification
        });

        var result = await sut.IndexAsync(data);
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.DocumentsIndexed.Should().Be(0);
        result.Errors.Should().Contain(e => e.Contains("Batch indexing error"));
    }

    [Fact]
    public async Task IndexAsync_ShouldSetIndexingTime_OnSuccess()
    {
        var sut = CreateSut();
        var data = new ProcessedData { Id = "test-id" };
        data.Documents.Add(new MotorcycleDocumentDto
        {
            Id = "doc-1",
            Title = "Test",
            Content = "Test content",
            Type = DocumentType.Specification
        });

        var result = await sut.IndexAsync(data);
        result.IndexingTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task IndexAsync_ShouldSetIndexingTime_OnFailure()
    {
        var searchMock = new Mock<IAzureSearchClient>();
        searchMock.Setup(c => c.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocumentDto>>()))
            .ThrowsAsync(new InvalidOperationException("Search down"));
        var sut = CreateSut(searchMock: searchMock);

        var data = new ProcessedData { Id = "test-id" };
        data.Documents.Add(new MotorcycleDocumentDto { Id = "d", Title = "t", Content = "c", Type = DocumentType.Specification });

        var result = await sut.IndexAsync(data);
        result.IndexingTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPropagateMultipleValidationErrors()
    {
        var sut = CreateSut();
        var input = new CSVFile { FileName = "", Content = null };
        var act = () => sut.ProcessAsync(input);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        // Should contain both "File name is required" and "File content is required"
        ex.Which.Message.Should().Contain("File name is required");
        ex.Which.Message.Should().Contain("File content is required");
    }

    [Fact]
    public async Task ProcessAsync_ShouldPopulateMetadata_WithProcessingTime()
    {
        var sut = CreateSut();
        var input = CreateValidCsvFile();
        var result = await sut.ProcessAsync(input);
        result.Metadata.Should().ContainKey("ProcessingTime");
        var processingTime = result.Metadata["ProcessingTime"];
        processingTime.Should().BeOfType<TimeSpan>();
        ((TimeSpan)processingTime).Should().BeGreaterThan(TimeSpan.Zero);
    }

    // ---- Additional coverage tests ----

    [Fact]
    public async Task ProcessAsync_ShouldGroupSameMotorcycleIntoSingleChunk_WhenPreservingIntegrity()
    {
        // Same make/model/year for all rows, so relational integrity keeps them in one chunk
        var config = new CSVProcessingConfiguration
        {
            MaxRows = 1000,
            ChunkSize = 50,
            Delimiter = ',',
            HasHeader = true,
            PreserveRelationalIntegrity = true
        };
        config.IdentifierFields.Add("Make");
        config.IdentifierFields.Add("Model");

        var sut = new MotorcycleCsvProcessor(
            CreateFoundryMock().Object,
            CreateSearchMock().Object,
            TestHelpers.CreateNullLogger<MotorcycleCsvProcessor>(),
            Options.Create(config));

        var csvContent = "Make,Model,Year\nHonda,CBR,2024\nHonda,CBR,2023\nHonda,CBR,2022\n";
        var input = CreateValidCsvFile(csvContent);
        input.Category = MotorcycleRAG.Domain.ValueObjects.MotorcycleCategory.Sport;

        var result = await sut.ProcessAsync(input);
        result.Should().NotBeNull();
        result.Documents.Should().NotBeEmpty();
        // All rows should be in one chunk since they share same make/model
        result.Metadata["ChunksCreated"].Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_ShouldThrow_WhenHeadersExceedMaxColumns()
    {
        var config = new CSVProcessingConfiguration
        {
            MaxRows = 1000,
            ChunkSize = 50,
            Delimiter = ',',
            HasHeader = true,
            PreserveRelationalIntegrity = true
        };
        var sut = new MotorcycleCsvProcessor(
            CreateFoundryMock().Object,
            CreateSearchMock().Object,
            TestHelpers.CreateNullLogger<MotorcycleCsvProcessor>(),
            Options.Create(config));

        // CSV has 3 columns but max is set to 2
        var csvContent = "Make,Model,Year\nHonda,CBR,2024\n";
        var input = CreateValidCsvFile(csvContent);
        input.MaxColumns = 2; // very strict limit

        var act = () => sut.ProcessAsync(input);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("maximum allowed"));
    }

    [Fact]
    public async Task ProcessAsync_ShouldHandleHeaderlessCsv_WithTwoRows()
    {
        var sut = CreateSut();
        // Need at least 2 rows: first row is consumed to determine column count, remaining rows are data
        var csvContent = "Honda,CBR600,2024\nKawasaki,Ninja,2024\n";
        var input = CreateValidCsvFile(csvContent);
        input.HasHeaders = false;

        var result = await sut.ProcessAsync(input);
        result.Should().NotBeNull();
        result.Documents.Should().NotBeEmpty();
    }
}
