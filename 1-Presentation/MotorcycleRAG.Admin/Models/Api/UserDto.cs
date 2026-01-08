namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// DTO for user information
/// </summary>
public class UserDto
{
    internal string UserId { get; set; } = string.Empty;
    internal string Email { get; set; } = string.Empty;
    internal string DisplayName { get; set; } = string.Empty;
    internal bool IsEnabled { get; set; }
    internal string PlanSku { get; set; } = string.Empty;
    internal DateTime CreatedAt { get; set; }
    internal DateTime? LastSignInAt { get; set; }
}
