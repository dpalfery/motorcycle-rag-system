using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.EndToEndTests;

public class FakeEndToEndAgentOrchestrator : IAgentOrchestrator
{
    public Task<SearchResult[]> ExecuteSequentialSearchAsync(string query, SearchContext context)
    {
        var answer = BuildAnswer(query);
        var documentId = Guid.NewGuid().ToString("N");

        SearchResult[] results =
        [
            new SearchResult
            {
                Id = $"foundry-{documentId}",
                Content = answer,
                RelevanceScore = 1.0f,
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.QueryPlanner,
                    SourceName = "End-to-end test orchestrator",
                    DocumentId = documentId,
                    LastUpdated = DateTime.UtcNow
                },
                Metadata = new Dictionary<string, object>
                {
                    ["FoundryAnswer"] = true,
                    ["ThreadId"] = $"thread-{documentId}",
                    ["RunId"] = $"run-{documentId}"
                }
            },
            new SearchResult
            {
                Id = $"vector-{documentId}",
                Content = $"Structured motorcycle knowledge for: {query}",
                RelevanceScore = 0.95f,
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.VectorSearch,
                    SourceName = "Motorcycle Knowledge Base",
                    DocumentId = documentId,
                    LastUpdated = DateTime.UtcNow
                }
            },
            new SearchResult
            {
                Id = $"pdf-{documentId}",
                Content = $"Manual excerpt related to: {query}",
                RelevanceScore = 0.85f,
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.PDFSearch,
                    SourceName = "Motorcycle Service Manual",
                    DocumentId = documentId,
                    LastUpdated = DateTime.UtcNow
                }
            }
        ];

        return Task.FromResult(results);
    }

    public Task<string> GenerateResponseAsync(SearchResult[] results, string originalQuery)
    {
        ArgumentNullException.ThrowIfNull(results);
        return Task.FromResult(BuildAnswer(originalQuery));
    }

    public Task<SearchResult[]> OrchestrateSearchAsync(string query, SearchParameters options)
    {
        return ExecuteSequentialSearchAsync(query, new SearchContext
        {
            Preferences = new SearchPreferences
            {
                MaxResults = options.MaxResults,
                MinRelevanceScore = options.MinRelevanceScore
            }
        });
    }

    public IEnumerable<ISearchAgent> GetAvailableAgents()
    {
        return Array.Empty<ISearchAgent>();
    }

    private static string BuildAnswer(string query)
    {
        var answer = $"Test response for query: {query}. This answer references the requested motorcycle details and supporting sources.";

        if (query.Contains("compare", StringComparison.OrdinalIgnoreCase))
        {
            answer += " This comparison highlights the main differences between the motorcycles.";
        }

        return answer;
    }
}