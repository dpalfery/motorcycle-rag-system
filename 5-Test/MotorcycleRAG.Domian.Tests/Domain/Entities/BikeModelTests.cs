using System;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.Domian.Tests.Domain.Entities;

public class BikeModelTests
{
    [Fact]
    public void NormalizeName_WithValidRawName_NormalizesCorrectly()
    {
        var raw = "honda cbr 1000rr";
        var normalized = BikeModel.NormalizeName(raw);
        Assert.Equal("Honda CBR 1000RR", normalized);
    }

    [Fact]
    public void NormalizeName_WithEmptyString_ReturnsEmpty()
    {
        var raw = "   ";
        var normalized = BikeModel.NormalizeName(raw);
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData(" \t\r\n ")]
    public void NormalizeName_WithWhitespaceOnly_ReturnsEmpty(string raw)
    {
        var normalized = BikeModel.NormalizeName(raw);

        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void NormalizeName_WithLeadingAndTrailingTabAndNewline_NormalizesTokens()
    {
        var normalized = BikeModel.NormalizeName("\t honda cbr 1000rr \r\n");

        Assert.Equal("Honda CBR 1000RR", normalized);
    }

    [Fact]
    public void NormalizeName_WithInternalTab_PreservesTheExistingSingleTokenBehavior()
    {
        var normalized = BikeModel.NormalizeName("honda\tcbr 1000rr");

        Assert.Equal("Honda\tcbr 1000RR", normalized);
    }

    [Fact]
    public void NormalizeName_WithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BikeModel.NormalizeName(null!));
    }

    [Fact]
    public void NormalizeName_SingleToken_NormalizesCorrectly()
    {
        var raw = "hONDa";
        var normalized = BikeModel.NormalizeName(raw);
        Assert.Equal("Honda", normalized);
    }

    [Fact]
    public void IsAlias_WithMatchingAliases_ReturnsTrue()
    {
        Assert.True(BikeModel.IsAlias("CBR1000RR", "CBR 1000RR"));
        Assert.True(BikeModel.IsAlias("  cbr1000rr  ", "CBR 1000RR"));
    }

    [Fact]
    public void IsAlias_WithNonMatchingAliases_ReturnsFalse()
    {
        Assert.False(BikeModel.IsAlias("CBR1000", "CBR 1000RR"));
    }

    [Fact]
    public void IsAlias_WithNull_ReturnsFalse()
    {
        Assert.False(BikeModel.IsAlias(null!, "canonical"));
        Assert.False(BikeModel.IsAlias("candidate", null!));
    }

    [Fact]
    public void Create_WithValidIdentity_TrimsValuesAndProducesCanonicalName()
    {
        // Act
        var model = BikeModel.Create(
            " Honda ",
            " cbr 1000rr ",
            2024,
            aliases: "Fireblade",
            createdByUserId: "user-1",
            uploadRef: "upload-1");

        // Assert
        Assert.Equal("Honda", model.Make);
        Assert.Equal("cbr 1000rr", model.Model);
        Assert.Equal("Honda CBR 1000RR", model.NormalizedName);
        Assert.Equal(2024, model.Year);
        Assert.Equal("Fireblade", model.Aliases);
        Assert.Equal("user-1", model.CreatedByUserId);
        Assert.Equal("upload-1", model.UploadRef);
    }

    [Theory]
    [InlineData(null, "CBR 1000RR", "make")]
    [InlineData("   ", "CBR 1000RR", "make")]
    [InlineData("Honda", null, "model")]
    [InlineData("Honda", "   ", "model")]
    public void Create_WhenIdentityFieldIsMissing_RejectsTheModel(
        string? make,
        string? model,
        string expectedParameterName)
    {
        // Act
        var exception = Assert.ThrowsAny<ArgumentException>(() => BikeModel.Create(make!, model!, 2024));

        // Assert
        Assert.Equal(expectedParameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WhenYearIsNotPositive_RejectsTheModel(int year)
    {
        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => BikeModel.Create("Honda", "CBR 1000RR", year));

        // Assert
        Assert.Equal("year", exception.ParamName);
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        var model = new BikeModel
        {
            Make = "Honda",
            Model = "CBR 1000RR",
            Year = 2024,
            Aliases = "Fireblade",
            CreatedByUserId = "User1",
            UploadRef = "Batch1"
        };

        Assert.NotEqual(Guid.Empty, model.Id);
        Assert.Equal("Honda", model.Make);
        Assert.Equal("CBR 1000RR", model.Model);
        Assert.Equal(2024, model.Year);
        Assert.Equal("Fireblade", model.Aliases);
        Assert.Equal("Honda CBR 1000RR", model.NormalizedName);
        Assert.Equal("User1", model.CreatedByUserId);
        Assert.Equal("Batch1", model.UploadRef);
        Assert.True(model.CreatedAtUtc <= DateTimeOffset.UtcNow);
        Assert.True(model.UpdatedAtUtc <= DateTimeOffset.UtcNow);
    }
}
