using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Validator for SQL configuration options
/// Note: Connection string must be provided through Azure App Configuration and Key Vault.
/// </summary>
internal class SqlOptionsValidator : IValidateOptions<SqlOptions>
{
    ValidateOptionsResult IValidateOptions<SqlOptions>.Validate(string? name, SqlOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            failures.Add("Sql:ConnectionString is required. In Development add it to user secrets; in Production it must come from Azure App Configuration + Key Vault.");

        if (options.CommandTimeout <= 0)
            failures.Add("Sql:CommandTimeout must be greater than 0");

        if (options.ConnectionTimeout <= 0)
            failures.Add("Sql:ConnectionTimeout must be greater than 0");

        if (options.MaxPoolSize <= 0)
            failures.Add("Sql:MaxPoolSize must be greater than 0");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
