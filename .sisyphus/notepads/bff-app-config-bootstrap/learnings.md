# Learnings — BFF App Config Bootstrap

## 2026-03-01

- API uses `Microsoft.Extensions.Configuration.AzureAppConfiguration` v8.4.0 and `Microsoft.Azure.AppConfiguration.AspNetCore` v8.4.0 — both are needed (former for `AddAzureAppConfiguration` on config builder, latter for `builder.Services.AddAzureAppConfiguration()` and `app.UseAzureAppConfiguration()` middleware)
- BFF already had `Azure.Identity` v1.18.0 — no need to add it
- BFF uses top-level statements; `isAppConfigEnabled` is a top-level bool referenced later in middleware section
- The API bootstrap pattern lives at lines 53-83 of its Program.cs; the `UseAzureAppConfiguration` call is conditional on `isAppConfigEnabled`
- Solution-wide build has 12 pre-existing errors in test projects (PerformanceTests, EndToEndTests, MobileApp.Tests) — none related to BFF changes
- BFF project builds cleanly: 0 warnings, 0 errors
