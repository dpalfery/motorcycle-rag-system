using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for API documentation using Swagger.
/// </summary>
internal static class SwaggerServiceConfiguration
{
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Motorcycle RAG API",
                Version = "v1",
                Description = "AI-powered motorcycle information retrieval system"
            });

            // Enable file upload support for [FromForm] IFormFile parameters
            c.MapType<IFormFile>(() => new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "binary"
            });
        });

        return services;
    }
}
