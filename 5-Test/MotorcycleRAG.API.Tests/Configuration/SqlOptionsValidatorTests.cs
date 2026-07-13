using FluentAssertions;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration;

public class SqlOptionsValidatorTests {
    private readonly SqlOptionsValidator _sut = new();

    private static SqlOptions ValidOptions() => new() {
        ConnectionString = "Server=localhost;Database=Test;User Id=sa;Password=pass;",
        CommandTimeout = 60,
        ConnectionTimeout = 30,
        MaxPoolSize = 100
    };

    [Fact]
    public void Validate_ValidOptions_Succeeds() {
        var result = ((IValidateOptions<SqlOptions>)_sut).Validate(null, ValidOptions());
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingConnectionString_Fails(string? value) {
        var options = ValidOptions();
        options.ConnectionString = value!;

        var result = ((IValidateOptions<SqlOptions>)_sut).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Sql:ConnectionString is required");
    }

    [Fact]
    public void Validate_ZeroCommandTimeout_Fails() {
        var options = ValidOptions();
        options.CommandTimeout = 0;

        var result = ((IValidateOptions<SqlOptions>)_sut).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("CommandTimeout");
    }

    [Fact]
    public void Validate_ZeroConnectionTimeout_Fails() {
        var options = ValidOptions();
        options.ConnectionTimeout = 0;

        var result = ((IValidateOptions<SqlOptions>)_sut).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ConnectionTimeout");
    }

    [Fact]
    public void Validate_ZeroMaxPoolSize_Fails() {
        var options = ValidOptions();
        options.MaxPoolSize = 0;

        var result = ((IValidateOptions<SqlOptions>)_sut).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxPoolSize");
    }
}
