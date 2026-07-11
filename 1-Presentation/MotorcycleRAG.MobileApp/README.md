# MotorcycleRAG Mobile App

MotorcycleRAG Mobile App is the end-user .NET MAUI client for motorcycle knowledge queries. It uses XAML and CommunityToolkit MVVM, authenticates with Entra through the system browser, calls the MotorcycleRAG API over HTTPS, and retains conversations, citations, and user memory in local SQLite storage.

The project currently targets Mac Catalyst on macOS and Windows/Mac Catalyst on Windows. Platform-specific services, including PDF rendering, live under `Platforms/` while shared UI and application behavior remain platform-neutral.

## Documentation

- [Developer onboarding](../../6-Docs/MotorcycleRAG.MobileApp/onboarding.md)
- [Architecture](../../6-Docs/MotorcycleRAG.MobileApp/architecture.md)
- [Requirements](../../6-Docs/MotorcycleRAG.MobileApp/requirements.md)
