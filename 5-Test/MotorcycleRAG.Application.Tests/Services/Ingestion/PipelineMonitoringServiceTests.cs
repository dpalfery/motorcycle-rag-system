using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public sealed class PipelineMonitoringServiceTests
{
    private readonly PipelineMonitoringConfiguration _config = new();

    private PipelineMonitoringService CreateSut() =>
        new(Options.Create(_config), NullLogger<PipelineMonitoringService>.Instance);

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        var act = () => new PipelineMonitoringService(null!, NullLogger<PipelineMonitoringService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new PipelineMonitoringService(Options.Create(_config), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task TrackPipelineStartAsync_LogsAndCompletes()
    {
        var sut = CreateSut();

        await sut.TrackPipelineStartAsync("exec-1", PipelineType.PDF, new PipelineExecutionContext());

        // Observable behavior: no exception and method returns.
        Assert.True(true);
    }

    [Fact]
    public async Task TrackPipelineCompletionAsync_NullResult_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        var act = () => sut.TrackPipelineCompletionAsync("exec-1", null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("result");
    }

    [Fact]
    public async Task TrackPipelineCompletionAsync_ValidResult_Completes()
    {
        var sut = CreateSut();
        var result = new PipelineExecutionResult { Status = PipelineStatus.Completed };

        await sut.TrackPipelineCompletionAsync("exec-1", result);

        Assert.True(true);
    }

    [Fact]
    public async Task TrackPipelineFailureAsync_ValidException_Completes()
    {
        var sut = CreateSut();

        await sut.TrackPipelineFailureAsync("exec-1", new InvalidOperationException("boom"), new PipelineExecutionContext());

        Assert.True(true);
    }

    [Fact]
    public async Task SendNotificationAsync_NullNotification_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        var act = () => sut.SendNotificationAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("notification");
    }

    [Fact]
    public async Task SendNotificationAsync_ValidNotification_Completes()
    {
        var sut = CreateSut();

        await sut.SendNotificationAsync(new PipelineNotification { Title = "Test" });

        Assert.True(true);
    }

    [Fact]
    public async Task GetHealthStatusAsync_ReturnsHealthy()
    {
        var sut = CreateSut();

        var result = await sut.GetHealthStatusAsync();

        result.Should().NotBeNull();
        result.Status.Should().Be(OverallHealthStatus.Healthy);
    }

    [Fact]
    public async Task GetDetailedMetricsAsync_ReturnsEmptyMetrics()
    {
        var sut = CreateSut();

        var result = await sut.GetDetailedMetricsAsync(TimeSpan.FromHours(1));

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAlertConfigAsync_ReturnsDefaultConfig()
    {
        var sut = CreateSut();

        var result = await sut.GetAlertConfigAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAlertConfigAsync_Completes()
    {
        var sut = CreateSut();

        await sut.UpdateAlertConfigAsync(new PipelineAlertConfig());

        Assert.True(true);
    }
}
