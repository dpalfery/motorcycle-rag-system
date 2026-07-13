using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Tests.Api.Configuration;

public class TelemetryConfigurationValidatorTests
{
    private readonly IValidateOptions<TelemetryOptions> _validator = new TelemetryConfigurationValidator();

    [Fact]
    public void Validate_Valid_Succeeds()
        => _validator.Validate(null, new TelemetryOptions { EnableTelemetry = true, ConnectionString = "c", ApplicationName = "app" }).Succeeded.Should().BeTrue();

    [Fact]
    public void Validate_TelemetryEnabledButNoConnectionString_Fails()
        => _validator.Validate(null, new TelemetryOptions { EnableTelemetry = true, ConnectionString = "", ApplicationName = "app" }).Failed.Should().BeTrue();

    [Fact]
    public void Validate_TelemetryDisabledNoConnectionString_Succeeds()
        => _validator.Validate(null, new TelemetryOptions { EnableTelemetry = false, ConnectionString = "", ApplicationName = "app" }).Succeeded.Should().BeTrue();

    [Fact]
    public void Validate_MissingApplicationName_Fails()
        => _validator.Validate(null, new TelemetryOptions { EnableTelemetry = false, ApplicationName = "" }).Failed.Should().BeTrue();
}
