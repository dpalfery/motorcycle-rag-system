namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Centralised constants for ASP.NET Core authorization policy names used across this API.
/// Using constants avoids magic strings and ensures compile-time safety when referencing policies.
/// </summary>
public static class AuthorizationPolicyNames
{
    /// <summary>
    /// Policy for content administrators: requires admin scope, mcr-api-admin role, and correct azp claim.
    /// </summary>
    public const string Admin = "mcr-api-admin";

    /// <summary>
    /// Policy for users/viewers entitled to view manual pages (GET /api/manuals/{id}/pages/{n}).
    /// Allows roles: User, Viewer, and mcr-api-admin.
    /// </summary>
    public const string ManualsView = "mcr-api-manuals-view";
}