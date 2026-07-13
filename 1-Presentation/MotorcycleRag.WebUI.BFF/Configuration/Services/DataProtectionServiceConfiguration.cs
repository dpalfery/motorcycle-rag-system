using System.IO;

using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotorcycleRag.WebUI.BFF.HealthChecks;

namespace MotorcycleRag.WebUI.BFF.Configuration.Services;

/// <summary>
/// Configuration for Data Protection services in the BFF.
/// </summary>
internal static class DataProtectionServiceConfiguration {
    public static IServiceCollection AddBffDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment env) {
        var dpBlobUri = configuration["DataProtection:BlobUri"];

        // The BFF maps /health in every environment. The blob-specific check remains conditional below.
        services.AddHealthChecks();

        if (env.IsDevelopment()) {
            var localKeyDirectory = new DirectoryInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MotorcycleRag.WebUI.BFF",
                "DataProtection-Keys"));

            services.AddDataProtection()
                .SetApplicationName("MotorcycleRag.WebUI.BFF")
                .PersistKeysToFileSystem(localKeyDirectory);
        }
        else if (!string.IsNullOrEmpty(dpBlobUri)) {
            var blobClient = new BlobClient(new Uri(dpBlobUri, UriKind.Absolute), new DefaultAzureCredential());

            services.AddDataProtection()
                .SetApplicationName("MotorcycleRag.WebUI.BFF")
                .PersistKeysToAzureBlobStorage(blobClient);

            services.AddSingleton<IDataProtectionBlobProbe>(new AzureBlobDataProtectionProbe(blobClient));
            services.AddHealthChecks()
                .AddCheck<DataProtectionHealthCheck>("data_protection_blob");
        }
        else {
            // Ephemeral keys — sessions will not survive container restarts
            services.AddDataProtection()
                .SetApplicationName("MotorcycleRag.WebUI.BFF");

            // Fail fast in production if blob URI is not configured
            if (env.IsProduction()) {
                throw new InvalidOperationException(
                    "DataProtection:BlobUri is required in production. " +
                    "Configure 'DataProtection:BlobUri' in App Configuration to persist encryption keys.");
            }
        }

        return services;
    }
}
