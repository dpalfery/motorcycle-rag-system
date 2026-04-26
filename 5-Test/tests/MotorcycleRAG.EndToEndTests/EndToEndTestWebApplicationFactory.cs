using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.IntegrationTests;

namespace MotorcycleRAG.EndToEndTests;

public class EndToEndTestWebApplicationFactory : TestWebApplicationFactory {
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services => {
            services.RemoveAll<IAgentOrchestrator>();
            services.AddSingleton<IAgentOrchestrator, FakeEndToEndAgentOrchestrator>();
        });
    }
}