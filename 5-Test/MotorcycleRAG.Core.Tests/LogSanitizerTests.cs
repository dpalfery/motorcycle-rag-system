using FluentAssertions;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Core.Tests;

public class LogSanitizerTests
{
    [Fact]
    public void Sanitize_WhenStringValueIsNull_ReturnsEmptyString()
    {
        // Act
        var result = LogSanitizer.Sanitize((string?)null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_WhenObjectValueIsNull_ReturnsEmptyString()
    {
        // Act
        var result = LogSanitizer.Sanitize((object?)null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_WhenValueContainsPrintableUnicode_PreservesTheFullValueWithoutDefaultTruncation()
    {
        // Arrange
        var input = $"Motorcycle 東京 é⚡😀 {new string('x', 250)}";

        // Act
        var result = LogSanitizer.Sanitize(input);

        // Assert
        result.Should().Be(input);
    }

    [Fact]
    public void Sanitize_WhenValueContainsBackslashBeforeLineFeed_EscapesTheBackslashFirst()
    {
        // Arrange
        const string input = "path\\manual\nnext";

        // Act
        var result = LogSanitizer.Sanitize(input);

        // Assert
        result.Should().Be("path\\\\manual\\nnext");
    }

    [Fact]
    public void Sanitize_WhenValueContainsCrLfTabNulC0AndC1Controls_UsesVisibleReversibleEscapes()
    {
        // Arrange
        const string input = "before\\after\r\n\t\0\u0001\u001F\u007F\u0085\u009Fafter";

        // Act
        var result = LogSanitizer.Sanitize(input);

        // Assert
        result.Should().Be("before\\\\after\\r\\n\\t\\0\\u0001\\u001F\\u007F\\u0085\\u009Fafter");
    }

    [Fact]
    public void Sanitize_WhenValueContainsControlCharacters_ReturnsNoRawControlCharacters()
    {
        // Arrange
        const string input = "\r\n\t\0\u0001\u001F\u007F\u0085\u009F";

        // Act
        var result = LogSanitizer.Sanitize(input);

        // Assert
        result.Any(char.IsControl).Should().BeFalse();
    }
}
