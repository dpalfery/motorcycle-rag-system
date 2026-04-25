namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// Admin-app DTO for retrying approval-time onboarding.
/// </summary>
internal sealed class RetryOnboardingDto {
    public string ExpectedRowVersion { get; set; } = string.Empty;
}