using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services.Web;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Web;

public class WebSearchTermEnhancerTests
{
    private static readonly string[] DefaultTerms = ["query"];
    private readonly WebSearchTermEnhancer _sut = new WebSearchTermEnhancer(NullLogger<WebSearchTermEnhancer>.Instance);

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new WebSearchTermEnhancer(null!, NullLogger<WebSearchTermEnhancer>.Instance));
        Assert.Throws<ArgumentNullException>(() => new WebSearchTermEnhancer(new Mock<IOptions<WebSearchOptions>>().Object, null!));
        Assert.Throws<ArgumentNullException>(() => new WebSearchTermEnhancer(null!));
    }

    [Fact]
    public async Task GenerateSearchTermsAsync_ReturnsOriginalQuery()
    {
        var result = await _sut.GenerateSearchTermsAsync("query", CancellationToken.None);
        result.Should().BeEquivalentTo(DefaultTerms);
    }

    [Fact]
    public async Task EnhanceSearchTermsAsync_ReturnsOriginalQuery()
    {
        var result = await _sut.EnhanceSearchTermsAsync("query");
        result.Should().Be("query");
    }
}
