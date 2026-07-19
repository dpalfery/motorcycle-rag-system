using FluentAssertions;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Core.Tests;

public class PiiMaskingTests
{
    [Fact]
    public void MaskEmailForLog_WhenEmailIsNull_ReturnsEmptyString()
    {
        // Act
        var result = PiiMasking.MaskEmailForLog(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskEmailForLog_WhenEmailIsEmptyOrWhitespace_ReturnsEmptyString(string email)
    {
        // Act
        var result = PiiMasking.MaskEmailForLog(email);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void MaskEmailForLog_WhenEmailHasNoAtSymbol_ReturnsRedacted()
    {
        // Act
        var result = PiiMasking.MaskEmailForLog("not-an-email");

        // Assert
        result.Should().Be("[redacted]");
    }

    [Fact]
    public void MaskEmailForLog_WhenAtSymbolIsFirstCharacter_ReturnsRedacted()
    {
        // Act
        var result = PiiMasking.MaskEmailForLog("@example.com");

        // Assert
        result.Should().Be("[redacted]");
    }

    [Fact]
    public void MaskEmailForLog_WhenAtSymbolIsLastCharacter_ReturnsRedacted()
    {
        // Act
        var result = PiiMasking.MaskEmailForLog("user@");

        // Assert
        result.Should().Be("[redacted]");
    }

    [Fact]
    public void MaskEmailForLog_WhenEmailIsWellFormed_MasksLocalPartKeepingFirstCharacterAndFullDomain()
    {
        // Act
        var result = PiiMasking.MaskEmailForLog("john@example.com");

        // Assert
        result.Should().Be("j***@example.com");
    }

    [Fact]
    public void MaskEmailForLog_WhenLocalPartIsSingleCharacter_MasksUsingThatCharacter()
    {
        // Act
        var result = PiiMasking.MaskEmailForLog("a@example.com");

        // Assert
        result.Should().Be("a***@example.com");
    }
}
