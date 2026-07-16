using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Web;

namespace MotorcycleRAG.Persistence.Tests.Web;

public sealed class HtmlWebContentExtractorTests
{
    private static TrustedSourceOptions DefaultSource => new()
    {
        Name = "Test Source",
        BaseUrl = new Uri("https://example.com"),
        ContentSelector = "//article",
    };

    // ─────────────────────────────────────────────────────────────────
    // Constructor / interface
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Implements_IWebContentExtractor()
    {
        var sut = new HtmlWebContentExtractor();
        sut.Should().BeAssignableTo<IWebContentExtractor>();
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — argument validation
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenHtmlContentIsNull_ThrowsArgumentNullException()
    {
        var sut = new HtmlWebContentExtractor();

        var act = () => sut.Extract(null!, "Honda", DefaultSource);

        act.Should().Throw<ArgumentNullException>().WithParameterName("htmlContent");
    }

    [Fact]
    public void Extract_WhenSearchTermIsNull_ThrowsArgumentNullException()
    {
        var sut = new HtmlWebContentExtractor();

        var act = () => sut.Extract("<p>text</p>", null!, DefaultSource);

        act.Should().Throw<ArgumentNullException>().WithParameterName("searchTerm");
    }

    [Fact]
    public void Extract_WhenSourceIsNull_ThrowsArgumentNullException()
    {
        var sut = new HtmlWebContentExtractor();

        var act = () => sut.Extract("<p>text</p>", "Honda", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("source");
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — empty input
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenHtmlContentIsEmpty_ReturnsEmptyString()
    {
        var sut = new HtmlWebContentExtractor();

        var result = sut.Extract(string.Empty, "Honda", DefaultSource);

        result.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — happy path: motorcycle keyword match
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenContentMatchesMotorcycleKeyword_ReturnsExtractedText()
    {
        var sut = new HtmlWebContentExtractor();
        var html = "<article>The Honda motorcycle engine produces strong torque at low RPM.</article>";

        var result = sut.Extract(html, "Honda", DefaultSource);

        result.Should().Be("The Honda motorcycle engine produces strong torque at low RPM.");
    }

    [Fact]
    public void Extract_WhenContentMatchesDifferentKeyword_ReturnsExtractedText()
    {
        var sut = new HtmlWebContentExtractor();
        var html = "<p>The ducati bike has excellent handling characteristics.</p>";

        var result = sut.Extract(html, "any search", DefaultSource);

        result.Should().Be("The ducati bike has excellent handling characteristics.");
    }

    [Fact]
    public void Extract_WhenContentMatchesSearchTermWord_ReturnsExtractedText()
    {
        var sut = new HtmlWebContentExtractor();
        // "chain" is not a motorcycle keyword, but it's a search word > 2 chars
        var html = "<p>How to adjust the chain tension properly.</p>";

        var result = sut.Extract(html, "chain adjustment", DefaultSource);

        result.Should().Be("How to adjust the chain tension properly.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — relevance filtering
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenContentIsTooShortAndFallbackTooShort_ReturnsEmptyString()
    {
        var sut = new HtmlWebContentExtractor();
        // < 20 chars, no keyword match — filtered out; fallback from body is also ≤ 50 chars
        var html = "<html><body><p>Hi</p></body></html>";

        var result = sut.Extract(html, "xyz", DefaultSource);

        // The <p> matches but content is filtered; document body fallback "Hi" ≤ 50 → empty
        result.Should().BeEmpty();
    }

    [Fact]
    public void Extract_WhenParagraphContentIsIrrelevant_FallsBackToBody()
    {
        var sut = new HtmlWebContentExtractor();
        // <p> matches selector but content fails relevance (no keyword, search word ≤ 2 chars)
        var bodyContent = "This page contains various general information about vehicles and their maintenance schedules.";
        var html = $"<html><body><p>boop</p>{bodyContent}</body></html>";

        var result = sut.Extract(html, "xy", DefaultSource);

        // Paragraph filtered out; falls back to body text (which is > 50 chars → returned, max 500)
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("This page contains");
    }

    [Fact]
    public void Extract_WhenParagraphFilteredAndFallbackTooShort_ReturnsEmptyString()
    {
        var sut = new HtmlWebContentExtractor();
        // Paragraph is < 20 chars, body content ≤ 50 chars → fallback returns empty
        var html = "<html><body><p>nope</p>xy</body></html>";

        var result = sut.Extract(html, "aa", DefaultSource);

        result.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — content selector behavior
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenCustomSelectorDoesNotMatch_FallsBackToDefaults()
    {
        var sut = new HtmlWebContentExtractor();
        var source = new TrustedSourceOptions
        {
            Name = "Custom",
            BaseUrl = new Uri("https://example.com"),
            ContentSelector = "//div[@class='motorcycle-content']",
        };
        // The custom selector won't match this HTML, so the default selectors kick in.
        var html = "<div class='other'><p>The honda CBR is a great bike.</p></div>";

        var result = sut.Extract(html, "Honda", source);

        result.Should().Contain("honda CBR");
    }

    [Fact]
    public void Extract_WhenAllParagraphsAreIrrelevant_FallsBackToBody()
    {
        var sut = new HtmlWebContentExtractor();
        // <p> selector matches but content fails IsRelevantContent — triggers fallback
        var bodyContent = "General motorcycle specifications and engine details for the new model.";
        var html = $"<html><body><p>nope</p>{bodyContent}</body></html>";

        var result = sut.Extract(html, "zz", new TrustedSourceOptions
        {
            Name = "None",
            BaseUrl = new Uri("https://example.com"),
            ContentSelector = "//nonexistent",
        });

        // Custom selector doesn't match, <p> matches but filtered out, falls back to body
        result.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — long text truncation via ExtractCleanText (>500 chars)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenTextExceeds500Chars_TruncatesAndAppendsEllipsis()
    {
        var sut = new HtmlWebContentExtractor();
        var longContent = new string('A', 600);
        var html = $"<article>The honda motorcycle {longContent}.</article>";

        var result = sut.Extract(html, "Honda", DefaultSource);

        result.Should().EndWith("...");
        result.Length.Should().BeLessThanOrEqualTo(503); // 500 + "..."
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — fallback content > 50 chars → truncated to max 500
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenFallbackExceeds500Chars_TruncatesTo500()
    {
        var sut = new HtmlWebContentExtractor();
        var longContent = new string('Z', 800);
        // <p> matches but content is < 20 chars and no keyword → filtered; fallback from body
        var html = $"<html><body><p>no</p>{longContent}</body></html>";

        var result = sut.Extract(html, "aa", DefaultSource);

        // Fallback text is > 50 chars but limited to 500 via AsSpan
        result.Length.Should().BeLessThanOrEqualTo(500);
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — multiple results joined
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenMultipleParagraphsMatch_JoinsThem()
    {
        var sut = new HtmlWebContentExtractor();
        var html = "<main>" +
                   "<p>Honda motorcycles are known for reliability.</p>" +
                   "<p>Yamaha motorcycles offer great performance.</p>" +
                   "<p>Kawasaki bikes have strong engines.</p>" +
                   "</main>";

        var result = sut.Extract(html, "motorcycle", new TrustedSourceOptions
        {
            Name = "Multi",
            BaseUrl = new Uri("https://example.com"),
            ContentSelector = "//p",
        });

        result.Should().Contain("Honda");
        result.Should().Contain("Yamaha");
        result.Should().Contain("Kawasaki");
        result.Should().Contain("\n\n");
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — null ContentSelector uses defaults only
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenContentSelectorIsNull_UsesDefaultsOnly()
    {
        var sut = new HtmlWebContentExtractor();
        var html = "<p>The suzuki GSX-R has a powerful engine.</p>";

        var result = sut.Extract(html, "Suzuki", new TrustedSourceOptions
        {
            Name = "Default",
            BaseUrl = new Uri("https://example.com"),
            ContentSelector = null!,
        });

        result.Should().Contain("suzuki");
    }

    [Fact]
    public void Extract_WhenContentSelectorIsEmpty_UsesDefaultsOnly()
    {
        var sut = new HtmlWebContentExtractor();
        var html = "<p>The bmw S1000RR engine produces impressive horsepower.</p>";

        var result = sut.Extract(html, "BMW", new TrustedSourceOptions
        {
            Name = "Empty",
            BaseUrl = new Uri("https://example.com"),
            ContentSelector = string.Empty,
        });

        result.Should().Contain("bmw");
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — whitespace normalization (ExtractCleanText)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenContentHasExtraWhitespace_NormalizesIt()
    {
        var sut = new HtmlWebContentExtractor();
        var html = "<article>The   honda   motorcycle    engine\n\n\nproduces    torque.</article>";

        var result = sut.Extract(html, "Honda", DefaultSource);

        result.Should().Be("The honda motorcycle engine produces torque.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Extract — up to 5 results (Take(5))
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_WhenMoreThanFiveParagraphs_FiltersToFive()
    {
        var sut = new HtmlWebContentExtractor();
        var paragraphs = string.Join("", Enumerable.Range(1, 10)
            .Select(i => $"<p>Honda motorcycle paragraph {i} with specifications.</p>"));
        var html = $"<div>{paragraphs}</div>";

        var result = sut.Extract(html, "Honda", new TrustedSourceOptions
        {
            Name = "Many",
            BaseUrl = new Uri("https://example.com"),
            ContentSelector = "//p",
        });

        // Should have 5 results joined by \n\n
        var parts = result.Split("\n\n");
        parts.Should().HaveCount(5);
    }
}
