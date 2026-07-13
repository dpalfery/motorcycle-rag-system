using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class WebScrapeOrchestratorTests
{
    private readonly Mock<IWebScrapeRunRepository> _runRepository = new();
    private readonly Mock<IAzureSearchClient> _searchClient = new();
    private readonly Mock<IMotorcycleIndexingService> _indexingService = new();
    private readonly Mock<IWebSourceRepository> _webSourceRepository = new();

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenDependenciesAreNull()
    {
        var actRunRepo = () => new WebScrapeOrchestrator(
            null!,
            _searchClient.Object,
            _indexingService.Object,
            _webSourceRepository.Object,
            NullLogger<WebScrapeOrchestrator>.Instance);
        var actSearch = () => new WebScrapeOrchestrator(
            _runRepository.Object,
            null!,
            _indexingService.Object,
            _webSourceRepository.Object,
            NullLogger<WebScrapeOrchestrator>.Instance);
        var actIndexing = () => new WebScrapeOrchestrator(
            _runRepository.Object,
            _searchClient.Object,
            null!,
            _webSourceRepository.Object,
            NullLogger<WebScrapeOrchestrator>.Instance);
        var actWebSource = () => new WebScrapeOrchestrator(
            _runRepository.Object,
            _searchClient.Object,
            _indexingService.Object,
            null!,
            NullLogger<WebScrapeOrchestrator>.Instance);
        var actLogger = () => new WebScrapeOrchestrator(
            _runRepository.Object,
            _searchClient.Object,
            _indexingService.Object,
            _webSourceRepository.Object,
            null!);

        actRunRepo.Should().Throw<ArgumentNullException>().WithParameterName("webScrapeRunRepository");
        actSearch.Should().Throw<ArgumentNullException>().WithParameterName("searchClient");
        actIndexing.Should().Throw<ArgumentNullException>().WithParameterName("indexingService");
        actWebSource.Should().Throw<ArgumentNullException>().WithParameterName("webSourceRepository");
        actLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task StartScrapeRunAsync_ShouldThrowArgumentException_WhenWebSourceIdIsInvalid(int webSourceId)
    {
        var sut = CreateSut();

        var act = async () => await sut.StartScrapeRunAsync(webSourceId);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("webSourceId");
    }

    [Fact]
    public async Task StartScrapeRunAsync_ShouldWrapNotFoundAsInvalidOperationException()
    {
        _webSourceRepository
            .Setup(x => x.GetWebSourceByIdAsync(42))
            .ReturnsAsync((WebSource?)null);
        var sut = CreateSut();

        var act = async () => await sut.StartScrapeRunAsync(42);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to start scrape run for web source 42");
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task StartScrapeRunAsync_ShouldWrapDisabledSourceAsInvalidOperationException()
    {
        _webSourceRepository
            .Setup(x => x.GetWebSourceByIdAsync(7))
            .ReturnsAsync(CreateWebSource(7, isEnabled: false));
        var sut = CreateSut();

        var act = async () => await sut.StartScrapeRunAsync(7);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("is not enabled");
    }

    [Fact]
    public async Task StartScrapeRunAsync_ShouldCreateRunAndCompletePipeline_WhenCrawlAndIndexSucceed()
    {
        var webSource = CreateWebSource(11);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _webSourceRepository.Setup(x => x.GetWebSourceByIdAsync(11)).ReturnsAsync(webSource);
        _runRepository.Setup(x => x.CreateWebScrapeRunAsync(11)).ReturnsAsync(101L);
        _runRepository
            .Setup(x => x.UpdateWebScrapeRunAsync(
                101L,
                It.IsAny<ScrapeRunStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true)
            .Callback<long, ScrapeRunStatus, int, int, int, string?>((_, status, _, _, _, _) =>
            {
                if (status == ScrapeRunStatus.Completed)
                {
                    completed.TrySetResult(true);
                }
            });
        _searchClient
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>()))
            .ReturnsAsync([CreateMatchingSearchResult(webSource.Url)]);
        _indexingService
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .ReturnsAsync(new BatchIndexingResult
            {
                Success = true,
                DocumentsProcessed = 5,
                DocumentsIndexed = 5
            });
        _webSourceRepository.Setup(x => x.UpdateWebSourceAsync(It.IsAny<WebSource>())).ReturnsAsync(true);
        var sut = CreateSut();

        var runId = await sut.StartScrapeRunAsync(11);

        runId.Should().Be(101L);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _indexingService.Verify(
            x => x.IndexDocumentsAsync(It.Is<IEnumerable<MotorcycleDocument>>(docs => docs.Any())),
            Times.Once);
        _webSourceRepository.Verify(
            x => x.UpdateWebSourceAsync(It.Is<WebSource>(source => source.Id == 11 && source.LastCrawledDate.HasValue)),
            Times.Once);
        _runRepository.Verify(
            x => x.UpdateWebScrapeRunAsync(101L, ScrapeRunStatus.Completed, It.Is<int>(c => c > 0), It.Is<int>(i => i > 0), It.IsAny<int>(), null),
            Times.Once);
    }

    [Fact]
    public async Task StartScrapeRunAsync_ShouldMarkRunFailed_WhenNoMatchingPagesAreCrawled()
    {
        var webSource = CreateWebSource(12);
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _webSourceRepository.Setup(x => x.GetWebSourceByIdAsync(12)).ReturnsAsync(webSource);
        _runRepository.Setup(x => x.CreateWebScrapeRunAsync(12)).ReturnsAsync(202L);
        _runRepository
            .Setup(x => x.UpdateWebScrapeRunAsync(
                202L,
                It.IsAny<ScrapeRunStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true)
            .Callback<long, ScrapeRunStatus, int, int, int, string?>((_, status, _, _, _, _) =>
            {
                if (status == ScrapeRunStatus.Failed)
                {
                    failed.TrySetResult(true);
                }
            });
        _searchClient
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>()))
            .ReturnsAsync([CreateMatchingSearchResult("https://other.example.com/page")]);
        var sut = CreateSut();

        var runId = await sut.StartScrapeRunAsync(12);

        runId.Should().Be(202L);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _runRepository.Verify(
            x => x.UpdateWebScrapeRunAsync(
                202L,
                ScrapeRunStatus.Failed,
                0,
                0,
                It.Is<int>(errors => errors > 0),
                It.Is<string?>(message => message != null && message.Contains("No pages were successfully crawled"))),
            Times.Once);
        _indexingService.Verify(
            x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()),
            Times.Never);
    }

    [Fact]
    public async Task StartScrapeRunAsync_ShouldContinueCrawl_WhenIndividualSearchTermsFail()
    {
        var webSource = CreateWebSource(13);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        _webSourceRepository.Setup(x => x.GetWebSourceByIdAsync(13)).ReturnsAsync(webSource);
        _runRepository.Setup(x => x.CreateWebScrapeRunAsync(13)).ReturnsAsync(303L);
        _runRepository
            .Setup(x => x.UpdateWebScrapeRunAsync(
                303L,
                It.IsAny<ScrapeRunStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true)
            .Callback<long, ScrapeRunStatus, int, int, int, string?>((_, status, _, _, _, _) =>
            {
                if (status == ScrapeRunStatus.Completed)
                {
                    completed.TrySetResult(true);
                }
            });
        _searchClient
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromException<SearchResult[]>(new InvalidOperationException("transient search failure"));
                }

                return Task.FromResult(new[] { CreateMatchingSearchResult(webSource.Url) });
            });
        var indexResult = new BatchIndexingResult
        {
            Success = true,
            DocumentsProcessed = 4,
            DocumentsIndexed = 4
        };
        indexResult.Errors.Add("partial index warning");
        _indexingService
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .ReturnsAsync(indexResult);
        _webSourceRepository.Setup(x => x.UpdateWebSourceAsync(It.IsAny<WebSource>())).ReturnsAsync(true);
        var sut = CreateSut();

        await sut.StartScrapeRunAsync(13);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _runRepository.Verify(
            x => x.UpdateWebScrapeRunAsync(
                303L,
                ScrapeRunStatus.Completed,
                It.Is<int>(crawled => crawled > 0),
                4,
                It.Is<int>(errors => errors > 0),
                null),
            Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CancelScrapeRunAsync_ShouldThrowArgumentException_WhenRunIdIsInvalid(long runId)
    {
        var sut = CreateSut();

        var act = async () => await sut.CancelScrapeRunAsync(runId);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("runId");
    }

    [Fact]
    public async Task CancelScrapeRunAsync_ShouldWrapMissingRunAsInvalidOperationException()
    {
        _runRepository.Setup(x => x.GetWebScrapeRunAsync(9)).ReturnsAsync((WebScrapeRun?)null);
        var sut = CreateSut();

        var act = async () => await sut.CancelScrapeRunAsync(9);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to cancel scrape run 9");
        exception.Which.InnerException!.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task CancelScrapeRunAsync_ShouldReturnFalse_WhenRunIsNotRunning()
    {
        _runRepository
            .Setup(x => x.GetWebScrapeRunAsync(8))
            .ReturnsAsync(new WebScrapeRun { Id = 8, Status = "Completed" });
        var sut = CreateSut();

        var result = await sut.CancelScrapeRunAsync(8);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelScrapeRunAsync_ShouldReturnFalse_WhenNoActiveCancellationTokenExists()
    {
        _runRepository
            .Setup(x => x.GetWebScrapeRunAsync(8))
            .ReturnsAsync(new WebScrapeRun { Id = 8, Status = "Running" });
        var sut = CreateSut();

        var result = await sut.CancelScrapeRunAsync(8);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelScrapeRunAsync_ShouldSignalCancellation_ForActivePipeline()
    {
        var webSource = CreateWebSource(15);
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledStatus = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSearch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _webSourceRepository.Setup(x => x.GetWebSourceByIdAsync(15)).ReturnsAsync(webSource);
        _runRepository.Setup(x => x.CreateWebScrapeRunAsync(15)).ReturnsAsync(505L);
        _runRepository
            .Setup(x => x.GetWebScrapeRunAsync(505L))
            .ReturnsAsync(new WebScrapeRun { Id = 505L, Status = "Running" });
        _runRepository
            .Setup(x => x.UpdateWebScrapeRunAsync(
                505L,
                It.IsAny<ScrapeRunStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true)
            .Callback<long, ScrapeRunStatus, int, int, int, string?>((_, status, _, _, _, _) =>
            {
                if (status == ScrapeRunStatus.Running)
                {
                    running.TrySetResult();
                }

                if (status == ScrapeRunStatus.Cancelled)
                {
                    cancelledStatus.TrySetResult(true);
                }
            });

        var searchCalls = 0;
        _searchClient
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>()))
            .Returns(async () =>
            {
                var call = Interlocked.Increment(ref searchCalls);
                if (call == 1)
                {
                    await running.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    // Hold the first search open until cancel is requested, then return.
                    // Cancellation is checked between terms, so we need cancel during/after first return.
                    await releaseSearch.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    return [CreateMatchingSearchResult(webSource.Url)];
                }

                await Task.Delay(Timeout.InfiniteTimeSpan);
                return Array.Empty<SearchResult>();
            });

        var sut = CreateSut();
        await sut.StartScrapeRunAsync(15);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelled = await sut.CancelScrapeRunAsync(505L);
        cancelled.Should().BeTrue();
        releaseSearch.TrySetResult();

        await cancelledStatus.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _runRepository.Verify(
            x => x.UpdateWebScrapeRunAsync(
                505L,
                ScrapeRunStatus.Cancelled,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                "Scrape operation was cancelled by user"),
            Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetScrapeRunStatusAsync_ShouldThrowArgumentException_WhenRunIdIsInvalid(long runId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetScrapeRunStatusAsync(runId);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("runId");
    }

    [Fact]
    public async Task GetScrapeRunStatusAsync_ShouldReturnRun()
    {
        var expected = new WebScrapeRun { Id = 3, Status = "Running" };
        _runRepository.Setup(x => x.GetWebScrapeRunAsync(3)).ReturnsAsync(expected);
        var sut = CreateSut();

        var result = await sut.GetScrapeRunStatusAsync(3);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GetScrapeRunStatusAsync_ShouldWrapRepositoryFailures()
    {
        var expected = new InvalidOperationException("boom");
        _runRepository.Setup(x => x.GetWebScrapeRunAsync(3)).ThrowsAsync(expected);
        var sut = CreateSut();

        var act = async () => await sut.GetScrapeRunStatusAsync(3);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get scrape run status for run 3");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task GetRecentScrapeRunsAsync_ShouldThrowArgumentException_WhenWebSourceIdIsInvalid(int webSourceId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetRecentScrapeRunsAsync(webSourceId);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("webSourceId");
    }

    [Fact]
    public async Task GetRecentScrapeRunsAsync_ShouldDefaultLimit_WhenLimitIsNonPositive()
    {
        _runRepository
            .Setup(x => x.GetRecentScrapeRunsAsync(4, 10))
            .ReturnsAsync([new WebScrapeRun { Id = 1 }]);
        var sut = CreateSut();

        var result = await sut.GetRecentScrapeRunsAsync(4, 0);

        result.Should().ContainSingle();
        _runRepository.Verify(x => x.GetRecentScrapeRunsAsync(4, 10), Times.Once);
    }

    [Fact]
    public async Task GetRecentScrapeRunsAsync_ShouldWrapRepositoryFailures()
    {
        _runRepository
            .Setup(x => x.GetRecentScrapeRunsAsync(4, 5))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();

        var act = async () => await sut.GetRecentScrapeRunsAsync(4, 5);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to get recent scrape runs for web source 4*");
    }

    [Fact]
    public async Task GetActiveScrapeRunsAsync_ShouldReturnActiveRuns()
    {
        var expected = new[] { new WebScrapeRun { Id = 1, Status = "Running" } };
        _runRepository.Setup(x => x.GetActiveScrapeRunsAsync()).ReturnsAsync(expected);
        var sut = CreateSut();

        var result = await sut.GetActiveScrapeRunsAsync();

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GetActiveScrapeRunsAsync_ShouldWrapRepositoryFailures()
    {
        _runRepository
            .Setup(x => x.GetActiveScrapeRunsAsync())
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();

        var act = async () => await sut.GetActiveScrapeRunsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Failed to get active scrape runs*");
    }

    [Fact]
    public async Task StartScrapeRunAsync_ShouldMarkFailed_WhenIndexingThrows()
    {
        var webSource = CreateWebSource(16);
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _webSourceRepository.Setup(x => x.GetWebSourceByIdAsync(16)).ReturnsAsync(webSource);
        _runRepository.Setup(x => x.CreateWebScrapeRunAsync(16)).ReturnsAsync(606L);
        _runRepository
            .Setup(x => x.UpdateWebScrapeRunAsync(
                606L,
                It.IsAny<ScrapeRunStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true)
            .Callback<long, ScrapeRunStatus, int, int, int, string?>((_, status, _, _, _, _) =>
            {
                if (status == ScrapeRunStatus.Failed)
                {
                    failed.TrySetResult(true);
                }
            });
        _searchClient
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>()))
            .ReturnsAsync([CreateMatchingSearchResult(webSource.Url, metadata: new Dictionary<string, object> { ["k"] = "v" })]);
        _indexingService
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .ThrowsAsync(new InvalidOperationException("index down"));
        var sut = CreateSut();

        await sut.StartScrapeRunAsync(16);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _runRepository.Verify(
            x => x.UpdateWebScrapeRunAsync(
                606L,
                ScrapeRunStatus.Failed,
                It.IsAny<int>(),
                0,
                It.IsAny<int>(),
                It.Is<string?>(message => message != null && message.Contains("Indexing failed"))),
            Times.Once);
    }

    [Fact]
    public async Task StartScrapeRunAsync_ShouldStillComplete_WhenCrawlDateUpdateFails()
    {
        var webSource = CreateWebSource(17);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _webSourceRepository.Setup(x => x.GetWebSourceByIdAsync(17)).ReturnsAsync(webSource);
        _runRepository.Setup(x => x.CreateWebScrapeRunAsync(17)).ReturnsAsync(707L);
        _runRepository
            .Setup(x => x.UpdateWebScrapeRunAsync(
                707L,
                It.IsAny<ScrapeRunStatus>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true)
            .Callback<long, ScrapeRunStatus, int, int, int, string?>((_, status, _, _, _, _) =>
            {
                if (status == ScrapeRunStatus.Completed)
                {
                    completed.TrySetResult(true);
                }
            });
        _searchClient
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<SearchOptions>()))
            .ReturnsAsync([CreateMatchingSearchResult(webSource.Url)]);
        _indexingService
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<IEnumerable<MotorcycleDocument>>()))
            .ReturnsAsync(new BatchIndexingResult { Success = true, DocumentsIndexed = 1, DocumentsProcessed = 1 });
        _webSourceRepository
            .Setup(x => x.UpdateWebSourceAsync(It.IsAny<WebSource>()))
            .ThrowsAsync(new InvalidOperationException("update failed"));
        var sut = CreateSut();

        await sut.StartScrapeRunAsync(17);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _runRepository.Verify(
            x => x.UpdateWebScrapeRunAsync(707L, ScrapeRunStatus.Completed, It.IsAny<int>(), 1, It.IsAny<int>(), null),
            Times.Once);
    }

    private WebScrapeOrchestrator CreateSut() =>
        new(
            _runRepository.Object,
            _searchClient.Object,
            _indexingService.Object,
            _webSourceRepository.Object,
            NullLogger<WebScrapeOrchestrator>.Instance);

    private static WebSource CreateWebSource(int id, bool isEnabled = true) =>
        new()
        {
            Id = id,
            Name = $"Source {id}",
            Url = "https://docs.example.com/motorcycles",
            IsEnabled = isEnabled
        };

    private static SearchResult CreateMatchingSearchResult(
        string sourceUrl,
        Dictionary<string, object>? metadata = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Content = "Motorcycle content",
            RelevanceScore = 0.9f,
            Source = new SearchSource
            {
                AgentType = SearchAgentType.WebSearch,
                SourceName = "Example Docs",
                SourceUrl = sourceUrl
            },
            Metadata = metadata ?? new Dictionary<string, object>()
        };
}
