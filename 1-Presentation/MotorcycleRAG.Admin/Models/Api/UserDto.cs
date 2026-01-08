namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// DTO for user information
/// </summary>
internal class UserDto
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string PlanSku { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSignInAt { get; set; }
}
