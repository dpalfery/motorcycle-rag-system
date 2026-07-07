using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Application.Services;

namespace MotorcycleRAG.API.Configuration.Services;

/// <summary>
/// Configuration for data processors
/// </summary>
internal static class DataProcessorsConfiguration
{
    /// <summary>
    /// Configure data processors
    /// </summary>
    internal static IServiceCollection AddDataProcessors(this IServiceCollection services, IConfiguration configuration)
    {
        // Register data processor implementations from Persistence layer
        services.AddScoped<IDataProcessor<CSVFile>, MotorcycleRAG.Persistence.DataProcessing.MotorcycleCsvProcessor>();
        if (Uri.TryCreate(configuration["AzureAI:DocumentIntelligenceEndpoint"], UriKind.Absolute, out _))
        {
            services.AddScoped<IDataProcessor<PDFDocument>, MotorcycleRAG.Persistence.DataProcessing.MotorcyclePdfProcessor>();
        }
        else
        {
            services.AddScoped<IDataProcessor<PDFDocument>, MotorcycleRAG.Persistence.DataProcessing.DisabledPdfProcessor>();
        }

        return services;
    }
}
