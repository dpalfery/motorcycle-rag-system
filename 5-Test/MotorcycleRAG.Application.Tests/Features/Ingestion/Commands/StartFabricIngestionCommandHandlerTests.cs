using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Features.Ingestion.Commands;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Features.Ingestion.Commands;

public class StartFabricIngestionCommandHandlerTests
{
    private readonly Mock<IIngestionJobService> _jobServiceMock;
    private readonly StartFabricIngestionCommandHandler _handler;

    public StartFabricIngestionCommandHandlerTests()
    {
        _jobServiceMock = new Mock<IIngestionJobService>();
        var logger = NullLogger<StartFabricIngestionCommandHandler>.Instance;
        _handler = new StartFabricIngestionCommandHandler(_jobServiceMock.Object, logger);
    }

    [Fact]
    public void Constructor_NullService_Throws()
    {
        var logger = NullLogger<StartFabricIngestionCommandHandler>.Instance;
        Assert.Throws<ArgumentNullException>(() => new StartFabricIngestionCommandHandler(null!, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new StartFabricIngestionCommandHandler(_jobServiceMock.Object, null!));
    }

    [Fact]
    public async Task HandleAsync_NullCommand_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _handler.HandleAsync(null!));
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_CallsServiceAndReturnsResponse()
    {
        var request = new IngestionJobStartRequest { UploadId = "container", DocumentType = "folder", ProcessorRunId = "run-1" };
        var userId = "user123";
        var command = new StartFabricIngestionCommand(request, userId);
        var expectedJobId = Guid.NewGuid();
        var expectedResponse = new IngestionJobStatusResponse { JobId = expectedJobId, Status = "Queued" };

        _jobServiceMock.Setup(x => x.StartJobAsync(request, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var response = await _handler.HandleAsync(command);

        Assert.NotNull(response);
        Assert.Equal(expectedJobId, response.JobId);
        _jobServiceMock.Verify(x => x.StartJobAsync(request, userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
