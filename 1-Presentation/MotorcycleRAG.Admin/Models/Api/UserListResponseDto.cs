using System.Collections.ObjectModel;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// Paged response wrapper for admin user listings.
/// </summary>
internal class UserListResponseDto
{
    public Collection<UserDto> Users { get; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
