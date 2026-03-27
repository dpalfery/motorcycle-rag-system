using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Application.Pipeline.Validators;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class IngestionJobsControllerTests
{
    [Fact]
    public async Task UploadAsync_WithBikeGraphCsv_UploadsCsvToRawUploadsAndReturnsAccepted()
    {
        // Arrange
        var blobStorage = new Mock<IBlobStorageService>();
        blobStorage
            .Setup(service => service.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example/raw-uploads/upload.csv");

        var sut = CreateController(blobStorage.Object);
        await using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes("make,model\nYamaha,R1"));
        var file = CreateFormFile(csvStream, "bikes.csv", "text/csv");

        // Act
        var result = await sut.UploadAsync(file, "bike-graph", CancellationToken.None);

        // Assert
        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var response = accepted.Value.Should().BeOfType<IngestionUploadResponse>().Subject;

        response.UploadId.Should().NotBeNullOrWhiteSpace();
        response.FileName.Should().Be("bikes.csv");
        response.DocumentType.Should().Be("bike-graph");
        response.Status.Should().Be("uploaded");

        blobStorage.Verify(service => service.UploadAsync(
            "raw-uploads",
            $"{response.UploadId}.csv",
            It.IsAny<Stream>(),
            "text/csv",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_WithBikeGraphNonCsv_ReturnsBadRequest()
    {
        // Arrange
        var blobStorage = new Mock<IBlobStorageService>();
        var sut = CreateController(blobStorage.Object);
        await using var pdfStream = new MemoryStream("%PDF-1.7"u8.ToArray());
        var file = CreateFormFile(pdfStream, "bikes.pdf", "application/pdf");

        // Act
        var result = await sut.UploadAsync(file, "bike-graph", CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Contain(".csv");

        blobStorage.Verify(service => service.UploadAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IngestionJobsController CreateController(IBlobStorageService blobStorageService)
    {
        return new IngestionJobsController(
            Mock.Of<IIngestionJobService>(),
            new IngestionJobValidator(),
            blobStorageService,
            Options.Create(new BlobStorageOptions
            {
                AccountEndpoint = "https://storage.example",
                RawUploadsContainer = "raw-uploads"
            }),
            NullLogger<IngestionJobsController>.Instance);
    }

    private static IFormFile CreateFormFile(Stream stream, string fileName, string contentType)
    {
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
