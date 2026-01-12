namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// DTO for user information
/// </summary>
#pragma warning disable CA1812 // Instantiated via deserialization
#pragma warning disable S3059 // Public properties required for serialization
internal class UserDto
{
    public string UserId { get; set; } = string.Empty;
#pragma warning restore S3059
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string PlanSku { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSignInAt { get; set; }
}
