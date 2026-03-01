using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Persistence.Sql;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

/// <summary>
/// Dapper-based implementation of <see cref="IBikeModelRepository"/>.
/// All SQL uses parameterized queries — no string concatenation.
/// </summary>
public class BikeModelRepository : IBikeModelRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<BikeModelRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BikeModelRepository"/>.
    /// </summary>
    /// <param name="connectionFactory">SQL connection factory.</param>
    /// <param name="logger">Logger instance.</param>
    public BikeModelRepository(ISqlConnectionFactory connectionFactory, ILogger<BikeModelRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<BikeModel?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT [Id], [Make], [Model], [Year], [Aliases], [CreatedAtUtc], [UpdatedAtUtc]
            FROM [dbo].[BikeModels]
            WHERE [Id] = @Id;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.QueryFirstOrDefaultAsync<BikeModel>(
                new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get bike model {BikeModelId}", id);
            throw new InvalidOperationException($"Failed to get bike model {id}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<BikeModel?> FindCanonicalAsync(string make, string model, int year, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT [Id], [Make], [Model], [Year], [Aliases], [CreatedAtUtc], [UpdatedAtUtc]
            FROM [dbo].[BikeModels]
            WHERE [Make] = @Make AND [Model] = @Model AND [Year] = @Year;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.QueryFirstOrDefaultAsync<BikeModel>(
                new CommandDefinition(sql, new { Make = make, Model = model, Year = year }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find canonical bike model {Make} {Model} {Year}", make, model, year);
            throw new InvalidOperationException($"Failed to find canonical bike model {make} {model} {year}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Guid> UpsertAsync(BikeModel bikeModel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bikeModel);

        const string sql = @"
            MERGE [dbo].[BikeModels] AS target
            USING (SELECT @Id AS [Id], @Make AS [Make], @Model AS [Model], @Year AS [Year]) AS source
            ON target.[Make] = source.[Make] AND target.[Model] = source.[Model] AND target.[Year] = source.[Year]
            WHEN MATCHED THEN
                UPDATE SET
                    [Aliases] = @Aliases,
                    [UpdatedAtUtc] = @UpdatedAtUtc
            WHEN NOT MATCHED THEN
                INSERT ([Id], [Make], [Model], [Year], [Aliases], [CreatedAtUtc], [UpdatedAtUtc])
                VALUES (@Id, @Make, @Model, @Year, @Aliases, @CreatedAtUtc, @UpdatedAtUtc)
            OUTPUT inserted.[Id];
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var resultId = await connection.ExecuteScalarAsync<Guid>(
                new CommandDefinition(sql, new
                {
                    bikeModel.Id,
                    bikeModel.Make,
                    bikeModel.Model,
                    bikeModel.Year,
                    bikeModel.Aliases,
                    CreatedAtUtc = bikeModel.CreatedAtUtc.UtcDateTime,
                    UpdatedAtUtc = bikeModel.UpdatedAtUtc.UtcDateTime
                }, cancellationToken: ct));

            _logger.LogInformation("Upserted bike model {BikeModelId} ({Make} {Model} {Year})",
                resultId, bikeModel.Make, bikeModel.Model, bikeModel.Year);

            return resultId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert bike model {Make} {Model} {Year}",
                bikeModel.Make, bikeModel.Model, bikeModel.Year);
            throw new InvalidOperationException(
                $"Failed to upsert bike model {bikeModel.Make} {bikeModel.Model} {bikeModel.Year}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BikeModel>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT [Id], [Make], [Model], [Year], [Aliases], [CreatedAtUtc], [UpdatedAtUtc]
            FROM [dbo].[BikeModels]
            ORDER BY [Make], [Model], [Year]
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            var results = await connection.QueryAsync<BikeModel>(
                new CommandDefinition(sql, new { Skip = skip, Take = take }, cancellationToken: ct));

            return results.ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list bike models (skip={Skip}, take={Take})", skip, take);
            throw new InvalidOperationException($"Failed to list bike models (skip={skip}, take={take})", ex);
        }
    }
}