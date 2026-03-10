# Plan: Decompose Program.cs into Testable Extensions

## Context
Both `MotorcycleRAG.API/Program.cs` (636 lines) and `MotorcycleRag.WebUI.BFF/Program.cs` (315 lines) are monolithic composition roots. Configuration and registration logic is inline and untestable. The goal is to extract logical groupings into extension methods following the pattern already established in the API's `Configuration/Services/` folder, and add unit tests for the logic that has meaningful testable behaviour.

---

## Existing Patterns to Follow

- **`Configuration/Services/XyzConfiguration.cs`** — `internal static class XyzConfiguration` with `AddXyz(this IServiceCollection, IConfiguration)` methods (already used in API for 8 service groups)
- **`Extensions/XyzExtensions.cs`** — `internal static class XyzExtensions` for `IApplicationBuilder`/`AuthenticationBuilder` extensions
- **Tests** — xUnit + Moq + FluentAssertions; AAA pattern; method named `Method_Scenario_ExpectedResult`
- **Test projects** — API extensions → `5-Test/tests/MotorcycleRAG.UnitTests/`; BFF extensions → `1-Presentation/MotorcycleRag.WebUI.BFF.Tests/`

---

## Phase 1 — API Extractions

### 1. `Extensions/ClaimsPrincipalExtensions.cs` *(new — highest testability value)*
Extract the three untestable private local functions from the authorization policy block:
```csharp
internal static bool HasScope(this ClaimsPrincipal user, string requiredScope)
internal static bool HasAnyRole(this ClaimsPrincipal user, params string[] roles)
internal static bool IsAuthorizedClient(this ClaimsPrincipal user, string expectedClientId)
```
**Unit tests:** `MotorcycleRAG.UnitTests/Presentation/API/Extensions/ClaimsPrincipalExtensionsTests.cs`
Test cases: null user, missing claim, correct value, multiple scopes in one claim, OrdinalIgnoreCase matching.

### 2. `Configuration/Services/AuthorizationPoliciesConfiguration.cs` *(new)*
```csharp
internal static IServiceCollection AddMotorcycleRagAuthorization(
    this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment env)
```
Contains all policy definitions (`mcr-api-admin`, `Read`, `Chat`, `User`, `Viewer`, `ManualsView`, FallbackPolicy, DefaultPolicy). Uses `ClaimsPrincipalExtensions` from above.

### 3. `Configuration/Services/RateLimitingServiceConfiguration.cs` *(new)*
```csharp
internal static IServiceCollection AddMotorcycleRagRateLimiting(this IServiceCollection services)
```
Contains all four rate limiting policies (`public`, `authenticated`, `ingestion-jobs`, `manuals-view`). Extract the per-role limit decision inside `authenticated` into an `internal static int GetRateLimitForUser(ClaimsPrincipal user)` helper → directly unit testable.
**Unit tests:** `MotorcycleRAG.UnitTests/Presentation/API/Configuration/Services/RateLimitingServiceConfigurationTests.cs`
Test cases: admin role → 100000, ProUser → 500, unauthenticated → 50.

### 4. `Configuration/Services/CorsServiceConfiguration.cs` *(new)*
```csharp
internal static IServiceCollection AddRestrictedCors(
    this IServiceCollection services, IConfiguration configuration)
```
Reads `Cors:AllowedOrigins` from config; falls back to `https://localhost:3000`.

### 5. `Configuration/Services/SwaggerServiceConfiguration.cs` *(new)*
```csharp
internal static IServiceCollection AddApiDocumentation(this IServiceCollection services)
```

### 6. `Configuration/Services/TelemetryServiceConfiguration.cs` *(new)*
```csharp
internal static IServiceCollection AddMotorcycleRagTelemetry(
    this IServiceCollection services, IConfiguration configuration)
```
Contains the AppInsights validation (fail-fast if enabled but no connection string) and registration.

