using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.QueryValidation;

/// <summary>
/// Deterministic query validation used by the Foundry orchestrator before retrieval tools run.
/// </summary>
public sealed class QuestionValidationService
{
    private const int BikeLookupLimit = 5000;
    private static readonly string[] TripTerms = ["trip", "route", "ride", "itinerary", "road trip", "touring", "destination"];
    private static readonly string[] SpecsTerms = ["spec", "specs", "horsepower", "torque", "weight", "seat height", "engine", "displacement"];
    private static readonly string[] MaintenanceTerms = ["oil", "valve", "service", "maintenance", "interval", "torque spec", "fault code", "repair"];
    private static readonly string[] ComparisonTerms = ["compare", "versus", " vs ", "difference", "better", "recommend"];
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "are", "is", "the", "a", "an", "on", "for", "of", "to", "about", "please",
        "tell", "me", "show", "give", "spec", "specs", "specifications", "motorcycle", "bike",
        "bikes", "model", "models", "year"
    };

    private readonly IBikeModelRepository _bikeModelRepository;
    private readonly IGraphRepository _graphRepository;
    private readonly ILogger<QuestionValidationService> _logger;

    public QuestionValidationService(
        IBikeModelRepository bikeModelRepository,
        IGraphRepository graphRepository,
        ILogger<QuestionValidationService> logger)
    {
        _bikeModelRepository = bikeModelRepository ?? throw new ArgumentNullException(nameof(bikeModelRepository));
        _graphRepository = graphRepository ?? throw new ArgumentNullException(nameof(graphRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<QuestionValidationResult> ValidateAsync(
        string query,
        IReadOnlyList<QueryRecentMessage> recentMessages,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Clarification(
                "Unknown",
                "What motorcycle question would you like help with?",
                []);
        }

        var subject = ClassifySubject(query);
        if (subject == "TripPlanning")
        {
            return Clarification(
                subject,
                "I can help with motorcycle trip planning, but I need a little more detail first. What are your origin, destination, travel dates or season, and riding preferences?",
                [
                    new QueryClarificationSuggestion
                    {
                        Label = "Add trip details",
                        Query = "Plan a motorcycle trip from [origin] to [destination] for [dates or season], preferring [scenic/fast/easy] roads.",
                        Reason = "Trip planning needs route and preference details before tools can be selected.",
                        Subject = subject
                    }
                ]);
        }

        if (!LooksLikeSpecificMotorcycleQuestion(query, subject))
        {
            return Answer(subject, query, confidence: subject == "Unknown" ? 0.45 : 0.65);
        }

        var bikeModels = await SafeListBikeModelsAsync(ct).ConfigureAwait(false);
        var detected = DetectBikeTerms(query, bikeModels);
        var candidates = ScoreBikeCandidates(detected, bikeModels);

        var graphCandidates = await SearchGraphCandidatesAsync(detected, ct).ConfigureAwait(false);
        candidates.AddRange(graphCandidates);

        candidates = candidates
            .GroupBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.Score).First())
            .Where(c => c.Score >= 0.30 || c.IsExact)
            .OrderByDescending(c => c.IsExact)
            .ThenByDescending(c => c.Score)
            .Take(5)
            .ToList();

        var exact = candidates.FirstOrDefault(c => c.IsExact);
        if (exact != null)
        {
            return Answer(subject, query, confidence: 0.95);
        }

        if (detected.HasSpecificBikeSignal && candidates.Count > 0)
        {
            return Clarification(
                subject,
                "I could not find an exact motorcycle match for that name. Did you mean one of these?",
                candidates.Take(3).Select(c => new QueryClarificationSuggestion
                {
                    Label = c.DisplayName,
                    Query = $"What are the specs on the {c.DisplayName}?",
                    Reason = c.Reason,
                    Subject = subject
                }).ToArray());
        }

        if (detected.HasSpecificBikeSignal)
        {
            return Clarification(
                subject,
                "I could not identify the exact motorcycle model. Can you confirm the make, model, and year?",
                []);
        }

        return Answer(subject, query, confidence: 0.65);
    }

    private async Task<IReadOnlyList<BikeModel>> SafeListBikeModelsAsync(CancellationToken ct)
    {
        try
        {
            return await _bikeModelRepository.ListAsync(0, BikeLookupLimit, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bike model lookup failed during query validation");
            return Array.Empty<BikeModel>();
        }
    }

    private async Task<IReadOnlyList<ScoredCandidate>> SearchGraphCandidatesAsync(
        DetectedBikeTerms detected,
        CancellationToken ct)
    {
        var terms = BuildGraphSearchTerms(detected).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        if (terms.Length == 0)
        {
            return Array.Empty<ScoredCandidate>();
        }

        var candidates = new List<ScoredCandidate>();
        foreach (var term in terms)
        {
            try
            {
                var nodes = await _graphRepository.SearchNodesAsync(term, "Motorcycle", 5, ct).ConfigureAwait(false);
                candidates.AddRange(nodes.Select(n => new ScoredCandidate(
                    n.Name,
                    ScoreGraphCandidate(detected, n),
                    IsExactGraphMatch(detected, n),
                    "Closest motorcycle entity found in the bike graph.")));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bike graph lookup failed during query validation");
            }
        }

        return candidates;
    }

    private static string ClassifySubject(string query)
    {
        if (ContainsAny(query, TripTerms)) return "TripPlanning";
        if (ContainsAny(query, MaintenanceTerms)) return "MotorcycleMaintenance";
        if (ContainsAny(query, ComparisonTerms)) return "MotorcycleComparison";
        if (ContainsAny(query, SpecsTerms)) return "MotorcycleSpecs";
        return ContainsAny(query, ["motorcycle", "bike", "scooter"]) ? "MotorcycleSpecs" : "Unknown";
    }

    private static bool LooksLikeSpecificMotorcycleQuestion(string query, string subject)
    {
        if (!subject.StartsWith("Motorcycle", StringComparison.Ordinal))
        {
            return false;
        }

        return ExtractYear(query) != null ||
               ContainsAny(query, SpecsTerms) ||
               ContainsAny(query, MaintenanceTerms) ||
               ContainsAny(query, ComparisonTerms);
    }

    private static DetectedBikeTerms DetectBikeTerms(string query, IReadOnlyList<BikeModel> bikeModels)
    {
        var year = ExtractYear(query);
        var tokens = Tokenize(query);
        var knownMakes = bikeModels
            .Select(b => b.Make)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var make = knownMakes.FirstOrDefault(m => tokens.Contains(m, StringComparer.OrdinalIgnoreCase));
        var modelTokens = tokens
            .Where(t => !StopWords.Contains(t))
            .Where(t => year?.ToString() != t)
            .Where(t => make == null || !string.Equals(t, make, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var modelTerm = string.Join(' ', modelTokens).Trim();
        return new DetectedBikeTerms(query, year, make, modelTerm);
    }

    private static List<ScoredCandidate> ScoreBikeCandidates(DetectedBikeTerms detected, IReadOnlyList<BikeModel> bikeModels)
    {
        var queryCompact = Compact(detected.Query);
        var modelCompact = Compact(detected.ModelTerm);
        var hasV4LikeTerm = HasV4LikeTerm(modelCompact);
        var candidates = new List<ScoredCandidate>();

        foreach (var bike in bikeModels)
        {
            var displayName = $"{bike.Year} {bike.Make} {bike.Model}".Trim();
            var canonicalCompact = Compact($"{bike.Make} {bike.Model} {bike.Year}");
            var makeModelCompact = Compact($"{bike.Make} {bike.Model}");
            var bikeModelCompact = Compact(bike.Model);
            var yearMatches = detected.Year == null || detected.Year == bike.Year;
            var makeMatches = detected.Make == null || string.Equals(detected.Make, bike.Make, StringComparison.OrdinalIgnoreCase);

            var aliasExact = GetAliases(bike).Any(alias =>
            {
                var aliasCompact = Compact(alias);
                return aliasCompact.Length > 0 &&
                       yearMatches &&
                       makeMatches &&
                       (queryCompact.Contains(aliasCompact, StringComparison.Ordinal) ||
                        string.Equals(aliasCompact, modelCompact, StringComparison.Ordinal));
            });

            var exact = yearMatches &&
                        makeMatches &&
                        (queryCompact.Contains(canonicalCompact, StringComparison.Ordinal) ||
                         queryCompact.Contains(makeModelCompact, StringComparison.Ordinal) ||
                         aliasExact);

            double score = 0;
            if (detected.Year == bike.Year) score += 0.20;
            if (detected.Make != null && string.Equals(detected.Make, bike.Make, StringComparison.OrdinalIgnoreCase)) score += 0.35;
            if (modelCompact.Length > 0 && bikeModelCompact.Contains(modelCompact, StringComparison.Ordinal)) score += 0.30;
            if (modelCompact.Length > 0 && modelCompact.Contains(bikeModelCompact, StringComparison.Ordinal)) score += 0.25;
            if (modelCompact.Length > 0) score += 0.35 * Similarity(modelCompact, bikeModelCompact);
            if (hasV4LikeTerm && HasV4LikeTerm(bikeModelCompact)) score += 0.15;
            if (exact) score = Math.Max(score, 1.0);

            if (score > 0)
            {
                candidates.Add(new ScoredCandidate(displayName, score, exact, "Closest canonical bike model match."));
            }
        }

        return candidates;
    }

    private static string[] BuildGraphSearchTerms(DetectedBikeTerms detected)
    {
        var terms = new List<string>();
        if (!string.IsNullOrWhiteSpace(detected.ModelTerm))
        {
            terms.Add(detected.ModelTerm);
            if (HasV4LikeTerm(Compact(detected.ModelTerm)))
            {
                terms.Add("V4");
                terms.Add("RSV4");
            }
        }

        if (!string.IsNullOrWhiteSpace(detected.Make) && !string.IsNullOrWhiteSpace(detected.ModelTerm))
        {
            terms.Add($"{detected.Make} {detected.ModelTerm}");
        }

        return terms.ToArray();
    }

    private static double ScoreGraphCandidate(DetectedBikeTerms detected, GraphNode node)
    {
        var score = 0.35 * Similarity(Compact(detected.ModelTerm), Compact(node.Name));
        if (detected.Year != null && node.Name.Contains(detected.Year.Value.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            score += 0.20;
        }

        if (!string.IsNullOrWhiteSpace(detected.Make) && node.Name.Contains(detected.Make, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.25;
        }

        if (HasV4LikeTerm(Compact(detected.ModelTerm)) && HasV4LikeTerm(Compact(node.Name)))
        {
            score += 0.15;
        }

        return score;
    }

    private static bool IsExactGraphMatch(DetectedBikeTerms detected, GraphNode node)
    {
        var nodeCompact = Compact(node.Name);
        var queryCompact = Compact(detected.Query);
        return queryCompact.Contains(nodeCompact, StringComparison.Ordinal) ||
               nodeCompact.Contains(queryCompact, StringComparison.Ordinal);
    }

    private static QuestionValidationResult Answer(string subject, string normalizedQuery, double confidence) => new()
    {
        Subject = subject,
        Confidence = confidence,
        NormalizedQuery = normalizedQuery,
        MaySearch = true,
        ResponseType = "Answer"
    };

    private static QuestionValidationResult Clarification(
        string subject,
        string question,
        QueryClarificationSuggestion[] suggestions) => new()
    {
        Subject = subject,
        Confidence = 0.90,
        NormalizedQuery = string.Empty,
        MaySearch = false,
        ResponseType = "Clarification",
        ClarificationQuestion = question,
        Suggestions = suggestions
    };

    private static string[] Tokenize(string value)
    {
        return value
            .Split([' ', '\t', '\r', '\n', ',', '.', '?', '!', ':', ';', '/', '\\', '-', '_', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToArray();
    }

    private static int? ExtractYear(string value)
    {
        foreach (var token in Tokenize(value))
        {
            if (token.Length == 4 && int.TryParse(token, out var year) && year is >= 1900 and <= 2100)
            {
                return year;
            }
        }

        return null;
    }

    private static bool ContainsAny(string value, IEnumerable<string> terms) =>
        terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> GetAliases(BikeModel bike)
    {
        if (string.IsNullOrWhiteSpace(bike.Aliases))
        {
            return [];
        }

        return bike.Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string Compact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static bool HasV4LikeTerm(string compactValue) =>
        compactValue.Contains("V4", StringComparison.Ordinal) ||
        compactValue.Contains("VR4", StringComparison.Ordinal) ||
        compactValue.Contains("RSV4", StringComparison.Ordinal);

    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1;
        }

        var distance = LevenshteinDistance(left, right);
        return 1.0 - (double)distance / Math.Max(left.Length, right.Length);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var costs = new int[right.Length + 1];
        for (var j = 0; j < costs.Length; j++)
        {
            costs[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            costs[0] = i;
            var corner = i - 1;
            for (var j = 1; j <= right.Length; j++)
            {
                var upper = costs[j];
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                costs[j] = Math.Min(Math.Min(costs[j - 1] + 1, costs[j] + 1), corner + cost);
                corner = upper;
            }
        }

        return costs[right.Length];
    }

    private sealed record DetectedBikeTerms(string Query, int? Year, string? Make, string ModelTerm)
    {
        public bool HasSpecificBikeSignal =>
            Year != null ||
            !string.IsNullOrWhiteSpace(Make) ||
            !string.IsNullOrWhiteSpace(ModelTerm);
    }

    private sealed record ScoredCandidate(string DisplayName, double Score, bool IsExact, string Reason);
}
