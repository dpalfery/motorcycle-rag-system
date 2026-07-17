using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public sealed class SpecsIngestionServiceTests
{
    [Fact]
    public async Task IngestCsvAsync_WhenRowsAreValid_UpsertsNormalizedBikeModels()
    {
        var repository = CreateRepository();
        var upserted = new List<BikeModel>();
        repository.Setup(store => store.UpsertAsync(It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()))
            .Callback<BikeModel, CancellationToken>((model, _) => upserted.Add(model))
            .ReturnsAsync(Guid.NewGuid());
        var sut = CreateSut(repository.Object);
        await using var csv = CreateCsv(
            "Make,Make,Model,Year\n" +
            " honda ,duplicate, cbr 600rr ,2024\n" +
            "Yamaha,duplicate,MT-07,2023\n");

        await sut.IngestCsvAsync("upload-1", "user-1", csv);

        upserted.Should().HaveCount(2);
        upserted[0].Should().BeEquivalentTo(BikeModel.Create(
            "honda",
            "cbr 600rr",
            2024,
            createdByUserId: "user-1",
            uploadRef: "upload-1"),
            options => options.Excluding(model => model.Id)
                .Excluding(model => model.CreatedAtUtc)
                .Excluding(model => model.UpdatedAtUtc));
        upserted[1].NormalizedName.Should().Be("Yamaha MT-07");
    }

    [Fact]
    public async Task IngestCsvAsync_WhenRowsAreInvalidOrIncomplete_SkipsThemWithoutUpserting()
    {
        var repository = CreateRepository();
        repository.Setup(store => store.UpsertAsync(It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var sut = CreateSut(repository.Object);
        await using var csv = CreateCsv(
            "Make,Model,Year\n" +
            "Honda,,2024\n" +
            ",CBR,2024\n" +
            "Yamaha,MT-07,not-a-year\n" +
            "Suzuki,GSX-R\n" +
            "\n");

        await sut.IngestCsvAsync("upload-1", "user-1", csv);

        repository.Verify(store => store.UpsertAsync(
            It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    [InlineData("Make,Model\nHonda,CBR\n")]
    [InlineData("Model,Year\nCBR,2024\n")]
    public async Task IngestCsvAsync_WhenCsvIsEmptyOrRequiredColumnsAreMissing_DoesNotUpsert(string content)
    {
        var repository = CreateRepository();
        var sut = CreateSut(repository.Object);
        await using var csv = CreateCsv(content);

        await sut.IngestCsvAsync("upload-1", "user-1", csv);

        repository.Verify(store => store.UpsertAsync(
            It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IngestCsvAsync_WhenArgumentsAreInvalid_Throws()
    {
        var sut = CreateSut(CreateRepository().Object);
        await using var csv = CreateCsv("Make,Model,Year\n");

#pragma warning disable CA2025 // tasks are awaited before disposal scope ends
        var missingUpload = () => sut.IngestCsvAsync(" ", "user", csv);
        var missingUser = () => sut.IngestCsvAsync("upload", " ", csv);
        var missingStream = () => sut.IngestCsvAsync("upload", "user", null!);
#pragma warning restore CA2025

        await missingUpload.Should().ThrowAsync<ArgumentException>();
        await missingUser.Should().ThrowAsync<ArgumentException>();
        await missingStream.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WhenDependencyIsNull_ThrowsArgumentNullException()
    {
        var repository = CreateRepository().Object;

        Assert.Throws<ArgumentNullException>(() => new SpecsIngestionService(null!, NullLogger<SpecsIngestionService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new SpecsIngestionService(repository, null!));
    }

    private static Mock<IBikeModelRepository> CreateRepository() => new(MockBehavior.Strict);

    private static SpecsIngestionService CreateSut(IBikeModelRepository repository) => new(
        repository,
        NullLogger<SpecsIngestionService>.Instance);

    private static MemoryStream CreateCsv(string content) => new(Encoding.UTF8.GetBytes(content));
}
