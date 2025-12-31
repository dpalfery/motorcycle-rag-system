using System.Globalization;
using MotorcycleRAG.Admin.Converters;
using Xunit;

namespace MotorcycleRAG.UnitTests.Admin;

public class InverseBoolConverterTests
{
    private readonly InverseBoolConverter _converter = new();

    [Fact]
    public void Convert_TrueValue_ReturnsFalse()
    {
        // Act
        var result = _converter.Convert(true, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_FalseValue_ReturnsTrue()
    {
        // Act
        var result = _converter.Convert(false, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_NullValue_ReturnsFalse()
    {
        // Act
        var result = _converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_NonBoolValue_ReturnsFalse()
    {
        // Act
        var result = _converter.Convert("string", typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_TrueValue_ReturnsFalse()
    {
        // Act
        var result = _converter.ConvertBack(true, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_FalseValue_ReturnsTrue()
    {
        // Act
        var result = _converter.ConvertBack(false, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(true, result);
    }
}

public class StringNotEmptyConverterTests
{
    private readonly StringNotEmptyConverter _converter = new();

    [Fact]
    public void Convert_ValidString_ReturnsTrue()
    {
        // Act
        var result = _converter.Convert("Hello World", typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_EmptyString_ReturnsFalse()
    {
        // Act
        var result = _converter.Convert(string.Empty, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_WhitespaceString_ReturnsFalse()
    {
        // Act
        var result = _converter.Convert("   ", typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_NullString_ReturnsFalse()
    {
        // Act
        var result = _converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_NonStringValue_ReturnsFalse()
    {
        // Act
        var result = _converter.Convert(123, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        // Act & Assert
        Assert.Throws<NotImplementedException>(() => 
            _converter.ConvertBack(true, typeof(string), null, CultureInfo.InvariantCulture));
    }
}

public class PercentageConverterTests
{
    private readonly PercentageConverter _converter = new();

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(50.0, 0.5)]
    [InlineData(100.0, 1.0)]
    [InlineData(25.5, 0.255)]
    [InlineData(75.8, 0.758)]
    public void Convert_DoublePercentage_ReturnsCorrectDecimal(double input, double expected)
    {
        // Act
        var result = _converter.Convert(input, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, (double)result!, 3);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1.0)]
    public void Convert_IntPercentage_ReturnsCorrectDecimal(int input, double expected)
    {
        // Act
        var result = _converter.Convert(input, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, (double)result!);
    }

    [Fact]
    public void Convert_NullValue_ReturnsZero()
    {
        // Act
        var result = _converter.Convert(null, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Convert_NonNumericValue_ReturnsZero()
    {
        // Act
        var result = _converter.Convert("string", typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(0.0, result);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 50.0)]
    [InlineData(1.0, 100.0)]
    [InlineData(0.255, 25.5)]
    public void ConvertBack_DecimalValue_ReturnsCorrectPercentage(double input, double expected)
    {
        // Act
        var result = _converter.ConvertBack(input, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, (double)result!);
    }

    [Fact]
    public void ConvertBack_NullValue_ReturnsZero()
    {
        // Act
        var result = _converter.ConvertBack(null, typeof(double), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(0.0, result);
    }
}

public class HasValueConverterTests
{
    private readonly HasValueConverter _converter = new();

    [Fact]
    public void Convert_NullValue_ReturnsFalse()
    {
        // Act
        var result = _converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_NonNullValue_ReturnsTrue()
    {
        // Act
        var result = _converter.Convert("string", typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_NullableWithValue_ReturnsTrue()
    {
        // Arrange
        int? nullable = 42;

        // Act
        var result = _converter.Convert(nullable, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_EmptyString_ReturnsTrue()
    {
        // Act - empty string is still a value, not null
        var result = _converter.Convert(string.Empty, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_ZeroInteger_ReturnsTrue()
    {
        // Act - zero is still a value
        var result = _converter.Convert(0, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(true, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        // Act & Assert
        Assert.Throws<NotImplementedException>(() => 
            _converter.ConvertBack(true, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
