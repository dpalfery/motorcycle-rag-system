using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.Services;

internal static class AdminAuthConfigurationHelper
{
    internal static string NormalizeAdminScope(string scope, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return scope;
        }

        static string ReplaceSuffix(string value, string suffix) =>
            value[..^suffix.Length] + "/admin";

        if (scope.EndsWith("/access_as_user", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = ReplaceSuffix(scope, "/access_as_user");
            logger.LogWarning("Normalizing legacy admin scope '{OriginalScope}' to '{NormalizedScope}'", scope, normalized);
            return normalized;
        }

        if (scope.EndsWith("/.default", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = ReplaceSuffix(scope, "/.default");
            logger.LogWarning("Normalizing broad admin scope '{OriginalScope}' to '{NormalizedScope}'", scope, normalized);
            return normalized;
        }

        if (scope.EndsWith("/admin_access", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = ReplaceSuffix(scope, "/admin_access");
            logger.LogWarning("Normalizing legacy admin scope '{OriginalScope}' to '{NormalizedScope}'", scope, normalized);
            return normalized;
        }

        return scope;
    }

    internal static bool IsSupportedScope(string scope) =>
        !string.IsNullOrWhiteSpace(scope) &&
        (scope.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
         scope.StartsWith("api://", StringComparison.OrdinalIgnoreCase));
}
