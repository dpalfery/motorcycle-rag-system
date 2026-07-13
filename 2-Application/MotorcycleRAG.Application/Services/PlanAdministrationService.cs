using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates administrative user-plan operations through the plan repository boundary.
/// </summary>
public sealed class PlanAdministrationService : IPlanAdministrationService
{
    private readonly IPlanRepository _planRepository;
    private readonly ILogger<PlanAdministrationService> _logger;

    public PlanAdministrationService(IPlanRepository planRepository, ILogger<PlanAdministrationService> logger)
    {
        _planRepository = planRepository ?? throw new ArgumentNullException(nameof(planRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<UserPlan[]> GetAllPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _planRepository.GetAllPlansAsync().ConfigureAwait(false);
        _logger.LogInformation("Admin retrieved {Count} plans", plans.Length);
        return plans;
    }

    /// <inheritdoc />
    public async Task<UserPlan?> GetPlanByIdAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        var plan = await _planRepository.GetPlanByIdAsync(planId).ConfigureAwait(false);
        if (plan is null)
        {
            _logger.LogWarning("Plan {PlanId} not found", LogSanitizer.Sanitize(planId));
        }
        else
        {
            _logger.LogInformation("Admin retrieved plan {PlanId}", LogSanitizer.Sanitize(planId));
        }

        return plan;
    }

    /// <inheritdoc />
    public async Task<UserPlan> CreatePlanAsync(PlanCreateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var createdPlan = await _planRepository.CreatePlanAsync(new UserPlan
        {
            Id = Guid.NewGuid().ToString(),
            Name = command.Name,
            Description = command.Description,
            DailyRequestLimit = command.DailyRequestLimit,
            IsPaid = command.IsPaid,
            CreatedDate = DateTime.UtcNow
        }).ConfigureAwait(false);
        _logger.LogInformation("Admin created plan {PlanId} with name {PlanName}", LogSanitizer.Sanitize(createdPlan.Id), LogSanitizer.Sanitize(createdPlan.Name));
        return createdPlan;
    }

    /// <inheritdoc />
    public async Task<UserPlan?> UpdatePlanAsync(string planId, PlanUpdateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(command);
        var plan = await _planRepository.GetPlanByIdAsync(planId).ConfigureAwait(false);
        if (plan is null)
        {
            _logger.LogWarning("Plan {PlanId} not found for update", LogSanitizer.Sanitize(planId));
            return null;
        }

        if (!string.IsNullOrWhiteSpace(command.Name))
        {
            plan.Name = command.Name;
        }

        if (!string.IsNullOrWhiteSpace(command.Description))
        {
            plan.Description = command.Description;
        }

        if (command.DailyRequestLimit.HasValue)
        {
            plan.DailyRequestLimit = command.DailyRequestLimit.Value;
        }

        if (command.IsPaid.HasValue)
        {
            plan.IsPaid = command.IsPaid.Value;
        }

        if (!await _planRepository.UpdatePlanAsync(plan).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Failed to update plan.");
        }

        _logger.LogInformation("Admin updated plan {PlanId}", LogSanitizer.Sanitize(planId));
        return plan;
    }

    /// <inheritdoc />
    public async Task<PlanDeleteResult> DeletePlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        if (await _planRepository.GetPlanByIdAsync(planId).ConfigureAwait(false) is null)
        {
            _logger.LogWarning("Plan {PlanId} not found for deletion", LogSanitizer.Sanitize(planId));
            return new(false, false);
        }

        var deleted = await _planRepository.DeletePlanAsync(planId).ConfigureAwait(false);
        if (deleted)
        {
            _logger.LogInformation("Admin deleted plan {PlanId}", LogSanitizer.Sanitize(planId));
        }
        else
        {
            _logger.LogError("Failed to delete plan {PlanId}", LogSanitizer.Sanitize(planId));
        }

        return new(true, deleted);
    }
}
