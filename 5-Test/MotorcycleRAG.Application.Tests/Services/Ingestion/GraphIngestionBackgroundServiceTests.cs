using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public class GraphIngestionBackgroundServiceTests
{
    private static readonly MethodInfo ExecuteAsyncMethod = typeof(GraphIngestionBackgroundService)
        .GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ExecuteAsync method not found via reflection.");

    /// <summary>Invokes the protected BackgroundService.ExecuteAsync via reflection so tests can drive it directly.</summary>
    private static Task InvokeExecuteAsync(GraphIngestionBackgroundService service, CancellationToken stoppingToken) =>
        (Task)ExecuteAsyncMethod.Invoke(service, new object[] { stoppingToken })!;

    private static (GraphIngestionBackgroundService Service, GraphIngestionChannel Channel, Mock<IIngestionJobService> JobServiceMock)
        CreateSut()
    {
        var channel = new GraphIngestionChannel();
        var jobServiceMock = new Mock<IIngestionJobService>();

        var services = new ServiceCollection();
        services.AddSingleton(jobServiceMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(x => x.ServiceProvider).Returns(serviceProvider);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);

        var service = new GraphIngestionBackgroundService(
            channel,
            scopeFactoryMock.Object,
            NullLogger<GraphIngestionBackgroundService>.Instance);

        return (service, channel, jobServiceMock);
    }

    private static IngestionJob CreateJob(string inputRef = "upload-1") =>
        new() { IngestionJobId = Guid.NewGuid(), InputRef = inputRef };

    [Fact]
    public async Task ExecuteAsync_WhenJobEnqueued_ProcessesJobAndCompletesWhenChannelClosed()
    {
        var (service, channel, jobServiceMock) = CreateSut();
        var job = CreateJob();

        jobServiceMock.Setup(x => x.ProcessGraphIngestionJobAsync(job)).Returns(Task.CompletedTask);

        await channel.Writer.WriteAsync(job);
        channel.Writer.Complete();

        await InvokeExecuteAsync(service, CancellationToken.None);

        jobServiceMock.Verify(x => x.ProcessGraphIngestionJobAsync(job), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenJobServiceThrowsGenericException_LogsAndContinuesDrainingQueue()
    {
        var (service, channel, jobServiceMock) = CreateSut();
        var job1 = CreateJob("upload-1");
        var job2 = CreateJob("upload-2");

        jobServiceMock.Setup(x => x.ProcessGraphIngestionJobAsync(job1)).ThrowsAsync(new InvalidOperationException("db unavailable"));
        jobServiceMock.Setup(x => x.ProcessGraphIngestionJobAsync(job2)).Returns(Task.CompletedTask);

        await channel.Writer.WriteAsync(job1);
        await channel.Writer.WriteAsync(job2);
        channel.Writer.Complete();

        var act = async () => await InvokeExecuteAsync(service, CancellationToken.None);

        await act.Should().NotThrowAsync();
        jobServiceMock.Verify(x => x.ProcessGraphIngestionJobAsync(job1), Times.Once);
        jobServiceMock.Verify(x => x.ProcessGraphIngestionJobAsync(job2), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationCanceledWhileStopping_RethrowsAndStopsConsumingQueue()
    {
        var (service, channel, jobServiceMock) = CreateSut();
        var job1 = CreateJob("upload-1");
        var job2 = CreateJob("upload-2");

        using var cts = new CancellationTokenSource();

        jobServiceMock
            .Setup(x => x.ProcessGraphIngestionJobAsync(job1))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException("host is shutting down"));

        await channel.Writer.WriteAsync(job1);
        await channel.Writer.WriteAsync(job2);

        var act = async () => await InvokeExecuteAsync(service, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        jobServiceMock.Verify(x => x.ProcessGraphIngestionJobAsync(job1), Times.Once);
        jobServiceMock.Verify(x => x.ProcessGraphIngestionJobAsync(job2), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationCanceledButNotStopping_IsTreatedAsGenericFailureAndContinues()
    {
        // The catch-when clause only matches when stoppingToken.IsCancellationRequested is true.
        // An OperationCanceledException raised for an unrelated reason (e.g. an internal timeout)
        // must fall through to the generic catch, be logged, and not stop the consumer loop.
        var (service, channel, jobServiceMock) = CreateSut();
        var job1 = CreateJob("upload-1");
        var job2 = CreateJob("upload-2");

        jobServiceMock
            .Setup(x => x.ProcessGraphIngestionJobAsync(job1))
            .ThrowsAsync(new OperationCanceledException("unrelated internal timeout"));
        jobServiceMock.Setup(x => x.ProcessGraphIngestionJobAsync(job2)).Returns(Task.CompletedTask);

        await channel.Writer.WriteAsync(job1);
        await channel.Writer.WriteAsync(job2);
        channel.Writer.Complete();

        var act = async () => await InvokeExecuteAsync(service, CancellationToken.None);

        await act.Should().NotThrowAsync();
        jobServiceMock.Verify(x => x.ProcessGraphIngestionJobAsync(job1), Times.Once);
        jobServiceMock.Verify(x => x.ProcessGraphIngestionJobAsync(job2), Times.Once);
    }

    [Fact]
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
    {
        var channel = new GraphIngestionChannel();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();

        Assert.Throws<ArgumentNullException>(() =>
            new GraphIngestionBackgroundService(null!, scopeFactoryMock.Object, NullLogger<GraphIngestionBackgroundService>.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new GraphIngestionBackgroundService(channel, null!, NullLogger<GraphIngestionBackgroundService>.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new GraphIngestionBackgroundService(channel, scopeFactoryMock.Object, null!));
    }
}
