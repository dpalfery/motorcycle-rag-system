using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Minimal external identity provisioning implementation that preserves orchestration flow until Graph wiring is added.
/// </summary>
public class ExternalIdentityProvisioningService : IExternalIdentityProvisioningService {
    private readonly ILogger<ExternalIdentityProvisioningService> _logger;

    public ExternalIdentityProvisioningService(ILogger<ExternalIdentityProvisioningService> logger) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<string> ProvisionApprovedUserAsync(string email, string displayName, TierLabel tier, IdentityProvider provider) {
        if (string.IsNullOrWhiteSpace(email)) {
            throw new ArgumentException("Email cannot be null or empty", nameof(email));
        }

        var externalDirectoryObjectId = CreateDeterministicObjectId(provider, email);
        _logger.LogInformation(
            "Provisioned placeholder external identity {ExternalDirectoryObjectId} for {Provider}/{Email} with tier {Tier}",
            externalDirectoryObjectId,
            provider,
            email,
            tier);

        return Task.FromResult(externalDirectoryObjectId);
    }

    public Task ReconcileTierAssignmentsAsync(string externalDirectoryObjectId, TierLabel tier) {
        if (string.IsNullOrWhiteSpace(externalDirectoryObjectId)) {
            throw new ArgumentException("External directory object ID cannot be null or empty", nameof(externalDirectoryObjectId));
        }

        _logger.LogInformation(
            "Reconciled placeholder tier assignment for external identity {ExternalDirectoryObjectId} to tier {Tier}",
            externalDirectoryObjectId,
            tier);

        return Task.CompletedTask;
    }

    public Task RevokeAccessAsync(string externalDirectoryObjectId) {
        if (string.IsNullOrWhiteSpace(externalDirectoryObjectId)) {
            throw new ArgumentException("External directory object ID cannot be null or empty", nameof(externalDirectoryObjectId));
        }

        _logger.LogInformation(
            "Revoked placeholder external identity access for {ExternalDirectoryObjectId}",
            externalDirectoryObjectId);

        return Task.CompletedTask;
    }

    private static string CreateDeterministicObjectId(IdentityProvider provider, string email) {
        var normalized = $"{provider}:{email.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes).ToString();
    }
}