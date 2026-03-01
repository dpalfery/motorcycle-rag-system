// TDD-RED: This file references BikeModel which does not yet exist.
// Tests will fail to compile until MotorcycleRAG.Domain.Entities.BikeModel is created.

using FluentAssertions;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Unit tests for BikeModel normalization and alias-matching static methods.
/// These tests FAIL TO COMPILE until BikeModel is implemented.
/// This is the correct TDD-RED state: the tests define the expected behavior contract.
/// </summary>
public class BikeModelNormalizationTests
{
    [Fact]
    public void NormalizeName_HondaCbr1000rr_TitleCasesMakeAndUppercasesModel()
    {
        // Arrange & Act
        var result = BikeModel.NormalizeName("honda CBR 1000rr");

        // Assert
        result.Should().Be("Honda CBR 1000RR");
    }

    [Fact]
    public void NormalizeName_YamahaYzfR1_TitleCasesMakeAndUppercasesModel()
    {
        // Arrange & Act
        var result = BikeModel.NormalizeName("YAMAHA yzf-r1");

        // Assert
        result.Should().Be("Yamaha YZF-R1");
    }

    [Fact]
    public void IsAlias_Cbr1000rrWithSpace_ReturnsTrueStripsSpaces()
    {
        // Arrange & Act
        var result = BikeModel.IsAlias("CBR1000RR", "CBR 1000RR");

        // Assert
        result.Should().BeTrue("alias matching strips spaces and is case-insensitive");
    }

    [Fact]
    public void IsAlias_R1VsYzfR1_ReturnsFalseUnrelated()
    {
        // Arrange & Act
        var result = BikeModel.IsAlias("R1", "YZF-R1");

        // Assert
        result.Should().BeFalse("R1 and YZF-R1 are not the same after space stripping");
    }

    [Fact]
    public void NormalizeName_Null_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => BikeModel.NormalizeName(null);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}