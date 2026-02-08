namespace MotorcycleRAG.Admin.Constants;

/// <summary>
/// Defines the admin roles for authorization and access control.
/// These roles must match the roles configured in Entra ID / Azure AD.
/// </summary>
internal static class AdminRoles
{
    /// <summary>Global admin role with full system access</summary>
    internal const string Admin = "mcr-api-admin";

    /// <summary>Content administration role for content configuration management</summary>
    internal const string ContentAdmin = "ContentAdmin";

    /// <summary>Super admin role with full system access and elevated privileges</summary>
    internal const string SuperAdmin = "SuperAdmin";

    /// <summary>Collection of all valid admin roles</summary>
    internal static readonly IReadOnlyList<string> AllAdminRoles = new[]
    {
        Admin,
        ContentAdmin,
        SuperAdmin
    };

    /// <summary>
    /// Checks if the given role is a valid admin role.
    /// </summary>
    /// <param name="role">The role name to check</param>
    /// <returns>True if the role is a valid admin role, false otherwise</returns>
    internal static bool IsValidAdminRole(string role)
    {
        return AllAdminRoles.Contains(role);
    }

    /// <summary>
    /// Filters a collection of roles to return only valid admin roles.
    /// </summary>
    /// <param name="roles">The roles to filter</param>
    /// <returns>A collection of valid admin roles</returns>
    internal static IEnumerable<string> GetValidAdminRoles(IEnumerable<string> roles)
    {
        return roles?.Where(IsValidAdminRole) ?? Enumerable.Empty<string>();
    }
}
