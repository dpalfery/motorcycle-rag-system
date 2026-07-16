using Azure;
using MotorcycleRAG.Core.Exceptions;
using MotorcycleRAG.Persistence.Azure.Search;
using Polly;
using Polly.Retry;

namespace MotorcycleRAG.Persistence.Tests.Azure.Search;

public sealed class SearchIndexResiliencePipelineProviderTests
{
    // ---- Constructor ----

    [Fact]
    public void Constructor_ShouldCreatePipeline()
    {
        var sut = new SearchIndexResiliencePipelineProvider();

        sut.Pipeline.Should().NotBeNull();
    }

    // ---- Pipeline property ----

    [Fact]
    public void Pipeline_ShouldReturnResiliencePipeline()
    {
        var sut = new SearchIndexResiliencePipelineProvider();

        var pipeline = sut.Pipeline;

        pipeline.Should().BeOfType<ResiliencePipeline>();
    }

    [Fact]
    public void Pipeline_ShouldBeSameInstanceOnMultipleCalls()
    {
        var sut = new SearchIndexResiliencePipelineProvider();

        var pipeline1 = sut.Pipeline;
        var pipeline2 = sut.Pipeline;

        pipeline1.Should().BeSameAs(pipeline2);
    }

    // ---- ShouldRetry ----

    [Fact]
    public void ShouldRetry_WithSearchIndexNotFoundException_ShouldReturnFalse()
    {
        var ex = new SearchIndexNotFoundException("motorcycle-dirt");

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public void ShouldRetry_WithNonTransientStatusCode_ShouldReturnFalse(int statusCode)
    {
        var ex = new RequestFailedException(statusCode, $"Error with status {statusCode}");

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(429)]
    public void ShouldRetry_WithTransientStatusCode_ShouldReturnTrue(int statusCode)
    {
        var ex = new RequestFailedException(statusCode, $"Transient error {statusCode}");

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_WithRequestFailedExceptionStatusZero_ShouldReturnTrue()
    {
        var ex = new RequestFailedException(0, "Network error");

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_WithHttpRequestException_ShouldReturnTrue()
    {
        var ex = new HttpRequestException("Connection refused");

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_WithTaskCanceledExceptionWithTimeoutInner_ShouldReturnTrue()
    {
        var ex = new TaskCanceledException("The operation timed out", new TimeoutException("Timeout"));

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_WithTaskCanceledExceptionWithoutTimeoutInner_ShouldReturnFalse()
    {
        var ex = new TaskCanceledException("Operation was cancelled");

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_WithTimeoutException_ShouldReturnTrue()
    {
        var ex = new TimeoutException("The operation timed out");

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_WithGenericException_ShouldReturnFalse()
    {
        var ex = new InvalidOperationException("Something went wrong");

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeFalse();
    }

    // ---- ShouldRetry - edge case status codes ----

    [Theory]
    [InlineData(308)] // redirect — not in non-transient set, not >=500, not 429, not 0 → false
    [InlineData(499)] // client closed request — not in non-transient set, not >=500, not 429, not 0 → false
    [InlineData(301)] // moved permanently → false
    public void ShouldRetry_WithRedirectOrClientClosedStatusCode_ShouldReturnFalse(int statusCode)
    {
        var ex = new RequestFailedException(statusCode, $"Status {statusCode}");

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_WithAggregateExceptionContainingTransientInner_ShouldReturnFalse()
    {
        // AggregateException is not specially handled — falls through to return false
        var inner = new HttpRequestException("Connection refused");
        var ex = new AggregateException(inner);

        var result = SearchIndexResiliencePipelineProvider.ShouldRetry(ex);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRetry_WithNullExceptionInOutcome_ShouldNotCallShouldRetry()
    {
        // The ShouldHandle callback in the Polly retry strategy checks
        // args.Outcome.Exception for null before calling ShouldRetry.
        // Invoke the pipeline with a successful outcome and verify the
        // null-guard works: a null Exception in the outcome must not reach
        // ShouldRetry (which would throw on null input).
        var sut = new SearchIndexResiliencePipelineProvider();

        var result = await sut.Pipeline.ExecuteAsync(
            _ => new ValueTask<int>(42),
            CancellationToken.None);

        result.Should().Be(42);
    }
}
