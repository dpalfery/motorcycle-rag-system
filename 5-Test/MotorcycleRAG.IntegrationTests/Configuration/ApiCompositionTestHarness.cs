using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.API;

namespace MotorcycleRAG.IntegrationTests.Configuration;

/// <summary>
/// Composes the API service collection with deterministic in-memory settings without
/// constructing or starting a web host.
/// </summary>
internal static class ApiCompositionTestHarness
{
    internal static IServiceCollection CreateServices()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Testing",
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AppConfig:Endpoint"] = string.Empty,
            ["AppConfig:ConnectionString"] = string.Empty,
            ["ApplicationInsights:ConnectionString"] = string.Empty,
            ["ApplicationInsights:EnableTelemetry"] = bool.FalseString,
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:Domain"] = "unit.invalid",
            ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
            ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
            ["AzureAd:AdminClientId"] = "22222222-2222-2222-2222-222222222222",
            ["AzureAd:LocalProcessorClientId"] = "33333333-3333-3333-3333-333333333333",
            ["Authentication:Audience"] = "11111111-1111-1111-1111-111111111111",
            ["AzureAI:OpenAIEndpoint"] = "https://openai.unit.invalid/",
            ["AzureAI:SearchServiceEndpoint"] = "https://search.unit.invalid/",
            ["AzureAI:DocumentIntelligenceEndpoint"] = "https://document-intelligence.unit.invalid/",
            ["AzureAI:FoundryEndpoint"] = "https://foundry.unit.invalid/",
            ["AzureAI:OrchestratorAgentName"] = "orchestrator",
            ["AzureAI:VectorSearchAgentName"] = "vector-search",
            ["AzureAI:WebSearchAgentName"] = "web-search",
            ["AzureAI:PDFSearchAgentName"] = "pdf-search",
            ["AzureAI:GraphQueryAgentName"] = "graph-query",
            ["BlobStorage:AccountEndpoint"] = "https://storage.unit.invalid/",
            ["BlobStorage:RawUploadsContainer"] = "raw-uploads",
            ["Ingestion:LocalEndpoint"] = "http://127.0.0.1:65535",
            ["Sql:ConnectionString"] = "Server=127.0.0.1,65535;Database=MotorcycleRAG_Unit;Integrated Security=true;TrustServerCertificate=true;",
            ["Onboarding:ApproverAddress"] = "approver@unit.invalid",
            ["ExternalIdentityProvisioning:InviteRedirectUrl"] = "https://unit.invalid/signin-oidc",
            ["ExternalIdentityProvisioning:ApiApplicationClientId"] = "44444444-4444-4444-4444-444444444444",
        });

        Program.ConfigureServices(builder);
        return builder.Services;
    }
}
