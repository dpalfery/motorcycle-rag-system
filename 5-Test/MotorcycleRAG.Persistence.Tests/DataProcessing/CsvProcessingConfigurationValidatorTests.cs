using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.DataProcessing;

namespace MotorcycleRAG.Persistence.Tests.DataProcessing;

public sealed class CsvProcessingConfigurationValidatorTests
{
    private readonly CsvProcessingConfigurationValidator _validator = new();

    [Fact]
    public void Validate_WhenMaxRowsIsMissing_ReturnsFailure()
    {
        var result = _validator.Validate(null, new CSVProcessingConfiguration
        {
            ChunkSize = 50,
            Delimiter = ',',
        });

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("MaxRows"));
    }

    [Fact]
    public void Validate_WhenChunkSizeIsInvalid_ReturnsFailure()
    {
        var result = _validator.Validate(null, new CSVProcessingConfiguration
        {
            MaxRows = 1000,
            ChunkSize = 0,
            Delimiter = ',',
        });

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("ChunkSize"));
    }

    [Fact]
    public void AddAzureServices_WhenCsvProcessingIsConfigured_ResolvesValidatedOptions()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sql:ConnectionString"] = "Server=.;Database=test",
            ["CsvProcessing:Delimiter"] = ",",
            ["CsvProcessing:ChunkSize"] = "50",
            ["CsvProcessing:MaxRows"] = "1000",
        }).Build();
        services.AddAzureServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CSVProcessingConfiguration>>().Value;

        options.MaxRows.Should().Be(1000);
        options.ChunkSize.Should().Be(50);
        options.Delimiter.Should().Be(',');
    }
}
