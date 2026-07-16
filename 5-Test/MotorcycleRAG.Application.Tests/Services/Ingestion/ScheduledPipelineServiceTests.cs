using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public class ScheduledPipelineServiceTests
{
    private static ScheduledPipelineService CreateService()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var configOptions = Options.Create(new ScheduledProcessingConfiguration
        {
            DefaultCronExpression = "0 2 * * *",
            IsEnabledByDefault = false,
            DefaultProcessingWindow = TimeSpan.FromHours(4),
            DefaultMaxConcurrentJobs = 3,
            BaseDirectory = "/scheduled-root"
        });
        var azureOptions = Options.Create(new AzureFoundryOptions
        {
            DocumentIntelligenceEndpoint = string.Empty
        });
        var localFileStoreMock = new Mock<ILocalFileStore>();
        var localFileDiscoveryMock = new Mock<ILocalFileDiscovery>();
        var loggerMock = new Mock<ILogger<ScheduledPipelineService>>();

        return new ScheduledPipelineService(
            scopeFactoryMock.Object,
            configOptions,
            azureOptions,
            localFileStoreMock.Object,
            localFileDiscoveryMock.Object,
            loggerMock.Object);
    }

    private static void SetCancellationTokenSource(ScheduledPipelineService service, CancellationTokenSource? cts)
    {
        var field = typeof(ScheduledPipelineService).GetField("_cancellationTokenSource",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(service, cts);
    }

    [Fact]
    public async Task CancelCurrentRunAsync_WhenCancellationTokenSourceIsNull_ReturnsFalse()
    {
        var service = CreateService();
        SetCancellationTokenSource(service, null);

        var result = await service.CancelCurrentRunAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelCurrentRunAsync_WhenCancelAsyncSucceeds_ReturnsTrue()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        SetCancellationTokenSource(service, cts);

        var result = await service.CancelCurrentRunAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CancelCurrentRunAsync_WhenCancelAsyncThrowsObjectDisposedException_ReturnsFalse()
    {
        var service = CreateService();
        var cts = new CancellationTokenSource();
        cts.Dispose();
        SetCancellationTokenSource(service, cts);

        var result = await service.CancelCurrentRunAsync();

        result.Should().BeFalse();
    }
}
