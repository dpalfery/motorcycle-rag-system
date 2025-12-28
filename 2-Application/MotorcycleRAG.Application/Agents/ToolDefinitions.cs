using System.Text.Json;
using MotorcycleRAG.Contracts.Models;

namespace MotorcycleRAG.Application.Agents;

/// <summary>
/// Defines available tools that LLM-based agents can call.
/// Tools are functions that agents invoke to search, retrieve information, or perform actions.
/// </summary>
public static class ToolDefinitions
{
    /// <summary>
    /// Tool definition for vector search (hybrid keyword + semantic search)
    /// </summary>
    public static ToolDefinition GetVectorSearchTool()
    {
        return new ToolDefinition
        {
            Name = "vector_search",
            DisplayName = "Vector Search",
            Description = "Search the motorcycle database using hybrid vector/keyword search. Returns relevant motorcycle specifications and documents.",
            Parameters = new ToolParameter
            {
                Type = "object",
                Properties = new Dictionary<string, ToolPropertyDefinition>
                {
                    ["query"] = new ToolPropertyDefinition
                    {
                        Type = "string",
                        Description = "The search query for motorcycle information (e.g., 'CBR 1000 specifications', 'Honda engine performance')",
                        IsRequired = true
                    },
                    ["maxResults"] = new ToolPropertyDefinition
                    {
                        Type = "integer",
                        Description = "Maximum number of results to return (default: 10, max: 50)",
                        IsRequired = false,
                        Default = 10
                    },
                    ["minRelevanceScore"] = new ToolPropertyDefinition
                    {
                        Type = "number",
                        Description = "Minimum relevance score threshold (0.0-1.0, default: 0.5)",
                        IsRequired = false,
                        Default = 0.5
                    }
                }
            }
        };
    }

    /// <summary>
    /// Tool definition for web search
    /// </summary>
    public static ToolDefinition GetWebSearchTool()
    {
        return new ToolDefinition
        {
            Name = "web_search",
            DisplayName = "Web Search",
            Description = "Search the web for external motorcycle information from trusted sources. Useful for current information, reviews, and supplementary data.",
            Parameters = new ToolParameter
            {
                Type = "object",
                Properties = new Dictionary<string, ToolPropertyDefinition>
                {
                    ["query"] = new ToolPropertyDefinition
                    {
                        Type = "string",
                        Description = "The web search query for motorcycle information",
                        IsRequired = true
                    },
                    ["maxResults"] = new ToolPropertyDefinition
                    {
                        Type = "integer",
                        Description = "Maximum number of results to return (default: 5, max: 20)",
                        IsRequired = false,
                        Default = 5
                    },
                    ["trustedSourcesOnly"] = new ToolPropertyDefinition
                    {
                        Type = "boolean",
                        Description = "Restrict search to trusted motorcycle sources only (default: true)",
                        IsRequired = false,
                        Default = true
                    }
                }
            }
        };
    }

    /// <summary>
    /// Tool definition for PDF/manual search
    /// </summary>
    public static ToolDefinition GetPdfSearchTool()
    {
        return new ToolDefinition
        {
            Name = "pdf_search",
            DisplayName = "PDF Manual Search",
            Description = "Search technical documentation and motorcycle manuals stored as PDFs. Best for detailed technical specifications and maintenance procedures.",
            Parameters = new ToolParameter
            {
                Type = "object",
                Properties = new Dictionary<string, ToolPropertyDefinition>
                {
                    ["query"] = new ToolPropertyDefinition
                    {
                        Type = "string",
                        Description = "Search query for technical documentation",
                        IsRequired = true
                    },
                    ["maxResults"] = new ToolPropertyDefinition
                    {
                        Type = "integer",
                        Description = "Maximum number of results to return (default: 5, max: 20)",
                        IsRequired = false,
                        Default = 5
                    },
                    ["documentType"] = new ToolPropertyDefinition
                    {
                        Type = "string",
                        Description = "Filter by document type: 'manual', 'specification', 'maintenance', or 'all' (default: 'all')",
                        IsRequired = false,
                        Default = "all"
                    }
                }
            }
        };
    }

    /// <summary>
    /// Tool definition for query planning
    /// </summary>
    public static ToolDefinition GetQueryPlanningTool()
    {
        return new ToolDefinition
        {
            Name = "plan_search_strategy",
            DisplayName = "Plan Search Strategy",
            Description = "Plan the search strategy for a query by breaking it into sub-queries and determining which search tools to use.",
            Parameters = new ToolParameter
            {
                Type = "object",
                Properties = new Dictionary<string, ToolPropertyDefinition>
                {
                    ["query"] = new ToolPropertyDefinition
                    {
                        Type = "string",
                        Description = "The original user query to analyze and plan",
                        IsRequired = true
                    },
                    ["includeSemantic"] = new ToolPropertyDefinition
                    {
                        Type = "boolean",
                        Description = "Include semantic search in the plan (default: true)",
                        IsRequired = false,
                        Default = true
                    },
                    ["includeWeb"] = new ToolPropertyDefinition
                    {
                        Type = "boolean",
                        Description = "Include web search in the plan (default: false)",
                        IsRequired = false,
                        Default = false
                    },
                    ["includePdf"] = new ToolPropertyDefinition
                    {
                        Type = "boolean",
                        Description = "Include PDF search in the plan (default: false)",
                        IsRequired = false,
                        Default = false
                    }
                }
            }
        };
    }

    /// <summary>
    /// Get all available tool definitions
    /// </summary>
    public static IReadOnlyList<ToolDefinition> GetAllTools()
    {
        return new[]
        {
            GetVectorSearchTool(),
            GetWebSearchTool(),
            GetPdfSearchTool(),
            GetQueryPlanningTool()
        };
    }
}

/// <summary>
/// Defines a tool that agents can invoke
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ToolParameter Parameters { get; set; } = new();
}

/// <summary>
/// Defines parameters for a tool
/// </summary>
public class ToolParameter
{
    public string Type { get; set; } = "object";
    public Dictionary<string, ToolPropertyDefinition> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

/// <summary>
/// Defines a single property of a tool parameter
/// </summary>
public class ToolPropertyDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public object? Default { get; set; }
    public List<string>? Enum { get; set; }
    public object? Minimum { get; set; }
    public object? Maximum { get; set; }
}

/// <summary>
/// Represents a tool call invoked by an agent
/// </summary>
public class ToolCall
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object> Arguments { get; set; } = new();
    public string CallId { get; set; } = Guid.NewGuid().ToString();
    public DateTime InvokedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Get a typed argument value
    /// </summary>
    public T? GetArgument<T>(string name)
    {
        if (!Arguments.TryGetValue(name, out var value))
            return default;

        if (value is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element);
        }

        return (T?)Convert.ChangeType(value, typeof(T));
    }

    /// <summary>
    /// Get a string argument
    /// </summary>
    public string GetStringArgument(string name, string defaultValue = "")
    {
        return GetArgument<string>(name) ?? defaultValue;
    }

    /// <summary>
    /// Get an integer argument
    /// </summary>
    public int GetIntArgument(string name, int defaultValue = 0)
    {
        var value = GetArgument<int?>(name);
        return value ?? defaultValue;
    }

    /// <summary>
    /// Get a boolean argument
    /// </summary>
    public bool GetBoolArgument(string name, bool defaultValue = false)
    {
        var value = GetArgument<bool?>(name);
        return value ?? defaultValue;
    }
}

/// <summary>
/// Result of executing a tool
/// </summary>
public class ToolExecutionResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}
