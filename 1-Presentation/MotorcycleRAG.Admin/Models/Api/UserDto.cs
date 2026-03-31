using System.Text.Json.Serialization;

namespace MotorcycleRAG.Admin.Models.Api;

/// <summary>
/// DTO for user information
/// </summary>
#pragma warning disable CA1812 // Instantiated via deserialization
#pragma warning disable S3059 // Public properties required for serialization
internal class UserDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
#pragma warning restore S3059
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }
    [JsonPropertyName("planId")]
    public string PlanId { get; set; } = string.Empty;
    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; set; }
    [JsonPropertyName("lastUpdatedDate")]
    public DateTime? LastUpdatedDate { get; set; }
}
