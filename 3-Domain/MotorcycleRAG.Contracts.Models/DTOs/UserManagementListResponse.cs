namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Paged management-row result for the admin user-management view.
/// </summary>
public class UserManagementListResponse {
    public UserManagementRow[] Rows { get; set; } = Array.Empty<UserManagementRow>();

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }
}