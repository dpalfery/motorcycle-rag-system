using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Public request model for submitting a new onboarding access request.
/// </summary>
public class CreateAccessRequestRequest {
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public IdentityProvider Provider { get; set; }
}