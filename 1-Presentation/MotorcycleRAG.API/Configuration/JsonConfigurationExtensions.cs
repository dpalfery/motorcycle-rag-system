using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Extension methods for configuring JSON serialization in ASP.NET Core
/// </summary>
internal static class JsonConfigurationExtensions
{
    /// <summary>
    /// Configures JSON serialization for ASP.NET Core controllers
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="isDevelopment">Whether the application is running in development mode</param>
    /// <returns>Service collection for chaining</returns>
    internal static IServiceCollection ConfigureJsonSerialization(this IServiceCollection services, bool isDevelopment = false)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            var jsonOptions = JsonSerializationConfiguration.DefaultOptions;
            
            options.SerializerOptions.PropertyNamingPolicy = jsonOptions.PropertyNamingPolicy;
            options.SerializerOptions.WriteIndented = isDevelopment;
            options.SerializerOptions.DefaultIgnoreCondition = jsonOptions.DefaultIgnoreCondition;
            options.SerializerOptions.PropertyNameCaseInsensitive = jsonOptions.PropertyNameCaseInsensitive;
            
            // Add custom converters
            foreach (var converter in jsonOptions.Converters)
            {
                options.SerializerOptions.Converters.Add(converter);
            }
        });

        // Also configure MVC JSON options for controllers
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            var jsonOptions = JsonSerializationConfiguration.DefaultOptions;
            
            options.JsonSerializerOptions.PropertyNamingPolicy = jsonOptions.PropertyNamingPolicy;
            options.JsonSerializerOptions.WriteIndented = isDevelopment;
            options.JsonSerializerOptions.DefaultIgnoreCondition = jsonOptions.DefaultIgnoreCondition;
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = jsonOptions.PropertyNameCaseInsensitive;
            
            // Add custom converters
            foreach (var converter in jsonOptions.Converters)
            {
                options.JsonSerializerOptions.Converters.Add(converter);
            }
        });

        return services;
    }
}
