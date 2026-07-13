using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services.Optimization;
using MotorcycleRAG.Contracts.Models.DTOs.Optimization;

namespace MotorcycleRAG.UnitTests.Services.Optimization;

public sealed class BatchProcessingServiceTests
{
    [Fact]
    public async Task ProcessBatchAsync_ProcessesSuccessfulAndFailedBatchesAndUpdatesStatistics()
    {
        var sut = CreateSut();

        var result = await sut.ProcessBatchAsync<int, int>(
            [1, 2, 3, 4],
            (batch, _) => batch.Contains(3)
                ? throw new InvalidOperationException("bad batch")
                : Task.FromResult<IEnumerable<int>>(batch.Select(value => value * 2)),
            batchSize: 2);

        result.Results.Should().Equal(2, 4);
        result.TotalProcessed.Should().Be(4);
        result.SuccessfullyProcessed.Should().Be(2);
        result.Failed.Should().Be(2);
        result.Errors.Select(error => error.ItemIndex).Should().Equal(2, 3);
        sut.GetStatistics().Should().Match<BatchProcessingStatistics>(stats =>
            stats.TotalBatchesProcessed == 1 && stats.TotalItemsProcessed == 4 && stats.TotalItemsFailed == 2 && stats.OptimalBatchSize == 2);
    }

    [Fact]
    public async Task ProcessBatchAsync_WithEmptyOrInvalidInput_HandlesBoundaries()
    {
        var sut = CreateSut();

        var empty = await sut.ProcessBatchAsync<int, int>([], (_, _) => Task.FromResult<IEnumerable<int>>([]));

        empty.IsSuccess.Should().BeTrue();
        empty.TotalProcessed.Should().Be(0);
        await sut.Invoking(service => service.ProcessBatchAsync<int, int>(null!, (_, _) => Task.FromResult<IEnumerable<int>>([])))
            .Should().ThrowAsync<ArgumentNullException>();
        await sut.Invoking(service => service.ProcessBatchAsync<int, int>([1], null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenCancelled_WrapsTheFatalFailure()
    {
        var sut = CreateSut();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => sut.ProcessBatchAsync<int, int>([1], (batch, _) => Task.FromResult<IEnumerable<int>>(batch), cancellationToken: cancellation.Token);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().BeOfType<OperationCanceledException>();
    }

    [Fact]
    public async Task ProcessParallelBatchAsync_RetriesTransientFailuresAndCapturesPermanentOnes()
    {
        var sut = CreateSut();
        var attempts = new Dictionary<int, int>();
        var options = new BatchProcessingOptions
        {
            BatchSize = 3,
            MaxDegreeOfParallelism = 2,
            EnableRetry = true,
            MaxRetryAttempts = 1,
            RetryDelay = TimeSpan.Zero,
            ProcessingTimeout = TimeSpan.FromSeconds(1),
        };

        var result = await sut.ProcessParallelBatchAsync<int, int>([1, 2, 3], (value, _) =>
        {
            attempts[value] = attempts.GetValueOrDefault(value) + 1;
            if (value == 2 && attempts[value] == 1)
                throw new InvalidOperationException("transient");
            if (value == 3)
                throw new InvalidOperationException("permanent");
            return Task.FromResult(value * 10);
        }, options);

        result.Results.Should().BeEquivalentTo([10, 20]);
        result.Errors.Should().ContainSingle(error => error.ItemIndex == 2 && error.ExceptionMessage == "permanent");
        result.SuccessfullyProcessed.Should().Be(2);
        result.Failed.Should().Be(1);
        attempts[2].Should().Be(2);
        attempts[3].Should().Be(2);
    }

    [Fact]
    public async Task ProcessParallelBatchAsync_HandlesEmptyInvalidAndCancelledInputs()
    {
        var sut = CreateSut();
        var options = new BatchProcessingOptions { MaxDegreeOfParallelism = 1 };
        var empty = await sut.ProcessParallelBatchAsync<int, int>([], (value, _) => Task.FromResult(value), options);
        empty.TotalProcessed.Should().Be(0);

        await sut.Invoking(service => service.ProcessParallelBatchAsync<int, int>(null!, (value, _) => Task.FromResult(value), options))
            .Should().ThrowAsync<ArgumentNullException>();
        await sut.Invoking(service => service.ProcessParallelBatchAsync<int, int>([1], null!, options))
            .Should().ThrowAsync<ArgumentNullException>();
        await sut.Invoking(service => service.ProcessParallelBatchAsync<int, int>([1], (value, _) => Task.FromResult(value), null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0, 100, 1000, 100)]
    [InlineData(50, 10, 10_000, 10)]
    [InlineData(500, 10, 10_000, 50)]
    [InlineData(5_000, 10, 10_000, 100)]
    [InlineData(100_000, 10, 10_000, 266)]
    public void OptimizeBatchSize_UsesInputSizeAndMemoryLimits(int documents, long averageSize, long memory, int expected)
    {
        CreateSut().OptimizeBatchSize(documents, averageSize, memory).Should().Be(expected);
    }

    [Fact]
    public void ResetStatistics_ClearsAccumulatedValues()
    {
        var sut = CreateSut();
        sut.OptimizeBatchSize(100, 10, 1_000);

        sut.ResetStatistics();

        sut.GetStatistics().Should().Match<BatchProcessingStatistics>(stats =>
            stats.TotalBatchesProcessed == 0 && stats.TotalItemsProcessed == 0 && stats.TotalItemsFailed == 0 && stats.OptimalBatchSize == 0);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException() =>
        ((Action)(() => new BatchProcessingService(null!))).Should().Throw<ArgumentNullException>();

    private static BatchProcessingService CreateSut() => new(NullLogger<BatchProcessingService>.Instance);
}
