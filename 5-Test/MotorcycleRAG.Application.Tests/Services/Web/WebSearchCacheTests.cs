using System.Collections;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services.Web;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services.Web;

public sealed class WebSearchCacheTests
{
    [Fact]
    public void Constructor_WithInvalidConfigurationOrLogger_Throws()
    {
        // Act
        var invalidSize = () => new WebSearchCache(TimeSpan.FromMinutes(1), 0, NullLogger<WebSearchCache>.Instance);
        var invalidExpiration = () => new WebSearchCache(TimeSpan.Zero, 1, NullLogger<WebSearchCache>.Instance);
        var nullLogger = () => new WebSearchCache(TimeSpan.FromMinutes(1), 1, null!);

        // Assert
        invalidSize.Should().Throw<ArgumentOutOfRangeException>();
        invalidExpiration.Should().Throw<ArgumentOutOfRangeException>();
        nullLogger.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryGet_AfterSet_UsesCaseInsensitiveKeyAndReturnsCachedResults()
    {
        // Arrange
        var sut = CreateSut();
        var expected = new[] { CreateResult("first"), CreateResult("second") };

        // Act
        sut.Set("Honda CBR", expected);
        var found = sut.TryGet("honda cbr", out var results);

        // Assert
        found.Should().BeTrue();
        results.Should().Equal(expected);
    }

    [Fact]
    public void TryGetCachedResults_LimitsCachedValuesAndReportsMisses()
    {
        // Arrange
        var sut = CreateSut();
        var expected = new[] { CreateResult("first"), CreateResult("second"), CreateResult("third") };
        sut.CacheResults("Honda", new SearchParameters(), expected);

        // Act
        var found = sut.TryGetCachedResults("HONDA", new SearchParameters { MaxResults = 2 }, out var results);
        var missing = sut.TryGetCachedResults("Yamaha", new SearchParameters(), out var missingResults);

        // Assert
        found.Should().BeTrue();
        results.Should().Equal(expected.Take(2));
        missing.Should().BeFalse();
        missingResults.Should().BeEmpty();
    }

    [Fact]
    public void TryGet_WithEmptyOrExpiredCacheEntry_ReturnsMissAndRemovesExpiredEntry()
    {
        // Arrange
        var sut = CreateSut();
        sut.Set("empty", []);
        sut.Set("expired", [CreateResult("stale")]);
        SetCachedAt(sut, "EXPIRED", DateTime.UtcNow.AddMinutes(-2));

        // Act
        var emptyFound = sut.TryGet("empty", out var emptyResults);
        var expiredFound = sut.TryGet("expired", out var expiredResults);
        var expiredFoundAgain = sut.TryGet("expired", out _);

        // Assert
        emptyFound.Should().BeFalse();
        emptyResults.Should().BeEmpty();
        expiredFound.Should().BeFalse();
        expiredResults.Should().BeEmpty();
        expiredFoundAgain.Should().BeFalse();
    }

    [Fact]
    public void Set_WhenCapacityIsReached_EvictsAnExistingEntry()
    {
        // Arrange
        var sut = CreateSut(maxCacheSize: 1);
        sut.Set("first", [CreateResult("first")]);

        // Act
        sut.Set("second", [CreateResult("second")]);
        var firstFound = sut.TryGet("first", out _);
        var secondFound = sut.TryGet("second", out var secondResults);

        // Assert
        firstFound.Should().BeFalse();
        secondFound.Should().BeTrue();
        secondResults.Should().ContainSingle().Which.Id.Should().Be("second");
    }

    [Fact]
    public void Clear_AndNullArguments_RespectTheCacheContract()
    {
        // Arrange
        var sut = CreateSut();
        sut.Set("Honda", [CreateResult("first")]);

        // Act
        sut.Clear();
        var foundAfterClear = sut.TryGet("Honda", out _);
        var nullQuery = () => sut.TryGet(null!, out _);
        var nullOptions = () => sut.TryGetCachedResults("Honda", null!, out _);
        var nullSetQuery = () => sut.Set(null!, []);
        var nullResults = () => sut.Set("Honda", null!);

        // Assert
        foundAfterClear.Should().BeFalse();
        nullQuery.Should().Throw<ArgumentNullException>();
        nullOptions.Should().Throw<ArgumentNullException>();
        nullSetQuery.Should().Throw<ArgumentNullException>();
        nullResults.Should().Throw<ArgumentNullException>();
    }

    private static WebSearchCache CreateSut(int maxCacheSize = 10) => new(
        TimeSpan.FromMinutes(1),
        maxCacheSize,
        NullLogger<WebSearchCache>.Instance);

    private static SearchResult CreateResult(string id) => new()
    {
        Id = id,
        Content = id,
        Source = new SearchSource { SourceName = "unit" },
    };

    private static void SetCachedAt(WebSearchCache cache, string queryKey, DateTime cachedAt)
    {
        var entries = (IEnumerable)typeof(WebSearchCache)
            .GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cache)!;
        var entry = entries.Cast<object>()
            .Single(item => item.ToString()!.Contains(queryKey, StringComparison.Ordinal));
        var cachedResults = entry.GetType().GetProperty("Value")!.GetValue(entry)!;

        cachedResults.GetType()
            .GetProperty("CachedAt", BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(cachedResults, cachedAt);
    }
}
