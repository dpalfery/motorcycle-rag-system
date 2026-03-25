using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Application.Services.TrustedSources;

/// <summary>
/// Loads trusted web sources from the database via <see cref="IWebSourceRepository"/>.
/// Implements <see cref="ITrustedSourcesLoader"/> for use by the <c>get_trusted_sources</c> Foundry tool handler.
/// </summary>
public sealed class DatabaseTrustedSourcesLoader : ITrustedSourcesLoader
{
    private const string DefaultContentSelector = "//p|//article|//div[@class='content']";

    private static readonly float[] TierCredibilityScores = [0f, 0.95f, 0.80f, 0.65f, 0.50f, 0.35f];

    private readonly IWebSourceRepository _repository;
    private readonly ILogger<DatabaseTrustedSourcesLoader> _logger;

    public DatabaseTrustedSourcesLoader(
        IWebSourceRepository repository,
        ILogger<DatabaseTrustedSourcesLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrustedSourceOptions[]> LoadAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Loading trusted sources from database");

        var all = await _repository.GetAllWebSourcesAsync();

        var enabled = all
            .Where(s => s.IsEnabled && s.IncludeInSearch)
            .ToArray();

        _logger.LogInformation("Loaded {Count} active trusted sources from database (of {Total} total)",
            enabled.Length, all.Length);

        return enabled
            .Select(ws => new TrustedSourceOptions
            {
                Name = ws.Name,
                BaseUrl = new Uri(ws.Url),
                SearchUrlTemplate = BuildSearchUrl(ws.Url),
                ContentSelector = DefaultContentSelector,
                CredibilityScore = MapTierToCredibility(ws.TrustTier)
            })
            .ToArray();
    }

    private static float MapTierToCredibility(int tier)
    {
        if (tier < 1 || tier > 5) return 0.50f;
        return TierCredibilityScores[tier];
    }

    private static Uri BuildSearchUrl(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return new Uri($"{trimmed}/search?q={{query}}");
    }
}
