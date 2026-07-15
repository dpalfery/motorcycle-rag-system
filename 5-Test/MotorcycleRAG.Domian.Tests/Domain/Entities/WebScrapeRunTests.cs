using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domian.Tests.Domain.Entities;

public sealed class WebScrapeRunTests
{
    [Fact]
    public void Create_WhenWebSourceIdIsValid_CreatesPendingRunAtUtc()
    {
        // Arrange
        var startedAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Local);

        // Act
        var run = WebScrapeRun.Create(100, startedAt);

        // Assert
        Assert.Equal(0, run.Id);
        Assert.Equal(100, run.WebSourceId);
        Assert.Equal(startedAt.ToUniversalTime(), run.CrawlStartTime);
        Assert.Null(run.CrawlEndTime);
        Assert.True(run.IsPending);
        Assert.Equal(0, run.PagesCrawled);
        Assert.Equal(0, run.PagesIndexed);
        Assert.Equal(0, run.Errors);
        Assert.Null(run.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WhenWebSourceIdIsNotPositive_ThrowsArgumentOutOfRangeException(int webSourceId)
    {
        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WebScrapeRun.Create(webSourceId));

        // Assert
        Assert.Equal("webSourceId", exception.ParamName);
    }

    [Theory]
    [InlineData(ScrapeRunStatus.Pending, false)]
    [InlineData(ScrapeRunStatus.Running, false)]
    [InlineData(ScrapeRunStatus.Completed, true)]
    [InlineData(ScrapeRunStatus.Cancelled, true)]
    public void Rehydrate_WhenPersistedStateIsValid_PreservesTheLifecycleState(ScrapeRunStatus status, bool terminal)
    {
        // Arrange
        var startedAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var endedAt = terminal ? startedAt.AddMinutes(30) : (DateTime?)null;

        // Act
        var run = WebScrapeRun.Rehydrate(10, 100, startedAt, endedAt, status, 5, 4, 1, null);

        // Assert
        Assert.Equal(10, run.Id);
        Assert.Equal(100, run.WebSourceId);
        Assert.Equal(status, run.Status);
        Assert.Equal(endedAt, run.CrawlEndTime);
        Assert.Equal(terminal ? TimeSpan.FromMinutes(30) : null, run.Duration);
    }

    [Fact]
    public void Rehydrate_WhenFailedRowIsValid_PreservesItsDiagnostic()
    {
        // Arrange
        var startedAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var run = WebScrapeRun.Rehydrate(
            10,
            100,
            startedAt,
            startedAt.AddMinutes(30),
            ScrapeRunStatus.Failed,
            5,
            4,
            1,
            "  crawler timed out  ");

        // Assert
        Assert.True(run.IsFailed);
        Assert.Equal("crawler timed out", run.ErrorMessage);
        Assert.Equal(TimeSpan.FromMinutes(30), run.Duration);
    }

    [Fact]
    public void Rehydrate_WhenPersistedStateViolatesInvariants_RejectsTheRow()
    {
        // Arrange
        var startedAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var invalidId = () => WebScrapeRun.Rehydrate(-1, 100, startedAt, null, ScrapeRunStatus.Pending, 0, 0, 0, null);
        var invalidSource = () => WebScrapeRun.Rehydrate(1, 0, startedAt, null, ScrapeRunStatus.Pending, 0, 0, 0, null);
        var invalidStatus = () => WebScrapeRun.Rehydrate(1, 100, startedAt, null, (ScrapeRunStatus)999, 0, 0, 0, null);
        var terminalWithoutEnd = () => WebScrapeRun.Rehydrate(1, 100, startedAt, null, ScrapeRunStatus.Completed, 0, 0, 0, null);
        var activeWithEnd = () => WebScrapeRun.Rehydrate(1, 100, startedAt, startedAt, ScrapeRunStatus.Running, 0, 0, 0, null);
        var failedWithoutError = () => WebScrapeRun.Rehydrate(1, 100, startedAt, startedAt, ScrapeRunStatus.Failed, 0, 0, 1, null);
        var negativeCount = () => WebScrapeRun.Rehydrate(1, 100, startedAt, null, ScrapeRunStatus.Pending, -1, 0, 0, null);
        var overlongError = () => WebScrapeRun.Rehydrate(
            1, 100, startedAt, startedAt, ScrapeRunStatus.Failed, 0, 0, 1, new string('x', 1_001));

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(invalidId);
        Assert.Throws<ArgumentOutOfRangeException>(invalidSource);
        Assert.Throws<ArgumentOutOfRangeException>(invalidStatus);
        Assert.Throws<ArgumentException>(terminalWithoutEnd);
        Assert.Throws<ArgumentException>(activeWithEnd);
        Assert.Throws<ArgumentException>(failedWithoutError);
        Assert.Throws<ArgumentOutOfRangeException>(negativeCount);
        Assert.Throws<ArgumentOutOfRangeException>(overlongError);
    }

    [Fact]
    public void Complete_WhenRunning_RecordsCountersAndARealDuration()
    {
        // Arrange
        var startedAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var run = WebScrapeRun.Create(100, startedAt);
        run.Start();

        // Act
        run.Complete(5, 4, 1, startedAt.AddMinutes(30));

        // Assert
        Assert.True(run.IsCompleted);
        Assert.Equal(5, run.PagesCrawled);
        Assert.Equal(4, run.PagesIndexed);
        Assert.Equal(1, run.Errors);
        Assert.Equal(TimeSpan.FromMinutes(30), run.Duration);
        Assert.Throws<InvalidOperationException>(() => run.Start());
    }

    [Fact]
    public void Fail_WhenPendingOrRunning_RecordsBoundedDiagnostic()
    {
        // Arrange
        var pending = WebScrapeRun.Create(100);
        var running = WebScrapeRun.Create(101);
        running.Start();

        // Act
        pending.Fail("pending crawl could not start");
        running.Fail(new string('x', 1_500), pagesCrawled: 2, pagesIndexed: 1, errors: 1);

        // Assert
        Assert.True(pending.IsFailed);
        Assert.Equal("pending crawl could not start", pending.ErrorMessage);
        Assert.True(running.IsFailed);
        Assert.Equal(1_000, running.ErrorMessage!.Length);
        Assert.Equal(2, running.PagesCrawled);
        Assert.Throws<InvalidOperationException>(() => running.Fail("late failure"));
    }

    [Fact]
    public void Cancel_WhenRunning_RecordsCountersAndRejectsInvalidCancellation()
    {
        // Arrange
        var pending = WebScrapeRun.Create(100);
        var running = WebScrapeRun.Create(101);
        running.Start();

        // Act
        running.Cancel(pagesCrawled: 8, pagesIndexed: 5, errors: 2);

        // Assert
        Assert.True(running.IsCancelled);
        Assert.Equal(8, running.PagesCrawled);
        Assert.Equal(5, running.PagesIndexed);
        Assert.Equal(2, running.Errors);
        Assert.Equal("Scrape operation was cancelled by user", running.ErrorMessage);
        Assert.NotNull(running.CrawlEndTime);
        Assert.Throws<InvalidOperationException>(() => pending.Cancel());
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Complete_WhenCounterIsNegative_RejectsTheTransition(int pagesCrawled, int pagesIndexed, int errors)
    {
        // Arrange
        var run = WebScrapeRun.Create(100);
        run.Start();

        // Act
        var act = () => run.Complete(pagesCrawled, pagesIndexed, errors);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
        Assert.True(run.IsRunning);
    }
}
