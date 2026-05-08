using OpenAI.Responses;

namespace MotorcycleRAG.AgentProvisioning.Azure;

/// <summary>
/// Static definitions for all four Foundry agents: system prompts (verbatim from
/// <c>specs/develop/contracts/</c>) and tool schemas used by
/// <see cref="AgentProvisioningService"/> when creating or updating agents.
/// </summary>
public static class AgentDefinitions
{
    // -------------------------------------------------------------------------
    // Model names
    // -------------------------------------------------------------------------

    public const string Qwen36DeploymentName = "qwen3-6-35b-a3b-fp8";
    public const string Qwen35DeploymentName = "qwen3-5-35b-a3b";
    public const string OrchestratorFallbackModel = "gpt-4-1";
    public const string SubAgentModel = "gpt-4-1-mini";

    public static readonly string[] DefaultOrchestratorModelCandidates =
    [
        Qwen36DeploymentName,
        Qwen35DeploymentName,
        OrchestratorFallbackModel
    ];

    // -------------------------------------------------------------------------
    // Agent names — used as the stable lookup key for upsert logic
    // -------------------------------------------------------------------------

    public const string OrchestratorAgentName = "MCR-OrchestratorAgent";
    public const string VectorSearchAgentName = "MCR-VectorSearchAgent";
    public const string WebSearchAgentName = "MCR-WebSearchAgent";
    public const string PDFSearchAgentName = "MCR-PDFSearchAgent";

    // -------------------------------------------------------------------------
    // System prompts — copied verbatim from specs/develop/contracts/
    // -------------------------------------------------------------------------

    public static readonly string OrchestratorSystemPrompt =
        """
        You are a motorcycle knowledge assistant. Your job is to answer user questions about motorcycles
        accurately and completely by coordinating three specialised search agents.

        You have three tools:

        - vector_search: Searches the internal motorcycle knowledge base (indexed manuals, specs, reviews).
          Use this first for any motorcycle question.

        - web_search: Searches trusted motorcycle websites for current, broad, or opinion-based information.
          Use this when vector_search results feel incomplete, the question is about current models, prices,
          trends, comparisons, or recommendations, or when a second perspective would meaningfully improve
          the answer. Do NOT use this for every query — only when it adds real value.

        - pdf_search: Searches technical motorcycle manuals stored as PDFs.
          Use this for precise technical questions: torque specs, valve clearances, service intervals,
          wiring diagrams, fault codes.

        Decision guidance:
        - Always start with vector_search.
        - After reviewing results, decide if they are sufficient to answer well.
        - If results feel thin, outdated, or the question needs broader context, call web_search.
        - If the question is clearly technical or maintenance-related, also call pdf_search.
        - Stop calling tools once you have enough information to give a thorough answer.
        - If you reach the tool call limit, synthesise the best answer from what you have gathered.

        Answer in clear markdown. Cite the source of key facts where possible.
        """;

    public static readonly string VectorSearchSystemPrompt =
        """
        You are a vector search specialist for the internal motorcycle knowledge base. Given a user query,
        search the indexed knowledge base using the execute_azure_search tool.

        Process:
        - Analyse the user query and identify the most effective search terms.
        - If the query contains typos or ambiguity, normalise it to standard motorcycle terminology before searching.
        - Call execute_azure_search with the refined query and an appropriate max_results count.
        - If the initial results are sparse or off-topic, refine the query and search again (max 2 attempts).
        - Synthesise the returned results into a concise, factual summary.
        - Return the summary with document source references where available.

        If the knowledge base returns no relevant results, state this clearly.
        """;

    public static readonly string WebSearchSystemPrompt =
        """
        You are a web search specialist for motorcycle information. Given a user query, you coordinate
        a structured web search using three tools:

        1. get_trusted_sources — retrieves the list of trusted motorcycle websites configured by the admin.
           Call this first to know which sources are available.

        2. fetch_web_content(url, search_term) — fetches and extracts content from a source URL using the
           search term. Call this for each relevant source with the most targeted search term derived from
           the user query. You may call this multiple times with refined terms if initial results are poor.

        3. score_content(content, source_url, trust_tier) — scores content quality and applies trust weighting.
           Call this for each piece of fetched content before including it in your result.

        Process:
        - Call get_trusted_sources first.
        - Derive 1-3 targeted search terms from the user query.
        - For each source, call fetch_web_content with the best matching term.
        - Score all retrieved content via score_content.
        - Synthesise the highest-scoring, most relevant content into a coherent summary.
        - Return a clear summary with source attributions.

        If no sources are available or all fetches return empty content, state clearly that no web results
        were found rather than fabricating information.
        """;

