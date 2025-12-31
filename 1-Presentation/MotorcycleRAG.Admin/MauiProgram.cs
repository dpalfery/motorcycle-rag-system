using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Pages;
using MotorcycleRAG.Admin.ViewModels;
using MotorcycleRAG.Admin.Processing;

namespace MotorcycleRAG.Admin;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Configure logging for all build configurations
		// Using Debug provider which works across all MAUI platforms
		builder.Logging.AddDebug();
#if DEBUG
		builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
		builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

		// Register Services
		builder.Services.AddSingleton<IAdminAuthService>(sp =>
		{
			// Retrieve authentication configuration from environment variables
			// Never use placeholder values - fail fast if configuration is missing
			var clientId = Environment.GetEnvironmentVariable("ENTRA_CLIENT_ID")
				?? throw new InvalidOperationException(
					"ENTRA_CLIENT_ID environment variable is required. " +
					"Please set it to your Microsoft Entra (Azure AD) application client ID.");

			var authority = Environment.GetEnvironmentVariable("ENTRA_AUTHORITY")
				?? throw new InvalidOperationException(
					"ENTRA_AUTHORITY environment variable is required. " +
					"Please set it to your Microsoft Entra authority URL (e.g., https://login.microsoftonline.com/{tenant-id}).");

			var apiScope = Environment.GetEnvironmentVariable("API_SCOPE")
				?? throw new InvalidOperationException(
					"API_SCOPE environment variable is required. " +
					"Please set it to your API scope (e.g., api://{client-id}/.default).");

			// Get logger from service provider - required for AdminAuthService
			var logger = sp.GetRequiredService<ILogger<AdminAuthService>>();

			return new AdminAuthService(
				clientId: clientId,
				authority: authority,
				scopes: new[] { apiScope },
				logger: logger
			);
		});

		builder.Services.AddSingleton<HttpClient>(sp =>
		{
			// Validate and retrieve API base URL
			var baseUrl = Environment.GetEnvironmentVariable("API_BASE_URL");

			if (string.IsNullOrWhiteSpace(baseUrl))
			{
				baseUrl = "https://localhost:7000";
			}

			// Validate HTTPS in production (non-localhost URLs must use HTTPS)
			if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				if (!baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException(
						$"API_BASE_URL must use HTTPS for non-localhost URLs. Got: {baseUrl}");
				}
			}

			// Validate URL format
			if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var apiUri))
			{
				throw new InvalidOperationException(
					$"API_BASE_URL is not a valid URI: {baseUrl}");
			}

			return new HttpClient
			{
				BaseAddress = apiUri,
				Timeout = TimeSpan.FromSeconds(30)
			};
		});

		builder.Services.AddSingleton<ApiClient>();

		builder.Services.AddSingleton<PdfChunker>();
		builder.Services.AddSingleton<CsvChunker>();

		// Optional: ONNX embedding service (requires model file in Resources/Raw/)
		// Only register if model is available - IngestionViewModel will check for null
		// and fall back to server-side processing if not registered
		try
		{
			var embeddingService = OnnxEmbeddingServiceFactory.CreateFromAppResources();
			builder.Services.AddSingleton(embeddingService);
		}
		catch (Exception ex)
		{
			// Model not available - IngestionViewModel will work without local embeddings
			System.Diagnostics.Debug.WriteLine($"ONNX model not available: {ex.Message}");
			// Don't register the service - IngestionViewModel constructor should handle null
		}

		// Register Pages
		builder.Services.AddTransient<UploadPage>();
		builder.Services.AddTransient<JobsPage>();

		// Register ViewModels
		builder.Services.AddTransient<IngestionViewModel>();

		return builder.Build();
	}
}
