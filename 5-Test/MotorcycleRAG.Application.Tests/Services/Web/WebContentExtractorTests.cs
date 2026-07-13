using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services.Web;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Web;

public class WebContentExtractorTests
{
    private readonly WebContentExtractor _sut = new WebContentExtractor(NullLogger<WebContentExtractor>.Instance);

    [Fact]
    public void ExtractFromHtml_WithValidParagraph_ExtractsText()
    {
        var html = "<html><body><p>This is a test paragraph with enough length to be extracted.</p></body></html>";
        var result = _sut.ExtractFromHtml(html, "//p");

        result.Should().HaveCount(1);
        result.First().Text.Should().Be("This is a test paragraph with enough length to be extracted.");
    }



    [Fact]
    public void ExtractFromHtml_TruncatesLongText()
    {
        var html = $"<html><body><p>{new string('A', 600)}</p></body></html>";
        var result = _sut.ExtractFromHtml(html, "//p");

        result.Should().HaveCount(1);
        result.First().Text.Length.Should().Be(503); // 500 + "..."
    }

    [Fact]
    public async Task ExtractContentAsync_ExtractsText()
    {
        var html = "<html><body><p>This is a test paragraph with enough length to be extracted.</p></body></html>";
        var result = await _sut.ExtractContentAsync(html);

        result.Text.Should().Be("This is a test paragraph with enough length to be extracted.");
    }
}
