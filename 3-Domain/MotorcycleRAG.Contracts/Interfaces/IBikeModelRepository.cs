using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository abstraction for persisting and querying canonical bike model records.
/// All methods are fully asynchronous; implementations must use parameterized SQL (no string concat).
/// </summary>
public interface IBikeModelRepository
{
    /// <summary>Returns a bike model by its GUID, or null if not found.</summary>
    Task<BikeModel?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Finds the canonical bike model matching the given make, model, and year.
    /// Returns null if no match exists.
    /// </summary>
    Task<BikeModel?> FindCanonicalAsync(string make, string model, int year, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a bike model record (SQL Server MERGE).
    /// Returns the Id of the upserted record.
    /// </summary>
    Task<Guid> UpsertAsync(BikeModel bikeModel, CancellationToken ct = default);

    /// <summary>
    /// Returns a paged list of bike models ordered by Make, Model, Year.
    /// </summary>
    Task<IReadOnlyList<BikeModel>> ListAsync(int skip, int take, CancellationToken ct = default);
}