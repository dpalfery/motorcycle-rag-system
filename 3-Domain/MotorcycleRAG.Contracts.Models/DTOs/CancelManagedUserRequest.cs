using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Admin request for cancelling an existing managed user.
/// </summary>
public class CancelManagedUserRequest {
    [Required]
    public string ExpectedRowVersion { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}