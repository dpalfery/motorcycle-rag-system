using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Admin request for cancelling a pending access request.
/// </summary>
public class CancelAccessRequestRequest {
    [Required]
    public string ExpectedRowVersion { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}