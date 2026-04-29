using System.IO;

using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace MotorcycleRag.WebUI.BFF.Configuration.Services;

/// <summary>
/// Configuration for Data Protection services in the BFF.
/// </summary>
internal static class DataProtectionServiceConfiguration
{
    public static IServiceCollection AddBffDataProtection(
        this IServiceCollection services, 
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        var dpBlobUri = configuration["DataProtection:BlobUri"];

        if (env.IsDevelopment())
        {
            var localKeyDirectory = new DirectoryInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MotorcycleRag.WebUI.BFF",
                "DataProtection-Keys"));

            services.AddDataProtection()
                .SetApplicationName("MotorcycleRag.WebUI.BFF")
                .PersistKeysToFileSystem(localKeyDirectory);
        }
        else if (!string.IsNullOrEmpty(dpBlobUri))
        {
            services.AddDataProtection()
                .SetApplicationName("MotorcycleRag.WebUI.BFF")
                .PersistKeysToAzureBlobStorage(new Uri(dpBlobUri), new DefaultAzureCredential());
        }
        else
        {
            // Ephemeral keys — sessions will not survive container restarts
            services.AddDataProtection()
                .SetApplicationName("MotorcycleRag.WebUI.BFF");
            
            // Fail fast in production if blob URI is not configured
            if (env.IsProduction())
            {
                throw new InvalidOperationException(
                    "DataProtection:BlobUri is required in production. " +
                    "Configure 'DataProtection:BlobUri' in App Configuration to persist encryption keys.");
            }
        }

        return services;
    }
}