### 7. `Configuration/AppConfigurationExtensions.cs` *(new)*
```csharp
internal static WebApplicationBuilder AddAzureAppConfigurationWithKeyVault(
    this WebApplicationBuilder builder)
```
Contains the App Config bootstrap (Select labels: unlabelled → `api` → Environment), Key Vault integration, refresh configuration, and `AddAzureAppConfiguration()` service registration. Also absorbs `ValidateAndPopulateAzureAdConfiguration` (rename to `WithDerivedAzureAdValues`) and `ValidateAndPopulateAzureAIConfiguration` (rename to `WithValidatedAzureAIEndpoints`) as chained internal methods or private helpers.
**Unit tests:** `MotorcycleRAG.UnitTests/Presentation/API/Configuration/AppConfigurationExtensionsTests.cs`
Test the derivation logic: given TenantId + ClientId, JWT issuer/audience/JWKS URL are correctly derived.

### 8. `Extensions/WebApplicationExtensions.cs` *(new — API)*
```csharp
internal static WebApplication UseMotorcycleRagMiddleware(this WebApplication app)
internal static Task PreWarmJwtSigningKeysAsync(this WebApplication app)  // async helper
```
Captures the fixed middleware order and the JWT key cache pre-warm block.

---

## Phase 2 — BFF Extractions

### 9. `Configuration/Services/AuthenticationServiceConfiguration.cs` *(new — BFF)*
```csharp
internal static IServiceCollection AddBffAuthentication(
    this IServiceCollection services, IConfiguration configuration)
```
Contains the full Cookie + OIDC setup (PKCE, nonce, SameSite=None dual-cookie, redirect_uri rewriting for ACA, scope configuration).
**Unit tests:** `MotorcycleRag.WebUI.BFF.Tests/Configuration/Services/AuthenticationServiceConfigurationTests.cs`
Test cases: verify `CookieAuthenticationOptions.Cookie.HttpOnly = true`, verify OIDC scopes include `read` and `chat`.

### 10. `Configuration/Services/CorsServiceConfiguration.cs` *(new — BFF)*
```csharp
internal static IServiceCollection AddBffCors(
    this IServiceCollection services, IConfiguration configuration)
```

### 11. `Configuration/Services/YarpServiceConfiguration.cs` *(new — BFF)*
```csharp
internal static IServiceCollection AddBffReverseProxy(
    this IServiceCollection services, IConfiguration configuration)
```
Contains YARP setup with Bearer token propagation from authenticated user.

### 12. `Configuration/Services/DataProtectionServiceConfiguration.cs` *(new — BFF)*
```csharp
internal static IServiceCollection AddBffDataProtection(
    this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment env)
```
Reads `DataProtection:BlobUri`; persists to blob when configured; ephemeral fallback in dev; fail-fast (throws) in Production if not configured.
**Tests already partially exist** in `MotorcycleRag.WebUI.BFF.Tests/DataProtectionConfigurationTests.cs` — update these to call the extracted method directly.

### 13. `Configuration/AppConfigurationExtensions.cs` *(new — BFF)*
```csharp
internal static WebApplicationBuilder AddBffAzureAppConfiguration(
    this WebApplicationBuilder builder)
```
Contains the BFF App Config bootstrap (labels: unlabelled → `bff` → Environment), Key Vault setup, refresh.

### 14. `Extensions/WebApplicationExtensions.cs` *(new — BFF)*
```csharp
internal static WebApplication UseMotorcycleRagBffMiddleware(this WebApplication app)
```
Captures the complete security-first middleware ordering (ForwardedHeaders → HTTPS → HSTS → HostHeaderValidation → SecurityHeaders → StaticFiles → Routing → CORS → Auth/AuthZ → Controllers → YARP → /health → SPA fallback).

---

## Target Program.cs Shape

**API `Program.cs` (~100 lines):**
```csharp
builder.Logging.AddStructuredLogging(builder.Environment);
builder.AddAzureAppConfigurationWithKeyVault();
builder.Services.AddMotorcycleRagTelemetry(configuration);
builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(...);   // Kestrel limits (3 lines, keep inline)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.ConfigureJsonSerialization(...);
builder.Services.Configure<AppOptions>(configuration);
builder.Services.AddApiDocumentation();
builder.Services.AddRestrictedCors(configuration);
// existing: AddAzureAIServices, AddCoreServices, AddSearchAgents, ...
builder.Services.AddDualIssuerJwtBearer(...);     // already extracted
builder.Services.AddMotorcycleRagAuthorization(configuration, builder.Environment);
builder.Services.AddMotorcycleRagRateLimiting();
var app = builder.Build();
await app.UseMotorcycleRagMiddleware()
         .PreWarmJwtSigningKeysAsync();
await app.RunAsync();
```

