using System.ComponentModel.DataAnnotations;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Defines trust policies and allowlist rules for web domains
/// </summary>
public class WebTrustPolicy
{
    private WebTrustPolicy(
        Guid id,
        string domainPattern,
        WebTrustTier tier,
        bool isBlocked,
        string? reason,
        bool allowSubdomains,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        DomainPattern = domainPattern;
        Tier = tier;
        IsBlocked = isBlocked;
        Reason = reason;
        AllowSubdomains = allowSubdomains;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    [Required]
    public Guid Id { get; }

    /// <summary>
    /// The domain or domain pattern (e.g., "*.honda.com", "motorcycle.com")
    /// </summary>
    [Required]
    [StringLength(255)]
    public string DomainPattern { get; }

    /// <summary>
    /// The assigned trust tier for this domain pattern
    /// </summary>
    [Required]
    public WebTrustTier Tier { get; private set; }

    /// <summary>
    /// Whether this domain is explicitly blocked regardless of tier
    /// </summary>
    public bool IsBlocked { get; private set; }

    /// <summary>
    /// Reason for the trust tier or block status
    /// </summary>
    [StringLength(1000)]
    public string? Reason { get; private set; }

    /// <summary>
    /// Whether to allow automatic discovery of subdomains
    /// </summary>
    public bool AllowSubdomains { get; }

    public DateTime CreatedAt { get; }

    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a policy with a normalized host pattern and validated trust tier.
    /// </summary>
    public static WebTrustPolicy Create(
        string domainPattern,
        WebTrustTier tier,
        bool allowSubdomains = false,
        string? reason = null,
        bool isBlocked = false)
    {
        return Rehydrate(
            Guid.NewGuid(),
            domainPattern,
            tier,
            isBlocked,
            reason,
            allowSubdomains,
            DateTime.UtcNow,
            updatedAt: null);
    }

    /// <summary>
    /// Rehydrates a persisted policy after validating the complete state at the Persistence
    /// boundary. Invalid records are rejected rather than becoming usable domain policies.
    /// </summary>
    public static WebTrustPolicy Rehydrate(
        Guid id,
        string domainPattern,
        WebTrustTier tier,
        bool isBlocked,
        string? reason,
        bool allowSubdomains,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Policy ID is required.", nameof(id));
        }

        var normalizedPattern = NormalizePattern(domainPattern);
        if (normalizedPattern.Length == 0 || normalizedPattern is "*" or "*.")
        {
            throw new ArgumentException("Domain pattern is required.", nameof(domainPattern));
        }

        if (!Enum.IsDefined(tier))
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown web trust tier.");
        }

        if (tier == WebTrustTier.None && !isBlocked)
        {
            throw new ArgumentException("A non-blocked policy must have a trust tier.", nameof(tier));
        }

        return new WebTrustPolicy(
            id,
            normalizedPattern,
            tier,
            isBlocked,
            NormalizeReason(reason),
            allowSubdomains,
            createdAt.ToUniversalTime(),
            updatedAt?.ToUniversalTime());
    }

    /// <summary>Returns whether this policy applies to the supplied host.</summary>
    public bool MatchesDomain(string domain)
    {
        var normalizedDomain = NormalizePattern(domain);
        if (normalizedDomain.Length == 0 || DomainPattern.Length == 0)
        {
            return false;
        }

        if (string.Equals(DomainPattern, normalizedDomain, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!DomainPattern.StartsWith("*.", StringComparison.Ordinal))
        {
            return false;
        }

        var baseDomain = DomainPattern[2..];
        return string.Equals(normalizedDomain, baseDomain, StringComparison.OrdinalIgnoreCase)
            || AllowSubdomains && normalizedDomain.EndsWith('.' + baseDomain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Blocks the policy and records the reason for the decision.</summary>
    public void Block(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        IsBlocked = true;
        Reason = NormalizeReason(reason);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Allows the policy while retaining an optional explanatory reason.</summary>
    public void Allow(string? reason = null)
    {
        if (Tier == WebTrustTier.None)
        {
            throw new InvalidOperationException("A policy without a trust tier cannot be allowed.");
        }

        IsBlocked = false;
        Reason = NormalizeReason(reason);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Changes the trust tier while keeping the policy identity stable.</summary>
    public void ChangeTier(WebTrustTier tier, string? reason = null)
    {
        if (!Enum.IsDefined(tier))
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown web trust tier.");
        }

        if (tier == WebTrustTier.None && !IsBlocked)
        {
            throw new ArgumentException("A non-blocked policy must have a trust tier.", nameof(tier));
        }

        Tier = tier;
        if (reason is not null)
        {
            Reason = NormalizeReason(reason);
        }

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks a persistence update without exposing a public setter.</summary>
    public void MarkUpdated() => UpdatedAt = DateTime.UtcNow;

    private static string NormalizePattern(string? pattern)
        => (pattern ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var normalizedReason = reason.Trim();
        if (normalizedReason.Length > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "Policy reason cannot exceed 1000 characters.");
        }

        return normalizedReason;
    }
}
