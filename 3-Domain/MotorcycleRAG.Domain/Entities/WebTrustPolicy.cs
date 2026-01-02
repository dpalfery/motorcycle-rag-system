using System.ComponentModel.DataAnnotations;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Defines trust policies and allowlist rules for web domains
/// </summary>
public class WebTrustPolicy
{
    [Required]
    public Guid Id { get; set; }

    /// <summary>
    /// The domain or domain pattern (e.g., "*.honda.com", "motorcycle.com")
    /// </summary>
    [Required]
    [StringLength(255)]
    public string DomainPattern { get; set; } = string.Empty;

    /// <summary>
    /// The assigned trust tier for this domain pattern
    /// </summary>
    [Required]
    public WebTrustTier Tier { get; set; }

    /// <summary>
    /// Whether this domain is explicitly blocked regardless of tier
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Reason for the trust tier or block status
    /// </summary>
    [StringLength(1000)]
    public string? Reason { get; set; }

    /// <summary>
    /// Whether to allow automatic discovery of subdomains
    /// </summary>
    public bool AllowSubdomains { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
