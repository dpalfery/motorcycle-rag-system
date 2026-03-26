using System.Security.Claims;

namespace MotorcycleRAG.API.Extensions;

/// <summary>
/// Extension methods for <see cref="ClaimsPrincipal"/> to facilitate authorization checks.
/// </summary>
internal static class ClaimsPrincipalExtensions {
    /// <summary>
    /// Checks if the user has the required scope.
    /// </summary>
    /// <param name="user">The claims principal.</param>
    /// <param name="requiredScope">The required scope.</param>
    /// <returns>True if the user has the scope, otherwise false.</returns>
    internal static bool HasScope(this ClaimsPrincipal user, string requiredScope) {
        if (user == null) {
            return false;
        }

        // Check for 'scp' claim type OR the full XML schema claim type for scopes
        var scpClaims = user.FindAll("scp").Select(c => c.Value);
        var schemaClaims = user.FindAll("http://schemas.microsoft.com/identity/claims/scope").Select(c => c.Value);

        var scopeClaims = scpClaims.Concat(schemaClaims);

        foreach (var scopeClaim in scopeClaims) {
            var scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (scopes.Any(s => string.Equals(s, requiredScope, StringComparison.OrdinalIgnoreCase))) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the user has any of the specified roles.
    /// </summary>
    /// <param name="user">The claims principal.</param>
    /// <param name="roles">The allowed roles.</param>
    /// <returns>True if the user has any of the roles, otherwise false.</returns>
    internal static bool HasAnyRole(this ClaimsPrincipal user, params string[] roles) {
        if (user == null) {
            return false;
        }

        // Check standard Role claim type AND "roles" claim type (common in Entra ID access tokens)
        var roleClaims = user.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Concat(user.FindAll("roles").Select(c => c.Value));

        return roleClaims.Any(role => roles.Any(allowed =>
            string.Equals(role, allowed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Validates if the client ID in the token matches the expected client ID.
    /// </summary>
    /// <param name="user">The claims principal.</param>
    /// <param name="expectedClientId">The expected client ID.</param>
    /// <returns>True if the client ID matches, otherwise false.</returns>
    internal static bool IsAuthorizedClient(this ClaimsPrincipal user, string expectedClientId) {
        if (user == null || string.IsNullOrEmpty(expectedClientId)) {
            return false;
        }

        // azp (Authorized Party) claim contains the client ID of the app that requested the token
        var azp = user.FindFirst("azp")?.Value;
        return string.Equals(azp, expectedClientId, StringComparison.OrdinalIgnoreCase);
    }
}
