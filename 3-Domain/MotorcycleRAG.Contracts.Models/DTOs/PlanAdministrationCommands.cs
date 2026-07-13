namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>Application command for creating an administrative user plan.</summary>
public sealed record PlanCreateCommand(
    string Name,
    string Description,
    int DailyRequestLimit,
    bool IsPaid);

/// <summary>Application command for updating an administrative user plan.</summary>
public sealed record PlanUpdateCommand(
    string? Name,
    string? Description,
    int? DailyRequestLimit,
    bool? IsPaid);

/// <summary>Outcome of deleting an administrative user plan.</summary>
public sealed record PlanDeleteResult(bool Found, bool Deleted);
