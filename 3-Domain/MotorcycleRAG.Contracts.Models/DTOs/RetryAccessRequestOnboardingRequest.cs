using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Admin request for retrying onboarding on an approved request that previously failed.
/// </summary>
public class RetryAccessRequestOnboardingRequest {
    [Required]
    public string ExpectedRowVersion { get; set; } = string.Empty;
}