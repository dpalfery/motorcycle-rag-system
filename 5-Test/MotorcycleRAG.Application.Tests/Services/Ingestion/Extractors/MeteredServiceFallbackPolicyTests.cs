using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services.Ingestion.Extractors;

namespace MotorcycleRAG.UnitTests.Services.Ingestion.Extractors;

public sealed class MeteredServiceFallbackPolicyTests
{
    [Fact]
    public async Task ExecuteWithFallbackAsync_WhenPrimarySucceeds_ReturnsPrimaryResult()
    {
        // Arrange
        var sut = CreateSut();
        var fallbackCalls = 0;

        // Act
        var result = await sut.ExecuteWithFallbackAsync(
            _ => Task.FromResult("primary"),
            _ =>
            {
                fallbackCalls++;
                return Task.FromResult("fallback");
            },
            CancellationToken.None);

        // Assert
        result.Should().Be("primary");
        fallbackCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteWithFallbackAsync_WhenPrimaryReturnsHttp429_UsesFallback()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.ExecuteWithFallbackAsync(
            _ => Task.FromException<string>(new HttpRequestException(
                "too many requests",
                inner: null,
                statusCode: HttpStatusCode.TooManyRequests)),
            _ => Task.FromResult("fallback"),
            CancellationToken.None);

        // Assert
        result.Should().Be("fallback");
    }

    [Fact]
    public async Task ExecuteWithFallbackAsync_WhenExceptionExposes503Status_UsesFallback()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.ExecuteWithFallbackAsync(
            _ => Task.FromException<string>(new StatusCodeException(503)),
            _ => Task.FromResult("fallback"),
            CancellationToken.None);

        // Assert
        result.Should().Be("fallback");
    }

    [Theory]
    [InlineData("quota has been exceeded")]
    [InlineData("rate limit reached")]
    [InlineData("request throttled")]
    [InlineData("too many requests")]
    [InlineData("provider returned 429")]
    public async Task ExecuteWithFallbackAsync_WhenQuotaMessageMatches_UsesFallback(string message)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.ExecuteWithFallbackAsync(
            _ => Task.FromException<string>(new InvalidOperationException(message)),
            _ => Task.FromResult("fallback"),
            CancellationToken.None);

        // Assert
        result.Should().Be("fallback");
    }

    [Fact]
    public async Task ExecuteWithFallbackAsync_WhenPrimaryFailsForAnotherReason_PropagatesWithoutFallback()
    {
        // Arrange
        var sut = CreateSut();
        var primaryFailure = new InvalidOperationException("invalid document");
        var fallbackCalls = 0;

        // Act
        var act = () => sut.ExecuteWithFallbackAsync(
            _ => Task.FromException<string>(primaryFailure),
            _ =>
            {
                fallbackCalls++;
                return Task.FromResult("fallback");
            },
            CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Should().BeSameAs(primaryFailure);
        fallbackCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteWithFallbackAsync_WithNullDependencies_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();
        Func<CancellationToken, Task<string>> operation = _ => Task.FromResult("value");

        // Act
        var nullPrimary = () => sut.ExecuteWithFallbackAsync<string>(null!, operation, CancellationToken.None);
        var nullFallback = () => sut.ExecuteWithFallbackAsync(operation, null!, CancellationToken.None);

        // Assert
        await nullPrimary.Should().ThrowAsync<ArgumentNullException>();
        await nullFallback.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act
        var create = () => new MeteredServiceFallbackPolicy(null!);

        // Assert
        create.Should().Throw<ArgumentNullException>();
    }

    private static MeteredServiceFallbackPolicy CreateSut() =>
        new(NullLogger<MeteredServiceFallbackPolicy>.Instance);

    private sealed class StatusCodeException(int statusCode) : Exception("service unavailable")
    {
        public int StatusCode { get; } = statusCode;
    }
}
