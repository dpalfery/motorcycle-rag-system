using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using CommunityToolkit.Mvvm;
using SQLite;
using MotorcycleRAG.MobileApp.Persistence.Repositories;
using MotorcycleRAG.MobileApp.Services;

namespace MotorcycleRAG.MobileApp;

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

        // Configuration
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("MotorcycleRAG.MobileApp.appsettings.json");
        if (stream != null)
        {
            var config = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();
            builder.Configuration.AddConfiguration(config);
        }

#if DEBUG
		builder.Logging.AddDebug();
#endif

        // Persistence
        string dbPath = Path.Combine(FileSystem.AppDataDirectory, "motorcyclerag.db3");
        builder.Services.AddSingleton<SQLiteAsyncConnection>(s => new SQLiteAsyncConnection(dbPath));

        builder.Services.AddSingleton<IConversationRepository, ConversationRepository>();
        builder.Services.AddSingleton<IMessageRepository, MessageRepository>();
        builder.Services.AddSingleton<ICitationRepository, CitationRepository>();
        builder.Services.AddSingleton<IUserMemoryRepository, UserMemoryRepository>();

        // Services
        builder.Services.AddHttpClient<IApiClient, MotorcycleRagApiClient>();
        // IAuthenticationService and IStorageService implementation to be added in next phases

		return builder.Build();
	}
}
