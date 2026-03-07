# Decisions — BFF App Config Bootstrap

## 2026-03-01

- **Package versions pinned to 8.4.0** to exactly match API and avoid version skew
- **Bootstrap block placed immediately after `var builder = WebApplication.CreateBuilder(args);`** and before any `builder.Services.Add*` calls, matching API ordering
- **Middleware placed after `var app = builder.Build();` and before `app.UseForwardedHeaders`** — ensures config refresh happens before any middleware that depends on config values
- **Conditional pattern (`isAppConfigEnabled`) preserved** — allows local dev without Azure App Configuration endpoint configured
- **Sentinel key: `Settings:Sentinel`** with 30-second refresh interval, matching API exactly
