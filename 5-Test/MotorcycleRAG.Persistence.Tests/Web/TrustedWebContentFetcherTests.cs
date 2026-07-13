using System.Net;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Persistence.Web;

namespace MotorcycleRAG.Persistence.Tests.Web;

public sealed class TrustedWebContentFetcherTests
{
    [Fact]
    public void Extract_WhenRelevantTrustedContentExists_ReturnsItsText()
    {
        var sut = new HtmlWebContentExtractor();
        var source = new Core.Options.TrustedSourceOptions
        {
            Name = "Example",
            BaseUrl = new Uri("https://example.com"),
            ContentSelector = "//article",
        };

        var result = sut.Extract("<article>The Honda motorcycle engine produces strong torque.</article>", "Honda", source);

        result.Should().Be("The Honda motorcycle engine produces strong torque.");
    }

    [Fact]
    public async Task FetchAsync_WhenHtmlIsReturned_DelegatesExtractionWithTheTrustedSource()
    {
        var extractor = new Mock<IWebContentExtractor>(MockBehavior.Strict);
        extractor
            .Setup(value => value.Extract("<article>Honda content</article>", "Honda", It.IsAny<Core.Options.TrustedSourceOptions>()))
            .Returns("Honda content");
        using var client = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, "<article>Honda content</article>"));
        var sut = new TrustedWebContentFetcher(client, extractor.Object);
        var sourceUrl = new Uri("https://example.com/honda");

        var result = await sut.FetchAsync(sourceUrl, "Honda", CancellationToken.None);

        result.Should().Be("Honda content");
        extractor.Verify(value => value.Extract(
            "<article>Honda content</article>",
            "Honda",
            It.Is<Core.Options.TrustedSourceOptions>(source =>
                source.Name == sourceUrl.ToString()
                && source.BaseUrl == sourceUrl
                && source.SearchUrlTemplate == sourceUrl
                && source.ContentSelector == "//p|//article|//div[@class='content']")),
            Times.Once);
    }

    [Fact]
    public async Task FetchAsync_WhenHttpRequestFails_PropagatesTheFailure()
    {
        using var client = new HttpClient(new StaticResponseHandler(HttpStatusCode.ServiceUnavailable, "unavailable"));
        var sut = new TrustedWebContentFetcher(client, Mock.Of<IWebContentExtractor>());

        var act = () => sut.FetchAsync(new Uri("https://example.com/honda"), "Honda", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content),
            });
    }
}
