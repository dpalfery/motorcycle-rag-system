using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Provides administrative use cases for user-plan management.
/// </summary>
public interface IPlanAdministrationService
{
    Task<UserPlan[]> GetAllPlansAsync(CancellationToken cancellationToken = default);

    Task<UserPlan?> GetPlanByIdAsync(string planId, CancellationToken cancellationToken = default);

    Task<UserPlan> CreatePlanAsync(PlanCreateCommand command, CancellationToken cancellationToken = default);

    Task<UserPlan?> UpdatePlanAsync(
        string planId,
        PlanUpdateCommand command,
        CancellationToken cancellationToken = default);

    Task<PlanDeleteResult> DeletePlanAsync(string planId, CancellationToken cancellationToken = default);
}
