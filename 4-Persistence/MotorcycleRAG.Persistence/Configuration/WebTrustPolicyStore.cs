using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Contracts.Interfaces;
using System.Collections.Concurrent;

namespace MotorcycleRAG.Persistence.Configuration;

/// <summary>
/// In-memory store for web trust policy configuration with optional persistence
/// </summary>
public class WebTrustPolicyStore : IWebTrustPolicyStore
{
    private readonly ILogger<WebTrustPolicyStore> _logger;
    private readonly ConcurrentDictionary<string, WebTrustPolicy> _policies;
    private readonly ConcurrentDictionary<string, HashSet<string>> _allowlists;

    public WebTrustPolicyStore(ILogger<WebTrustPolicyStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policies = new ConcurrentDictionary<string, WebTrustPolicy>();
        _allowlists = new ConcurrentDictionary<string, HashSet<string>>();

        // Initialize with default trusted sources
        InitializeDefaultPolicies();
    }

    /// <summary>
    /// Initialize default trust policies for common motorcycle domains
    /// </summary>
    private void InitializeDefaultPolicies()
    {
        AddDefaultPolicy("*.honda.com", WebTrustTier.TierA, "Official Honda Motorcycle website");
        AddDefaultPolicy("*.yamaha-motor.com", WebTrustTier.TierA, "Official Yamaha Motorcycle website");
        AddDefaultPolicy("*.kawasaki.com", WebTrustTier.TierA, "Official Kawasaki website");
        AddDefaultPolicy("*.bmwmotorcycles.com", WebTrustTier.TierA, "Official BMW Motorcycles website");
        AddDefaultPolicy("*.suzuki.com", WebTrustTier.TierA, "Official Suzuki website");
        AddDefaultPolicy("*.ducati.com", WebTrustTier.TierA, "Official Ducati website");
        AddDefaultPolicy("*.cycleworld.com", WebTrustTier.TierB, "Cycle World - Reputable motorcycle media");
        AddDefaultPolicy("*.motorcycle.com", WebTrustTier.TierB, "Motorcycle.com - Established motorcycle publication");
        AddDefaultPolicy("*.motorcycle-usa.com", WebTrustTier.TierB, "MotorcycleUSA - Reputable motorcycle news");

        _logger.LogInformation("Initialized default web trust policies with {Count} entries", _policies.Count);
    }

    private void AddDefaultPolicy(string domainPattern, WebTrustTier tier, string reason) =>
        AddOrUpdatePolicy(WebTrustPolicy.Create(domainPattern, tier, allowSubdomains: true, reason));

    /// <summary>
    /// Add or update a trust policy
    /// </summary>
    public void AddOrUpdatePolicy(WebTrustPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.MarkUpdated();
        _policies.AddOrUpdate(policy.DomainPattern, policy, (_, _) => policy);

        // Update allowlist cache
        if (!policy.IsBlocked && policy.Tier != WebTrustTier.None)
        {
            var tierKey = policy.Tier.ToString();
            var allowlist = _allowlists.GetOrAdd(tierKey, _ => new HashSet<string>());
            allowlist.Add(policy.DomainPattern);
        }

        _logger.LogDebug("Updated trust policy for {DomainPattern} with tier {Tier}", 
            policy.DomainPattern, policy.Tier);
    }

    /// <summary>
    /// Get trust policy for a domain
    /// </summary>
    public WebTrustPolicy? GetPolicyForDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        // Try exact match first
        if (_policies.TryGetValue(domain, out var exactPolicy))
        {
            return exactPolicy;
        }

        // Try wildcard pattern matching. The entity owns normalization and
        // host-pattern semantics so this adapter only orders candidates.
        foreach (var kvp in _policies.OrderByDescending(x => x.Key.Length))
        {
            if (kvp.Value.MatchesDomain(domain))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Check if a domain is on the allowlist for a specific tier
    /// </summary>
    public bool IsDomainAllowed(string domain, WebTrustTier minimumTier = WebTrustTier.TierC)
    {
        var policy = GetPolicyForDomain(domain);
        
        if (policy == null)
        {
            return false;
        }

        if (policy.IsBlocked)
        {
            return false;
        }

        return policy.Tier >= minimumTier;
    }

    /// <summary>
    /// Get all policies for a specific tier
    /// </summary>
    public WebTrustPolicy[] GetPoliciesByTier(WebTrustTier tier)
    {
        return _policies.Values
            .Where(p => p.Tier == tier)
            .ToArray();
    }

    /// <summary>
    /// Get all policies
    /// </summary>
    public WebTrustPolicy[] GetAllPolicies()
    {
        return _policies.Values.ToArray();
    }

    /// <summary>
    /// Remove a policy
    /// </summary>
    public bool RemovePolicy(string domainPattern)
    {
        if (_policies.TryRemove(domainPattern, out var removedPolicy))
        {
            // Remove from allowlist cache
            var tierKey = removedPolicy.Tier.ToString();
            if (_allowlists.TryGetValue(tierKey, out var allowlist))
            {
                allowlist.Remove(domainPattern);
            }

            _logger.LogInformation("Removed trust policy for {DomainPattern}", domainPattern);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get allowlist for a tier
    /// </summary>
    public string[] GetAllowlistForTier(WebTrustTier tier)
    {
        return _allowlists.TryGetValue(tier.ToString(), out var allowlist) 
            ? allowlist.ToArray() 
            : Array.Empty<string>();
    }
}
