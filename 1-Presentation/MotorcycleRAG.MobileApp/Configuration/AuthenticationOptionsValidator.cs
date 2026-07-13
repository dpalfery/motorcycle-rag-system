using Microsoft.Extensions.Options;

namespace MotorcycleRAG.MobileApp.Configuration;

/// <summary>
/// Validates the configuration used to compose mobile MSAL authentication.
/// </summary>
public sealed class AuthenticationOptionsValidator : IValidateOptions<AuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Authentication:ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.TenantId))
        {
            failures.Add("Authentication:TenantId is required.");
        }

        if (!Uri.TryCreate(options.RedirectUri, UriKind.Absolute, out _))
        {
            failures.Add("Authentication:RedirectUri must be an absolute URI.");
        }

        if (options.Scopes is null || options.Scopes.Length == 0 || options.Scopes.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Authentication:Scopes must contain one or more non-empty scopes.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
