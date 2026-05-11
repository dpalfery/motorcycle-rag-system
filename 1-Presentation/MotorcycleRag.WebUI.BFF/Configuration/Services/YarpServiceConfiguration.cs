using Microsoft.AspNetCore.Authentication;
using Yarp.ReverseProxy.Transforms;

namespace MotorcycleRag.WebUI.BFF.Configuration.Services;

/// <summary>
/// Configuration for the YARP reverse proxy in the BFF.
/// </summary>
internal static class YarpServiceConfiguration
{
    public static IServiceCollection AddBffReverseProxy(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        services.AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"))
            .AddTransforms(builderContext =>
            {
                // Override Host header to match API custom domain for internal routing
                builderContext.AddRequestTransform(transformContext =>
                {
                    transformContext.ProxyRequest.Headers.Host = "motorag.api.palfery.com";
                    return ValueTask.CompletedTask;
                });

                // Attach Bearer Token from User Identity to downstream requests
                builderContext.AddRequestTransform(async transformContext =>
                {
                    var token = await transformContext.HttpContext.GetTokenAsync("access_token")
                        .ConfigureAwait(false);
                    
                    if (!string.IsNullOrEmpty(token))
                    {
                        transformContext.ProxyRequest.Headers.Authorization = 
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    }
                });
            });

        return services;
    }
}
