namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Active managed-user identity link state used by lifecycle orchestration.
/// </summary>
public class UserIdentityLinkRecord {
    public string ManagedUserId { get; set; } = string.Empty;

    public IdentityProvider Provider { get; set; }

    public string ProviderEmail { get; set; } = string.Empty;

    public string? ExternalDirectoryObjectId { get; set; }
}