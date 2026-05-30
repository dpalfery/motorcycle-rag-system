using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for API documentation using ASP.NET Core OpenAPI.
/// </summary>
internal static class SwaggerServiceConfiguration {
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services) {
        services.AddOpenApi("v1", options => {
            options.AddDocumentTransformer((document, _, _) => {
                document.Info ??= new OpenApiInfo();
                document.Info.Title = "Motorcycle RAG API";
                document.Info.Version = "v1";
                document.Info.Description = "AI-powered motorcycle information retrieval system";

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
