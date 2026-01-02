using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Store for web trust policies that enforces domain allowlist and trust tiers
/// </summary>
public interface IWebTrustPolicyStore
{
    /// <summary>
    /// Get trust policy for a specific domain
    /// </summary>
    /// <param name="domain">Domain to lookup</param>
    /// <returns>Trust policy if found, null otherwise</returns>
    WebTrustPolicy? GetPolicyForDomain(string domain);

    /// <summary>
    /// Check if a domain is on the allowlist for a minimum trust tier
    /// </summary>
    /// <param name="domain">Domain to check</param>
    /// <param name="minimumTier">Minimum required trust tier</param>
    /// <returns>True if domain is allowed, false otherwise</returns>
    bool IsDomainAllowed(string domain, WebTrustTier minimumTier = WebTrustTier.TierC);

    /// <summary>
    /// Get all policies for a specific trust tier
    /// </summary>
    /// <param name="tier">Trust tier to retrieve</param>
    /// <returns>Array of policies for the tier</returns>
    WebTrustPolicy[] GetPoliciesByTier(WebTrustTier tier);

    /// <summary>
    /// Get all policies
    /// </summary>
    /// <returns>Array of all policies</returns>
    WebTrustPolicy[] GetAllPolicies();
}
