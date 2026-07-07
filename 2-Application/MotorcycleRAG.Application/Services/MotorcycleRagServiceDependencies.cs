using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Caching;
using MotorcycleRAG.Application.Services.Citations;
using MotorcycleRAG.Application.Services.Metrics;
using MotorcycleRAG.Application.Services.QueryProcessing;
using MotorcycleRAG.Application.Services.QueryValidation;
using MotorcycleRAG.Application.Services.ResponseProcessing;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Groups MotorcycleRagService dependencies to keep the service constructor small.
/// </summary>
public sealed class MotorcycleRagServiceDependencies
{
    public ITelemetryService TelemetryService { get; }
    public IQueryCacheService CacheService { get; }
    public CacheConfiguration CacheConfig { get; }

    public ClaimCitationService CitationService { get; }
    public QueryRefinementService RefinementService { get; }
    public QuestionValidationState QuestionValidationState { get; }
    public ResponseLimitationAnalyzer LimitationAnalyzer { get; }
    public QueryCostCalculator CostCalculator { get; }

    public MotorcycleRagServiceDependencies(
        ITelemetryService telemetryService,
        IQueryCacheService cacheService,
        IOptions<CacheConfiguration> cacheConfig,
        ClaimCitationService citationService,
        QueryRefinementService refinementService,
        QuestionValidationState questionValidationState,
        ResponseLimitationAnalyzer limitationAnalyzer,
        QueryCostCalculator costCalculator)
    {
        TelemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        CacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        CacheConfig = cacheConfig?.Value ?? throw new ArgumentNullException(nameof(cacheConfig));

        CitationService = citationService ?? throw new ArgumentNullException(nameof(citationService));
        RefinementService = refinementService ?? throw new ArgumentNullException(nameof(refinementService));
        QuestionValidationState = questionValidationState ?? throw new ArgumentNullException(nameof(questionValidationState));
        LimitationAnalyzer = limitationAnalyzer ?? throw new ArgumentNullException(nameof(limitationAnalyzer));
        CostCalculator = costCalculator ?? throw new ArgumentNullException(nameof(costCalculator));
    }
}
