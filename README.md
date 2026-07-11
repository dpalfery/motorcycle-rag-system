# MotorcycleRAG

MotorcycleRAG is an open-source, multi-agent retrieval-augmented generation system for motorcycle information. It combines structured specifications, manuals, and curated web sources through a Clean Architecture backend, end-user clients, and a local-first ingestion processor.

## Start here

- New contributors: [system onboarding](6-Docs/system/onboarding.md)
- Architecture: [system architecture](6-Docs/system/architecture.md)
- Complete documentation: [6-Docs index](6-Docs/README.md)
- Component ownership and documentation coverage: [catalog](6-Docs/catalog.md)
- Contribution process: [CONTRIBUTING.md](CONTRIBUTING.md)
- Security reports: [SECURITY.md](SECURITY.md)

## Components

| Component | Purpose | Overview |
| --- | --- | --- |
| MotorcycleRAG API | Authenticated query, ingestion, administration, and health API | [README](1-Presentation/MotorcycleRAG.API/README.md) |
| Web UI and BFF | Browser UI with an OIDC/session and API-proxy boundary | [README](1-Presentation/MotorcycleRag.WebUI/README.md) |
| Admin Desktop | Operator desktop app and local-ingestion coordinator | [README](1-Presentation/MotorcycleRAG.AdminDesktop/README.md) |
| Mobile App | .NET MAUI end-user query client | [README](1-Presentation/MotorcycleRAG.MobileApp/README.md) |
| Local Processing Service | Python service for local PDF, CSV, and graph ingestion | [README](2-Application/local-processing-service/README.md) |
| Azure Environment | Infrastructure, deployment assets, and operational tooling | [README](7-Deployment/README.md) |
| Database Setup CLI | Local SQL development database provisioning tool | [README](7-Deployment/DbSetup/README.md) |
| SkillForge | Vendored skill-validation and governance tool | [README](7-Deployment/tools/SkillForge/README.md) |
| Caching services | Application caching and performance facilities | [README](2-Application/MotorcycleRAG.Application/Services/Caching/README.md) |

## Development and deployment

Follow the component onboarding document for the application you are changing. Deployment and infrastructure changes are validated and released exclusively through the repository's GitHub Actions workflows; do not deploy infrastructure or application images directly from a local shell.

## Community

- [Contributing](CONTRIBUTING.md)
- [Code of conduct](CODE_OF_CONDUCT.md)
- [Security policy](SECURITY.md)
- [Support](SUPPORT.md)
- [MIT license](LICENSE)
