namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Centralised constants for ASP.NET Core authorization policy names used across this API.
/// Using constants avoids magic strings and ensures compile-time safety when referencing policies.
/// </summary>
internal static class AuthorizationPolicyNames {
    /// <summary>
    /// Policy for content administrators: requires admin scope, mcr-api-admin role, and correct azp claim.
    /// </summary>
    internal const string Admin = "mcr-api-admin";

    /// <summary>
    /// Policy for local processor machine-to-machine ingestion callbacks.
    /// </summary>
    internal const string LocalProcessor = "mcr-api-local-processor";

    /// <summary>
    /// Policy requiring delegated read scope.
    /// </summary>
    internal const string Read = "Read";

    /// <summary>
    /// Policy requiring delegated chat scope.
    /// </summary>
    internal const string Chat = "Chat";

    /// <summary>
    /// Policy for regular approved users.
    /// </summary>
    internal const string User = "User";

    /// <summary>
    /// Policy for read-only approved users.
    /// </summary>
    internal const string Viewer = "Viewer";

    /// <summary>
    /// Policy for users/viewers entitled to view manual pages (GET /api/manuals/{id}/pages/{n}).
    /// Allows roles: User, Viewer, and mcr-api-admin.
    /// </summary>
    internal const string ManualsView = "mcr-api-manuals-view";
}
