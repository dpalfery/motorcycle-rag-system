using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Validator for Search configuration
/// </summary>
internal class SearchConfigurationValidator : IValidateOptions<SearchOptions>
{
    ValidateOptionsResult IValidateOptions<SearchOptions>.Validate(string? name, SearchOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.IndexName))
            failures.Add("Search:IndexName is required");

        if (options.BatchSize <= 0 || options.BatchSize > 1000)
            failures.Add("Search:BatchSize must be between 1 and 1000");

        if (options.MaxSearchResults <= 0 || options.MaxSearchResults > 100)
            failures.Add("Search:MaxSearchResults must be between 1 and 100");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}