**BFF `Program.cs` (~60 lines):**
```csharp
builder.AddBffAzureAppConfiguration();
builder.Services.AddControllers();
builder.Services.AddBffCors(builder.Configuration);
builder.Services.AddBffReverseProxy(builder.Configuration);
builder.Services.AddBffAuthentication(builder.Configuration);
builder.Services.AddBffDataProtection(builder.Configuration, builder.Environment);
var app = builder.Build();
app.UseMotorcycleRagBffMiddleware();
await app.RunAsync();
```

---

## Files Modified

| Action | Path |
|--------|------|
| Modified | `1-Presentation/MotorcycleRAG.API/Program.cs` |
| Modified | `1-Presentation/MotorcycleRag.WebUI.BFF/Program.cs` |
| New | `1-Presentation/MotorcycleRAG.API/Extensions/ClaimsPrincipalExtensions.cs` |
| New | `1-Presentation/MotorcycleRAG.API/Extensions/WebApplicationExtensions.cs` |
| New | `1-Presentation/MotorcycleRAG.API/Configuration/AppConfigurationExtensions.cs` |
| New | `1-Presentation/MotorcycleRAG.API/Configuration/Services/AuthorizationPoliciesConfiguration.cs` |
| New | `1-Presentation/MotorcycleRAG.API/Configuration/Services/RateLimitingServiceConfiguration.cs` |
| New | `1-Presentation/MotorcycleRAG.API/Configuration/Services/CorsServiceConfiguration.cs` |
| New | `1-Presentation/MotorcycleRAG.API/Configuration/Services/SwaggerServiceConfiguration.cs` |
| New | `1-Presentation/MotorcycleRAG.API/Configuration/Services/TelemetryServiceConfiguration.cs` |
| New | `1-Presentation/MotorcycleRag.WebUI.BFF/Extensions/WebApplicationExtensions.cs` |
| New | `1-Presentation/MotorcycleRag.WebUI.BFF/Configuration/AppConfigurationExtensions.cs` |
| New | `1-Presentation/MotorcycleRag.WebUI.BFF/Configuration/Services/AuthenticationServiceConfiguration.cs` |
| New | `1-Presentation/MotorcycleRag.WebUI.BFF/Configuration/Services/CorsServiceConfiguration.cs` |
| New | `1-Presentation/MotorcycleRag.WebUI.BFF/Configuration/Services/YarpServiceConfiguration.cs` |
| New | `1-Presentation/MotorcycleRag.WebUI.BFF/Configuration/Services/DataProtectionServiceConfiguration.cs` |
| New | `5-Test/tests/MotorcycleRAG.UnitTests/Presentation/API/Extensions/ClaimsPrincipalExtensionsTests.cs` |
| New | `5-Test/tests/MotorcycleRAG.UnitTests/Presentation/API/Configuration/Services/RateLimitingServiceConfigurationTests.cs` |
| New | `5-Test/tests/MotorcycleRAG.UnitTests/Presentation/API/Configuration/AppConfigurationExtensionsTests.cs` |
| New | `1-Presentation/MotorcycleRag.WebUI.BFF.Tests/Configuration/Services/AuthenticationServiceConfigurationTests.cs` |
| Modified | `1-Presentation/MotorcycleRag.WebUI.BFF.Tests/DataProtectionConfigurationTests.cs` |

---

## Verification

```
dotnet build D:\motorcycle-rag-system\MotorcycleRAG.sln
dotnet test 5-Test/tests/MotorcycleRAG.UnitTests/
dotnet test 1-Presentation/MotorcycleRag.WebUI.BFF.Tests/
dotnet test 5-Test/tests/MotorcycleRAG.IntegrationTests/   # existing integration tests must still pass
```

Existing `DIRegistrationTests` and `AuthorizationTests` in IntegrationTests provide regression coverage — no behaviour changes, only structural refactoring.
