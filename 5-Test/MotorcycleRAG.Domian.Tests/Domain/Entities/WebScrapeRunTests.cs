using System;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.Domian.Tests.Domain.Entities;

public class WebScrapeRunTests
{
    [Fact]
    public void StatusProperties_ReturnExpectedValues()
    {
        var runPending = new WebScrapeRun { Status = "Pending" };
        Assert.True(runPending.IsPending);
        Assert.False(runPending.IsRunning);
        Assert.False(runPending.IsCompleted);
        Assert.False(runPending.IsFailed);

        var runRunning = new WebScrapeRun { Status = "Running" };
        Assert.False(runRunning.IsPending);
        Assert.True(runRunning.IsRunning);
        Assert.False(runRunning.IsCompleted);
        Assert.False(runRunning.IsFailed);

        var runCompleted = new WebScrapeRun { Status = "Completed" };
        Assert.False(runCompleted.IsPending);
        Assert.False(runCompleted.IsRunning);
        Assert.True(runCompleted.IsCompleted);
        Assert.False(runCompleted.IsFailed);

        var runFailed = new WebScrapeRun { Status = "Failed" };
        Assert.False(runFailed.IsPending);
        Assert.False(runFailed.IsRunning);
        Assert.False(runFailed.IsCompleted);
        Assert.True(runFailed.IsFailed);
    }

    [Fact]
    public void Duration_WhenCrawlEndTimeIsNull_ReturnsNull()
    {
        var run = new WebScrapeRun
        {
            CrawlStartTime = DateTime.UtcNow,
            CrawlEndTime = null
        };

        Assert.Null(run.Duration);
    }

    [Fact]
    public void Duration_WhenCrawlEndTimeIsSet_ReturnsDifference()
    {
        var startTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var endTime = new DateTime(2025, 1, 1, 12, 30, 0, DateTimeKind.Utc);
        
        var run = new WebScrapeRun
        {
            CrawlStartTime = startTime,
            CrawlEndTime = endTime
        };

        Assert.Equal(TimeSpan.FromMinutes(30), run.Duration);
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var run = new WebScrapeRun
        {
            Id = 1,
            WebSourceId = 100,
            PagesCrawled = 5,
            PagesIndexed = 4,
            Errors = 1,
            ErrorMessage = "Test Error"
        };

        Assert.Equal(1, run.Id);
        Assert.Equal(100, run.WebSourceId);
        Assert.Equal("Pending", run.Status);
        Assert.Equal(5, run.PagesCrawled);
        Assert.Equal(4, run.PagesIndexed);
        Assert.Equal(1, run.Errors);
        Assert.Equal("Test Error", run.ErrorMessage);
        Assert.True(run.CrawlStartTime <= DateTime.UtcNow);
    }
}
