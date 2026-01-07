namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Constants for health check tags
/// </summary>
internal static class HealthCheckTags
{
    /// <summary>
    /// Azure Search tags
    /// </summary>
    internal static readonly string[] AzureSearch = { "azure", "search" };

    /// <summary>
    /// Azure OpenAI tags
    /// </summary>
    internal static readonly string[] AzureOpenAI = { "azure", "ai" };

    /// <summary>
    /// Azure Document Intelligence tags
    /// </summary>
    internal static readonly string[] AzureDocument = { "azure", "document" };

    /// <summary>
    /// Azure Foundry tags
    /// </summary>
    internal static readonly string[] AzureFoundry = { "azure", "foundry" };

    /// <summary>
    /// SQL Database tags
    /// </summary>
    internal static readonly string[] SqlDatabase = { "database", "sql" };

    /// <summary>
    /// Pipeline tags
    /// </summary>
    internal static readonly string[] Pipeline = { "pipeline", "processing" };

    /// <summary>
    /// System tags
    /// </summary>
    internal static readonly string[] System = { "system" };
}
