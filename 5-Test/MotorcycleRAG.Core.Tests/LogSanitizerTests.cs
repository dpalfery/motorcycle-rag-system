using MotorcycleRAG.Core.Utilities;
using Xunit;

namespace MotorcycleRAG.Core.Tests;

public class LogSanitizerTests
{
    [Fact]
    public void Sanitize_String_WithNull_ReturnsEmptyString()
    {
        var result = LogSanitizer.Sanitize((string?)null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_Object_WithNull_ReturnsEmptyString()
    {
        var result = LogSanitizer.Sanitize((object?)null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_String_WithoutControlCharacters_ReturnsSameString()
    {
        var input = "valid log message";
        var result = LogSanitizer.Sanitize(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Sanitize_String_WithControlCharacters_ReplacesThemWithSpaces()
    {
        var input = "line1\nline2\rline3\ttab\0null";
        var result = LogSanitizer.Sanitize(input);
        Assert.Equal("line1 line2 line3 tab null", result);
    }

    [Fact]
    public void Sanitize_String_LongerThanMaxLength_TruncatesString()
    {
        var input = new string('A', 250);
        var result = LogSanitizer.Sanitize(input, 100);
        Assert.Equal(100, result.Length);
        Assert.Equal(new string('A', 100), result);
    }

    [Fact]
    public void Sanitize_Object_CallsToStringAndSanitizes()
    {
        var input = new Exception("test\nexception");
        var result = LogSanitizer.Sanitize(input);
        Assert.Contains("test exception", result);
        Assert.DoesNotContain("\n", result);
    }
}
