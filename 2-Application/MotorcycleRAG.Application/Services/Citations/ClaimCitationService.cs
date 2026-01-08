using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Services.Citations;

/// <summary>
/// Extracts factual claims from text and generates proper citations
/// </summary>
public class ClaimCitationService
{
    private readonly IAzureOpenAIClient _openAIClient;
    private readonly ILogger<ClaimCitationService> _logger;

    public ClaimCitationService(
        IAzureOpenAIClient openAIClient,
        ILogger<ClaimCitationService> logger)
    {
        _openAIClient = openAIClient;
        _logger = logger;
    }

    public async Task<CitedResponse> GenerateCitedResponseAsync(
        string originalAnswer,
        SearchResult[] sources,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalAnswer);
        ArgumentNullException.ThrowIfNull(sources);

        try
        {
            var claims = await IdentifyFactualClaimsAsync(originalAnswer, cancellationToken);

            if (claims == null || claims.Length == 0)
            {
                return new CitedResponse
                {
                    Answer = originalAnswer,
                    Results = sources
                };
            }

            var claimEvidences = MapClaimsToEvidence(claims, sources);
            var citedAnswer = await GenerateAnswerWithCitationsAsync(originalAnswer, claims, claimEvidences, cancellationToken);
            var citedResults = EnsureAllResultsHaveCitations(sources);

            var (isCompliant, issues) = EnforceClaimCitationPolicy(claims, claimEvidences);
            if (!isCompliant)
            {
                citedAnswer = ApplyPolicyCorrections(citedAnswer, issues);
            }

            return new CitedResponse
            {
                Answer = citedAnswer,
                Results = citedResults
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate cited response");
            return new CitedResponse { Answer = originalAnswer, Results = sources };
        }
    }

    private async Task<string[]> IdentifyFactualClaimsAsync(string answer, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = $"""
Analyze the following answer and extract all factual claims that require citation.
Return only the factual statements as a JSON array of strings.
Do not include opinions, qualifiers, or introductory phrases.

Answer to analyze:
{answer}

Factual claims (JSON array):
""";

            var claimsJson = await _openAIClient.GetChatCompletionAsync("gpt-4o-mini", prompt, cancellationToken);

            if (string.IsNullOrWhiteSpace(claimsJson))
                return Array.Empty<string>();

            // Simple JSON parsing (in production, use proper JSON parser)
            if (claimsJson.StartsWith('[') && claimsJson.EndsWith(']'))
            {
                var claims = claimsJson
                    .Trim(TrimChars)
                    .Split(JsonArraySeparators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim('"', ' ', '\\'))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToArray();

                return claims;
            }

            // Fallback: extract sentences that look like facts
            return ExtractFactLikeSentences(answer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to identify factual claims using AI, falling back to simple extraction");
            return ExtractFactLikeSentences(answer);
        }
    }

    private List<ClaimEvidence> MapClaimsToEvidence(string[] claims, SearchResult[] results)
    {
        var claimEvidences = new List<ClaimEvidence>();

        foreach (var claim in claims)
        {
            var matchingSources = FindMatchingSourcesForClaim(claim, results);
            var evidence = new ClaimEvidence();

            if (matchingSources.Any())
            {
                evidence.Citations.AddRange(CreateCitationsFromSources(matchingSources, claim));

                // Add to results with enhanced citations
                foreach (var source in matchingSources.Where(s => evidence.Results.All(r => r.Id != s.Id)))
                {
                    evidence.Results.Add(source);
                }
            }
            else
            {
                _logger.LogWarning("No evidence found for claim: {Claim}", claim);
            }
            claimEvidences.Add(evidence);
        }

        return claimEvidences;
    }

    private List<SearchResult> FindMatchingSourcesForClaim(string claim, SearchResult[] results)
    {
        return results.Where(result =>
            result.Content.Contains(claim, StringComparison.OrdinalIgnoreCase) ||
            ExtractKeyTerms(result.Content).Intersect(ExtractKeyTerms(claim), StringComparer.OrdinalIgnoreCase).Any()
        ).ToList();
    }

