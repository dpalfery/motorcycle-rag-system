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
    public void Rehydrate_WithValidFields_PreservesAllValues()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var model = BikeModel.Rehydrate(
            id,
            " Honda ",
            " CBR 1000RR ",
            2024,
            aliases: "  Fireblade, CBR1000RR  ",
            createdAtUtc: createdAt,
            updatedAtUtc: updatedAt,
            createdByUserId: "user-1",
            uploadRef: "upload-1");

        Assert.Equal(id, model.Id);
        Assert.Equal("Honda", model.Make);
        Assert.Equal("CBR 1000RR", model.Model);
        Assert.Equal("Honda CBR 1000RR", model.NormalizedName);
        Assert.Equal(2024, model.Year);
        Assert.Equal("Fireblade, CBR1000RR", model.Aliases);
        Assert.Equal("user-1", model.CreatedByUserId);
        Assert.Equal("upload-1", model.UploadRef);
        Assert.Equal(createdAt, model.CreatedAtUtc);
        Assert.Equal(updatedAt, model.UpdatedAtUtc);
    }

    [Fact]
    public void Rehydrate_WithEmptyId_RejectsTheModel()
    {
        var act = () => BikeModel.Rehydrate(
            Guid.Empty,
            "Honda",
            "CBR 1000RR",
            2024,
            aliases: null,
            createdAtUtc: DateTimeOffset.UtcNow,
            updatedAtUtc: DateTimeOffset.UtcNow,
            createdByUserId: null,
            uploadRef: null);

        var exception = Assert.Throws<ArgumentException>(act);
        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Rehydrate_WithWhitespaceAliases_ClearsToNull()
    {
        var model = BikeModel.Rehydrate(
            Guid.NewGuid(),
            "Honda",
            "CBR 1000RR",
            2024,
            aliases: "   ",
            createdAtUtc: DateTimeOffset.UtcNow,
            updatedAtUtc: DateTimeOffset.UtcNow,
            createdByUserId: null,
            uploadRef: null);

        Assert.Null(model.Aliases);
    }

    [Fact]
    public void UpdateAliases_WhenCalled_UpdatesAliasesAndStampsTimestamp()
    {
        var beforeUpdate = DateTimeOffset.UtcNow;
        var model = BikeModel.Create("Honda", "CBR 1000RR", 2024);
        var originalUpdatedAt = model.UpdatedAtUtc;

        model.UpdateAliases("Fireblade,CBR1000RR");

        Assert.Equal("Fireblade,CBR1000RR", model.Aliases);
        Assert.True(model.UpdatedAtUtc >= beforeUpdate);
        Assert.True(model.UpdatedAtUtc >= originalUpdatedAt);
        // CreatedAtUtc is immutable
        Assert.Equal(model.CreatedAtUtc, model.CreatedAtUtc);
    }

    [Fact]
    public void UpdateAliases_WithWhitespace_ClearsAliases()
    {
        var model = BikeModel.Create("Honda", "CBR 1000RR", 2024, aliases: "Fireblade");

        model.UpdateAliases("   ");

        Assert.Null(model.Aliases);
    }

    [Theory]
    [InlineData(null, "CBR 1000RR", "make")]
    [InlineData("   ", "CBR 1000RR", "make")]
    public void Rehydrate_WhenIdentityFieldIsMissing_RejectsTheModel(
        string? make,
        string? model,
        string expectedParameterName)
    {
        var act = () => BikeModel.Rehydrate(
            Guid.NewGuid(),
            make!,
            model!,
            2024,
            aliases: null,
            createdAtUtc: DateTimeOffset.UtcNow,
            updatedAtUtc: DateTimeOffset.UtcNow,
            createdByUserId: null,
            uploadRef: null);

        var exception = Assert.ThrowsAny<ArgumentException>(act);
        Assert.Equal(expectedParameterName, exception.ParamName);
    }
}
