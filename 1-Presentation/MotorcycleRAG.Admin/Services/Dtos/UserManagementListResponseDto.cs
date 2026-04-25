namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// Admin-app DTO for the paged user-management response.
/// </summary>
internal sealed class UserManagementListResponseDto {
    public UserManagementRowDto[] Rows { get; set; } = Array.Empty<UserManagementRowDto>();

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }
}