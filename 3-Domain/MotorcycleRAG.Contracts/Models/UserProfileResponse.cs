namespace MotorcycleRAG.Domain.Models;

/// <summary>
/// User profile response model
/// </summary>
public class UserProfileResponse
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
    public string PlanId { get; set; } = string.Empty;
    public int DailyRequestLimit { get; set; }
    public int RemainingDailyRequests { get; set; }
}
