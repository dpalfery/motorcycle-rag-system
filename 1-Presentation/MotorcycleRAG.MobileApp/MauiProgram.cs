using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using System.Reflection;
using CommunityToolkit.Mvvm;
using SQLite;
using MotorcycleRAG.MobileApp.Persistence;
using MotorcycleRAG.MobileApp.Persistence.Repositories;
using MotorcycleRAG.MobileApp.Services;
using MotorcycleRAG.MobileApp.Views;
using MotorcycleRAG.MobileApp.ViewModels;
using CommunityToolkit.Maui;
using MotorcycleRAG.Core.Logging;
using MotorcycleRAG.MobileApp.Configuration;

namespace MotorcycleRAG.MobileApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
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

        // Wrap every ILoggerProvider registered above (Debug in DEBUG builds) in the
        // central SanitizingLoggerProvider so no structured log state reaches a sink
        // without LogSanitizer escaping. MUST be the last logging-provider call per
        // SanitizingLoggerExtensions remarks. Always registered so release builds are
        // also covered when other providers (e.g. App Center) are added later.
        builder.Logging.AddSanitizingLogger();

        // Persistence
        string dbPath = Path.Combine(FileSystem.AppDataDirectory, "motorcyclerag.db3");
        builder.Services.AddSingleton<SQLiteAsyncConnection>(s => new SQLiteAsyncConnection(dbPath));
        builder.Services.AddSingleton<DatabaseBootstrap>();

        builder.Services.AddSingleton<IConversationRepository, ConversationRepository>();
        builder.Services.AddSingleton<IMessageRepository, MessageRepository>();
        builder.Services.AddSingleton<ICitationRepository, CitationRepository>();
        builder.Services.AddSingleton<IUserMemoryRepository, UserMemoryRepository>();

        // Services
        builder.Services.AddHttpClient<IApiClient, MotorcycleRagApiClient>();
        builder.Services.Configure<AuthenticationOptions>(
            builder.Configuration.GetSection(AuthenticationOptions.SectionName));
        builder.Services.AddSingleton<IValidateOptions<AuthenticationOptions>, AuthenticationOptionsValidator>();
        builder.Services.AddOptions<AuthenticationOptions>().ValidateOnStart();
        builder.Services.AddSingleton<IPublicClientApplication>(serviceProvider =>
        {
            var authentication = serviceProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
            var msalBuilder = PublicClientApplicationBuilder.Create(authentication.ClientId)
                .WithRedirectUri(authentication.RedirectUri)
                .WithAuthority(AzureCloudInstance.AzurePublic, authentication.TenantId);

#if ANDROID
            msalBuilder = msalBuilder.WithParentActivityOrWindow(() => Platform.CurrentActivity);
#endif

            return msalBuilder.Build();
        });
        builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
        builder.Services.AddSingleton<IConversationService, ConversationService>();
        builder.Services.AddSingleton<IUserMemoryService, UserMemoryService>();
        builder.Services.AddSingleton<IStorageService, StorageService>();

        // PDF Services
        builder.Services.AddTransient<IPdfRenderer, PdfRenderer>();
        builder.Services.AddTransient<IPdfViewerService, PdfViewerService>();
        builder.Services.AddSingleton<IImageSourceFactory, ImageSourceFactory>();

        // Views & ViewModels
        builder.Services.AddTransient<AuthenticationPage>();
        builder.Services.AddTransient<AuthenticationViewModel>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<ConversationListPage>();
        builder.Services.AddTransient<ConversationListViewModel>();
        builder.Services.AddTransient<UserMemoryPage>();
        builder.Services.AddTransient<UserMemoryViewModel>();
        builder.Services.AddTransient<PdfViewerPage>();
        builder.Services.AddTransient<PdfViewerViewModel>();

        // AppShell
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }
}
