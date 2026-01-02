namespace MotorcycleRAG.Admin.Constants;

/// <summary>
/// Defines the admin roles for authorization and access control.
/// These roles must match the roles configured in Entra ID / Azure AD.
/// </summary>
public static class AdminRoles
{
    /// <summary>Base admin role with general administrative access</summary>
    public const string Admin = "Admin";

    /// <summary>Admin role with data ingestion and management permissions</summary>
    public const string DataAdmin = "DataAdmin";

    /// <summary>Admin role with content management permissions</summary>
    public const string ContentAdmin = "ContentAdmin";

    /// <summary>Super admin role with full system access</summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Collection of all valid admin roles for easy access</summary>
    public static readonly IReadOnlyList<string> AllAdminRoles = new[]
    {
        Admin,
        DataAdmin,
        ContentAdmin,
        SuperAdmin
    };

    /// <summary>
    /// Checks if the given role is a valid admin role.
    /// </summary>
    /// <param name="role">The role name to check</param>
    /// <returns>True if the role is a valid admin role, false otherwise</returns>
    public static bool IsValidAdminRole(string role)
    {
        return AllAdminRoles.Contains(role);
    }

    /// <summary>
    /// Filters a collection of roles to return only valid admin roles.
    /// </summary>
    /// <param name="roles">The roles to filter</param>
    /// <returns>A collection of valid admin roles</returns>
    public static IEnumerable<string> GetValidAdminRoles(IEnumerable<string> roles)
    {
        return roles?.Where(IsValidAdminRole) ?? Enumerable.Empty<string>();
    }
}
