using System;
using System.Collections.Generic;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Optimization;
using Xunit;

namespace MotorcycleRAG.UnitTests.Contracts.Models.DTOs;

public class RecordCoverageTests
{
    // Extracted to a static readonly field to avoid CA1861 (constant array passed as argument).
    private static readonly string[] s_failureErrors = { "error1", "error2" };

    [Fact]
    public void ModelValidationResult_FactoryMethods_Work()
    {
        var success = ModelValidationResult.Success();
        Assert.True(success.IsValid);
        Assert.Empty(success.Errors);
        Assert.Equal("Validation passed", success.ErrorMessage);

        var failure = ModelValidationResult.Failure("error");
        Assert.False(failure.IsValid);
        Assert.Single(failure.Errors);
        Assert.Equal("error", failure.ErrorMessage);

        var failureList = ModelValidationResult.Failure(s_failureErrors);
        Assert.False(failureList.IsValid);
        Assert.Equal(2, failureList.Errors.Count);
        Assert.Equal("error1; error2", failureList.ErrorMessage);

        // Null input exercises the null-coalescing branch in Failure(IEnumerable<string>).
        IEnumerable<string>? nullErrors = null;
        var failureNull = ModelValidationResult.Failure(nullErrors!);
        Assert.False(failureNull.IsValid);
        Assert.Empty(failureNull.Errors);
    }

    [Fact]
    public void BatchProcessingStatistics_Properties_Work()
    {
        var stats = new BatchProcessingStatistics
        {
            TotalBatchesProcessed = 10,
            TotalItemsProcessed = 100,
            TotalItemsFailed = 10,
            TotalProcessingTime = TimeSpan.FromSeconds(10),
            OptimalBatchSize = 50,
            LastUpdated = DateTime.UtcNow
        };

        // Computed: 100 items / 10s = 10/s; (100-10)/100 = 0.9.
        Assert.Equal(10, stats.AverageThroughputPerSecond);
        Assert.Equal(0.9, stats.SuccessRate);

        // Zero-denominator guards.
        var emptyStats = new BatchProcessingStatistics();
        Assert.Equal(0, emptyStats.AverageThroughputPerSecond);
        Assert.Equal(0, emptyStats.SuccessRate);
    }

    [Fact]
    public void BatchPipelineResult_Properties_Work()
    {
        var result = new BatchPipelineResult
        {
            BatchId = "batch-1",
            StartTime = DateTime.UtcNow,
            TotalFiles = 10,
            ProcessedSuccessfully = 8,
            ProcessedWithErrors = 1,
            Failed = 1
        };

        // IsCompleted tracks EndTime; HasErrors is true when Failed or ProcessedWithErrors > 0.
        Assert.False(result.IsCompleted);
        Assert.True(result.HasErrors);

        result.EndTime = DateTime.UtcNow;
        Assert.True(result.IsCompleted);

        var successResult = new BatchPipelineResult { Failed = 0, ProcessedWithErrors = 0 };
        Assert.False(successResult.HasErrors);
    }

    [Fact]
    public void PipelineRunStatusResult_FromStatus_SetsStatus()
    {
        var result = PipelineRunStatusResult.FromStatus("Completed");

        Assert.Equal("Completed", result.Status);
    }

    [Fact]
    public void ExecutionMetrics_SuccessRate_ComputedCorrectly()
    {
        var exec = new ExecutionMetrics
        {
            TotalExecutions = 10,
            SuccessfulExecutions = 8,
            FailedExecutions = 1,
            CancelledExecutions = 1
        };

        // 8 / 10 * 100 = 80.
        Assert.Equal(80, exec.SuccessRate);

        // Zero-denominator guard.
        var emptyExec = new ExecutionMetrics();
        Assert.Equal(0, emptyExec.SuccessRate);
    }
}
