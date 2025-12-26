using System;

namespace MotorcycleRAG.MobileApp.Models;

public enum SubscriptionPlan
{
    Free,
    Plus,
    Pro
}

public class UserProfile
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Free;
    public int DailyRequestLimit { get; set; } = 10;
    public int RequestsUsedToday { get; set; }
    public DateTime LimitResetAt { get; set; }
}
