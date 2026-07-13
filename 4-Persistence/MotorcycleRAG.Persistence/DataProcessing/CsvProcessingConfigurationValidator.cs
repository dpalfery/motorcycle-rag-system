using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Persistence.DataProcessing;

/// <summary>
/// Validates the configuration required by <see cref="MotorcycleCsvProcessor"/>.
/// </summary>
public sealed class CsvProcessingConfigurationValidator : IValidateOptions<CSVProcessingConfiguration>
{
    public ValidateOptionsResult Validate(string? name, CSVProcessingConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.MaxRows <= 0)
        {
            failures.Add("CsvProcessing:MaxRows must be greater than zero.");
        }

        if (options.ChunkSize <= 0)
        {
            failures.Add("CsvProcessing:ChunkSize must be greater than zero.");
        }

        if (options.Delimiter == '\0')
        {
            failures.Add("CsvProcessing:Delimiter must be configured.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