    private string[] ExtractKeyTerms(string text)
    {
        return text.Split(KeyTermSeparators, StringSplitOptions.RemoveEmptyEntries)
                   .Select(t => t.Trim())
                   .Where(t => t.Length > 3 && !string.IsNullOrWhiteSpace(t))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToArray();
    }

    private List<Citation> CreateCitationsFromSources(List<SearchResult> sources, string claim)
    {
        var citations = new List<Citation>();

        foreach (var source in sources)
        {
            var citation = new Citation
            {
                SourceType = MapAgentTypeToCitationSourceType(source.Source.AgentType),
                SourceName = source.Source.SourceName,
                SourceUrl = source.Source.SourceUrl?.ToString(),
                ConfidenceScore = source.RelevanceScore,
                Verified = source.RelevanceScore >= 0.8f,
                VerificationMethod = source.RelevanceScore >= 0.8f ? "High relevance score" : "Content matching",
                VerifiedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    { "MatchedClaim", claim },
                    { "RelevanceScore", source.RelevanceScore },
                    { "ContentPreview", source.Content.Length > 100 ? source.Content[..100] + "…" : source.Content }
                }
            };

            citation.Locator = CreateLocatorForSource(source);
            citations.Add(citation);
        }

        return citations;
    }

    private CitationSourceType MapAgentTypeToCitationSourceType(SearchAgentType agentType)
    {
        return agentType switch
        {
            SearchAgentType.VectorSearch => CitationSourceType.Dataset,
            SearchAgentType.WebSearch => CitationSourceType.Website,
            SearchAgentType.PDFSearch => CitationSourceType.ManualPdf,
            _ => CitationSourceType.Dataset
        };
    }

    private object CreateLocatorForSource(SearchResult source)
    {
        return source.Source.AgentType switch
        {
            SearchAgentType.VectorSearch => new DatasetCitationLocator
            {
                DatasetName = source.Source.SourceName,
                RecordId = source.Id,
                FieldName = "Content",
                DataSourceUrl = source.Source.SourceUrl?.ToString() ?? string.Empty,
                RetrievalTimestamp = source.GeneratedAt
            },

            SearchAgentType.WebSearch => new WebsiteCitationLocator
            {
                Url = source.Source.SourceUrl?.ToString() ?? string.Empty,
                Title = source.Source.SourceName,
                AccessedDate = source.GeneratedAt,
                TrustTier = WebsiteTrustTier.Standard
            },

            SearchAgentType.PDFSearch => CreateManualPdfLocator(source),

            _ => new DatasetCitationLocator
            {
                DatasetName = source.Source.SourceName,
                RecordId = source.Id,
                DataSourceUrl = source.Source.SourceUrl?.ToString() ?? string.Empty,
                RetrievalTimestamp = source.GeneratedAt
            }
        };
    }

    private ManualPdfCitationLocator CreateManualPdfLocator(SearchResult source)
    {
        var locator = new ManualPdfCitationLocator
        {
            DocumentId = source.Source.DocumentId,
            Title = source.Source.SourceName,
            PageNumber = 1,
            SourceUrl = source.Source.SourceUrl?.ToString() ?? string.Empty,
            PublicationDate = source.Source.LastUpdated
        };

        if (source.Metadata == null || source.Metadata.Count == 0) return locator;

        MapMetadataToLocator(source.Metadata, locator);
        MapLegacyLocator(source.Metadata, locator);

        return locator;
    }

    private static void MapMetadataToLocator(IReadOnlyDictionary<string, object> metadata, ManualPdfCitationLocator locator)
    {
        if (metadata.TryGetValue("PageNumber", out var pn) && pn is int pageNum && pageNum > 0)
            locator.PageNumber = pageNum;

        if (metadata.TryGetValue("PageRange", out var pr) && pr is string pageRange)
            locator.PageRange = pageRange;

        if (metadata.TryGetValue("PrimarySection", out var ps) && ps is string primarySection)
            locator.PrimarySection = primarySection;

        if (metadata.TryGetValue("SectionLevel", out var sl) && sl is int sectionLevel)
            locator.SectionLevel = sectionLevel;

        if (metadata.TryGetValue("SectionHeadings", out var sh) && sh is string[] sectionHeadings && sectionHeadings.Length > 0)
        {
            locator.SectionHeadings = sectionHeadings;
            locator.PrimarySection = sectionHeadings[0];
        }

        if (metadata.TryGetValue("TableCaption", out var tc) && tc is string tableCaption)
            locator.TableCaption = tableCaption;

        if (metadata.TryGetValue("ChunkIndex", out var ci) && ci is int chunkIndex)
            locator.ChunkIndex = chunkIndex;
    }

    private static void MapLegacyLocator(IReadOnlyDictionary<string, object> metadata, ManualPdfCitationLocator locator)
    {
        if (metadata.TryGetValue("AdditionalProperties", out var ap) && ap is Dictionary<string, object> props &&
            props.TryGetValue("Locator", out var legacy) && legacy is string legacyLoc && !string.IsNullOrWhiteSpace(legacyLoc))
        {
            locator.Section = legacyLoc;
        }
    }

    private async Task<string> GenerateAnswerWithCitationsAsync(
        string originalAnswer,
        string[] claims,
        List<ClaimEvidence> claimEvidences,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build citation markers for each claim
            var citationMarkers = new Dictionary<string, string>();

            for (int i = 0; i < claims.Length; i++)
            {
                var claim = claims[i];
                var citations = claimEvidences[i].Citations;

                citationMarkers[claim] = citations.Count != 0
                    ? string.Join(",", citations.Select((_, idx) => $"[{i + 1}-{idx + 1}]"))
                    : "[Note: Could not verify this information]";
            }

            // Generate final answer with citations
            var finalAnswer = originalAnswer;

            foreach (var claim in claims)
            {
                if (citationMarkers.TryGetValue(claim, out var marker))
                {
                    finalAnswer = finalAnswer.Replace(claim, $"{claim} {marker}");
                }
            }

            // Add citation references at the end
            finalAnswer += "\n\n### Sources and Citations\n";

            for (int i = 0; i < claims.Length; i++)
            {
                var claim = claims[i];
                var citations = claimEvidences[i].Citations;

                if (citations.Count == 0) continue;

                finalAnswer = string.Concat(finalAnswer, "\n**Claim ", (i + 1), "**: ", claim, "\n");

                for (int j = 0; j < citations.Count; j++)
                {
                    var citation = citations[j];
                    finalAnswer = string.Concat(finalAnswer, "  - [", (i + 1), "-", (j + 1), "] ", citation.SourceName);

                    if (citation.SourceUrl != null)
                    {
                        finalAnswer = string.Concat(finalAnswer, " ([URL](", citation.SourceUrl, "))");
                    }

                    finalAnswer = string.Concat(finalAnswer, (citation.Verified ? " ✓ Verified" : " ⚠ Requires verification"), "\n");
                }
            }

            return finalAnswer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate cited answer");
            return originalAnswer;
        }
    }

    private (bool IsCompliant, List<ClaimCitationIssue> Issues) EnforceClaimCitationPolicy(
        string[] claims, List<ClaimEvidence> claimEvidences)
    {
        var issues = new List<ClaimCitationIssue>();

        for (int i = 0; i < claims.Length; i++)
        {
            var claim = claims[i];
            var citations = claimEvidences[i].Citations;

            if (citations.Count == 0)
            {
                if (!IsQualifiedClaim(claim) && !IsCommonKnowledge(claim))
                {
                    issues.Add(new ClaimCitationIssue
                    {
                        Claim = claim,
                        IssueType = ClaimCitationIssueType.MissingCitation,
                        Suggestion = "Add citation, qualify the statement, or omit if unverifiable"
                    });
                }
            }
            else
            {
                var hasHighQualityCitation = citations.Any(c => c.Verified || c.ConfidenceScore >= 0.8f);

                if (!hasHighQualityCitation)
                {
                    issues.Add(new ClaimCitationIssue
                    {
                        Claim = claim,
                        IssueType = ClaimCitationIssueType.LowQualityCitation,
                        Suggestion = "Add verification or qualify the statement"
                    });
                }
            }
        }

        return (issues.Count == 0, issues);
    }

    private string ApplyPolicyCorrections(string originalAnswer, List<ClaimCitationIssue> issues)
    {
        var correctedAnswer = originalAnswer;

        foreach (var issue in issues)
        {
            if (issue.IssueType == ClaimCitationIssueType.MissingCitation && correctedAnswer.Contains(issue.Claim))
            {
                var qualifiedClaim = string.Concat("[Note: Could not verify] ", issue.Claim);
                correctedAnswer = correctedAnswer.Replace(issue.Claim, qualifiedClaim);
            }
            else if (issue.IssueType == ClaimCitationIssueType.LowQualityCitation && correctedAnswer.Contains(issue.Claim))
            {
                var qualifiedClaim = string.Concat(issue.Claim, " [Note: Requires verification]");
                correctedAnswer = correctedAnswer.Replace(issue.Claim, qualifiedClaim);
            }
        }

        return correctedAnswer;
    }

    private bool IsQualifiedClaim(string claim)
    {
        return QualifyingTerms.Any(term => claim.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsCommonKnowledge(string claim)
    {
        var words = claim.Split(WordSplitSeparators, StringSplitOptions.RemoveEmptyEntries)
                         .Select(w => w.ToUpperInvariant().Trim(CleanTrimChars))
                         .Where(w => w.Length > 2)
                         .ToArray();

        if (words.Length == 0) return false;

        var commonWordCount = words.Count(w => CommonKnowledgePatterns.Contains(w));
        return commonWordCount >= words.Length * 0.7;
    }

    private SearchResult[] EnsureAllResultsHaveCitations(SearchResult[] results)
    {
        foreach (var result in results)
        {
            result.Source.Citation ??= new Citation
            {
                SourceType = MapAgentTypeToCitationSourceType(result.Source.AgentType),
                SourceName = result.Source.SourceName,
                SourceUrl = result.Source.SourceUrl?.ToString(),
                ConfidenceScore = result.RelevanceScore,
                Verified = result.RelevanceScore >= 0.8f,
                VerificationMethod = "Automated citation generation",
                VerifiedAt = DateTime.UtcNow,
                Locator = CreateLocatorForSource(result)
            };
        }

        return results;
    }

    // Constants and helper fields
    private static readonly string[] JsonArraySeparators = { "\",\"" };
    private static readonly char[] TrimChars = { '[', ']', ' ', '\\', '"' };
    private static readonly char[] KeyTermSeparators = { ' ', '.', ',', ';', ':', '(', ')', '[', ']', '{', '}', '\\', '/', '-', '_' };
    private static readonly char[] WordSplitSeparators = { ' ' };
    private static readonly char[] CleanTrimChars = { '.', ',', ';', ':', '!', '?' };
    private static readonly string[] CommonKnowledgePatterns = {
        "MOTORCYCLE", "VEHICLE", "ENGINE", "WHEELS", "TWO-WHEELED", "TRANSPORTATION",
        "RIDE", "DRIVER", "PASSENGER", "ROAD", "STREET", "SPEED", "POWER"
    };
    private static readonly string[] QualifyingTerms = {
        "may", "might", "could", "possibly", "potentially", "likely", "probably",
        "often", "sometimes", "typically", "generally", "usually", "can", "tend to"
    };

    private string[] ExtractFactLikeSentences(string answer)
    {
        return answer.Split(SentenceSeparators, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Where(s => FactIndicators.Any(indicator => s.Contains(indicator, StringComparison.OrdinalIgnoreCase)) || s.Any(char.IsDigit))
                        .ToArray();
    }

    private static readonly char[] SentenceSeparators = { '.', '!', '?' };
    private static readonly string[] FactIndicators = { " is ", " has ", " are ", " was ", " were " };
}

public class CitedResponse
{
    public string Answer { get; init; } = string.Empty;
    public SearchResult[] Results { get; init; } = Array.Empty<SearchResult>();
}

public class ClaimEvidence
{
    public List<Citation> Citations { get; } = new();
    public List<SearchResult> Results { get; } = new();
}