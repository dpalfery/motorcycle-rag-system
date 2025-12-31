using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// YARP
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        // Attach Bearer Token from User Identity to downstream requests
        builderContext.AddRequestTransform(async transformContext =>
        {
            var user = transformContext.HttpContext.User;
            var token = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.GetTokenAsync(transformContext.HttpContext, "access_token");
            if (!string.IsNullOrEmpty(token))
            {
                transformContext.ProxyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        });
    });

// Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Name = "__Host-MotorcycleRAG";
    options.Cookie.Path = "/";
    options.Cookie.IsEssential = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(1);
    options.SlidingExpiration = true;
    options.Cookie.MaxAge = TimeSpan.FromHours(1);
    options.Cookie.Domain = null; // Prevent subdomain attacks
})
.AddOpenIdConnect(options =>
{
    var authConfig = builder.Configuration.GetSection("AzureAd");
    options.Authority = $"{authConfig["Instance"]}{authConfig["TenantId"]}";
    options.ClientId = authConfig["ClientId"];
    options.ClientSecret = Environment.GetEnvironmentVariable("B2C_CLIENT_SECRET") ?? string.Empty;
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("api");
    
    // Redirect hardening
    options.ProtocolValidator.RequireNonce = true;
    options.ProtocolValidator.RequireState = true;
    options.ProtocolValidator.RequireStateValidation = true;

    // PKCE protection
    options.UsePkce = true;
    options.ResponseMode = "query";
    
    // Token validation
    options.TokenValidationParameters.ValidateIssuer = true;
    options.TokenValidationParameters.ValidateAudience = true;
    options.TokenValidationParameters.ValidateLifetime = true;
    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
    options.TokenValidationParameters.RequireExpirationTime = true;
    options.TokenValidationParameters.RequireSignedTokens = true;
});

var app = builder.Build();

// Pipeline
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// YARP Endpoints
app.MapReverseProxy();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "MotorcycleRag.WebUI.BFF" }));

// Fallback to React (SPA)
app.MapFallbackToFile("index.html");

app.Run();