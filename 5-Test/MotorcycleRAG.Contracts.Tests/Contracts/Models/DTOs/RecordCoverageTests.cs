using System;
using System.Collections.Generic;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Optimization;
using Xunit;

namespace MotorcycleRAG.UnitTests.Contracts.Models.DTOs;

public class RecordCoverageTests
{
    [Fact]
    public void AgentRunStatus_Constructor_SetsProperties()
    {
        var toolCalls = new List<AgentToolCall>();
        var status = new AgentRunStatus("run-1", AgentRunState.Completed, toolCalls);
        
        Assert.Equal("run-1", status.RunId);
        Assert.Equal(AgentRunState.Completed, status.State);
        Assert.Same(toolCalls, status.RequiredToolCalls);
    }

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

        var failureList = ModelValidationResult.Failure(new[] { "error1", "error2" });
        Assert.False(failureList.IsValid);
        Assert.Equal(2, failureList.Errors.Count);
        Assert.Equal("error1; error2", failureList.ErrorMessage);
        
        var failureNull = ModelValidationResult.Failure((IEnumerable<string>)null);
        Assert.False(failureNull.IsValid);
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

        Assert.Equal(10, stats.AverageThroughputPerSecond);
        Assert.Equal(0.9, stats.SuccessRate);
        
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

        Assert.False(result.IsCompleted);
        Assert.True(result.HasErrors);
        Assert.NotNull(result.Results);
        Assert.NotNull(result.BatchMetrics);

        result.EndTime = DateTime.UtcNow;
        Assert.True(result.IsCompleted);
        
        var successResult = new BatchPipelineResult { Failed = 0, ProcessedWithErrors = 0 };
        Assert.False(successResult.HasErrors);
    }

    [Fact]
    public void PipelineRunStatusResult_Properties_Work()
    {
        var result = new PipelineRunStatusResult
        {
            Status = "Running",
            Message = "msg",
            Error = "err",
            RawJson = "{}"
        };

        Assert.Equal("Running", result.Status);
        Assert.Equal("msg", result.Message);
        Assert.Equal("err", result.Error);
        Assert.Equal("{}", result.RawJson);

        var fromStatus = PipelineRunStatusResult.FromStatus("Completed");
        Assert.Equal("Completed", fromStatus.Status);
    }

    [Fact]
    public void DetailedPipelineMetrics_Properties_Work()
    {
        var metrics = new DetailedPipelineMetrics
        {
            TimeWindow = TimeSpan.FromHours(1),
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow
        };

        Assert.NotNull(metrics.Executions);
        Assert.NotNull(metrics.Processing);
        Assert.NotNull(metrics.Errors);
        Assert.NotNull(metrics.Performance);
        Assert.NotNull(metrics.TrendData);

        var exec = new ExecutionMetrics
        {
            TotalExecutions = 10,
            SuccessfulExecutions = 8,
            FailedExecutions = 1,
            CancelledExecutions = 1
        };
        Assert.Equal(80, exec.SuccessRate);
        Assert.NotNull(exec.ExecutionsByType);

        var emptyExec = new ExecutionMetrics();
        Assert.Equal(0, emptyExec.SuccessRate);

        var proc = new ProcessingMetrics
        {
            TotalDocumentsProcessed = 10,
            TotalDocumentsIndexed = 10,
            TotalBytesProcessed = 100
        };
        Assert.NotNull(proc.DocumentsByType);
        Assert.NotNull(proc.BytesByType);

        var err = new ErrorMetrics
        {
            TotalErrors = 1,
            TotalWarnings = 1
        };
        Assert.NotNull(err.ErrorsByType);
        Assert.NotNull(err.ErrorsByPipeline);
        Assert.NotNull(err.TopErrors);

        var perf = new ExecutionPerformanceMetrics
        {
            AverageExecutionTime = TimeSpan.FromSeconds(1),
            MedianExecutionTime = TimeSpan.FromSeconds(1),
            P95ExecutionTime = TimeSpan.FromSeconds(1),
            AverageDocumentsPerSecond = 10,
            AverageBytesPerSecond = 100
        };
        Assert.NotNull(perf.ExecutionTimeByType);

        var trend = new TrendDataPoint
        {
            Timestamp = DateTime.UtcNow,
            Executions = 1,
            SuccessfulExecutions = 1,
            FailedExecutions = 0,
            AverageExecutionTime = TimeSpan.FromSeconds(1),
            DocumentsProcessed = 10
        };

        var errSum = new ErrorSummary
        {
            ErrorType = "type",
            ErrorMessage = "msg",
            Count = 1,
            FirstOccurrence = DateTime.UtcNow,
            LastOccurrence = DateTime.UtcNow
        };
        Assert.NotNull(errSum.AffectedExecutions);
    }
}
