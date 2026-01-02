namespace MotorcycleRAG.Domain.Enums;

/// <summary>
/// Defines the trust levels for web sources
/// </summary>
public enum WebTrustTier
{
    /// <summary>
    /// No trust assigned (untrusted or unknown)
    /// </summary>
    None = 0,

    /// <summary>
    /// Tier C: Community sources, blogs, and general enthusiasts
    /// </summary>
    TierC = 1,

    /// <summary>
    /// Tier B: Verified enthusiast sites, specialist forums, and reputable community hubs
    /// </summary>
    TierB = 2,

    /// <summary>
    /// Tier A: Official manufacturer websites, primary technical publications, and regulatory bodies
    /// </summary>
    TierA = 3
}