    public static readonly string PDFSearchSystemPrompt =
        """
        You are a technical manual search specialist. Given a user query about motorcycle technical details,
        search the indexed PDF manuals using the search_pdf_index tool.

        Process:
        - Identify the specific technical information being requested (spec value, procedure, diagram reference).
        - Translate the query into precise technical terminology used in service manuals.
        - Call search_pdf_index with the refined technical query.
        - If results are insufficient, try alternative technical phrasings (max 2 attempts).
        - Extract and return the specific technical values or procedures found, with document and page references.

        Always return exact values where available (e.g., "Front fork oil: 446ml ± 2.5ml per leg — Honda
        CBR600RR 2005 Service Manual, Chapter 13, p.13-8") rather than approximations.
        If no relevant manual content is found, state this clearly.
        """;

    // -------------------------------------------------------------------------
    // Tool definitions — JSON schemas matching specs/develop/data-model.md
    // -------------------------------------------------------------------------

    public static readonly ResponseTool[] OrchestratorTools =
    [
        CreateFunctionTool("vector_search", "Searches the internal motorcycle knowledge base (indexed manuals, specs, reviews).", new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "The search query" },
                max_results = new { type = "integer", description = "Maximum number of results to return", @default = 10 }
            },
            required = new[] { "query" }
        }),
        CreateFunctionTool("web_search", "Searches trusted motorcycle websites for current, broad, or opinion-based information.", new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "The search query" },
                max_results = new { type = "integer", description = "Maximum number of results to return", @default = 5 }
            },
            required = new[] { "query" }
        }),
        CreateFunctionTool("pdf_search", "Searches technical motorcycle manuals stored as PDFs.", new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "The technical search query" },
                max_results = new { type = "integer", description = "Maximum number of results to return", @default = 5 }
            },
            required = new[] { "query" }
        })
    ];

    public static readonly ResponseTool[] VectorSearchTools =
    [
        CreateFunctionTool("execute_azure_search", "Executes a query against the Azure AI Search index for the motorcycle knowledge base.", new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "The search query" },
                max_results = new { type = "integer", description = "Maximum number of results to return", @default = 10 }
            },
            required = new[] { "query" }
        })
    ];

    public static readonly ResponseTool[] WebSearchTools =
    [
        CreateFunctionTool("get_trusted_sources", "Retrieves the list of trusted motorcycle websites configured by the admin.", new
        {
            type = "object",
            properties = new { },
            required = Array.Empty<string>()
        }),
        CreateFunctionTool("fetch_web_content", "Fetches and extracts content from a source URL using a search term.", new
        {
            type = "object",
            properties = new
            {
                url = new { type = "string", description = "The URL to fetch content from" },
                search_term = new { type = "string", description = "The search term to use for content extraction" }
            },
            required = new[] { "url", "search_term" }
        }),
        CreateFunctionTool("score_content", "Scores content quality and applies trust weighting based on source tier.", new
        {
            type = "object",
            properties = new
            {
                content = new { type = "string", description = "The content to score" },
                source_url = new { type = "string", description = "The URL of the source" },
                trust_tier = new { type = "integer", description = "Trust tier of the source (1=highest, 5=lowest)" }
            },
            required = new[] { "content", "source_url", "trust_tier" }
        })
    ];

    public static readonly ResponseTool[] PDFSearchTools =
    [
        CreateFunctionTool("search_pdf_index", "Searches the indexed PDF motorcycle manuals.", new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "The technical search query" },
                max_results = new { type = "integer", description = "Maximum number of results to return", @default = 5 }
            },
            required = new[] { "query" }
        })
    ];

    private static ResponseTool CreateFunctionTool(string name, string description, object parameters)
        => ResponseTool.CreateFunctionTool(
            name,
            BinaryData.FromObjectAsJson(parameters),
            false,
            description);
}
