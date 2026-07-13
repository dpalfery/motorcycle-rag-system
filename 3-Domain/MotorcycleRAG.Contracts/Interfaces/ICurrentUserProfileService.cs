using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Provides the current authenticated user's profile and usage use cases.
/// </summary>
public interface ICurrentUserProfileService
{
    Task<CurrentUserProfileResult> GetProfileAsync(CancellationToken cancellationToken = default);

    Task<CurrentUserUsageResult> GetUsageAsync(int days, CancellationToken cancellationToken = default);

    Task<CurrentUserProfileResult> UpdateProfileAsync(
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default);
}
