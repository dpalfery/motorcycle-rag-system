# MotorcycleRAG Component Catalog

This catalog defines the maintained documentation surface. `Current` means the README and canonical documentation are maintained together; `Needs review` identifies content that is present but requires source verification before substantive changes.

| Component | Type | Source root | Overview | Detailed documentation | Owner | Last reviewed | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MotorcycleRAG system | System | repository root | [README](../README.md) | [system](system/) | Maintainers | 2026-07-11 | Current |
| MotorcycleRAG API | Application | `1-Presentation/MotorcycleRAG.API` | [README](../1-Presentation/MotorcycleRAG.API/README.md) | [API docs](MotorcycleRAG.API/) | API maintainers | 2026-07-11 | Current |
| MotorcycleRAG Admin Desktop | Application | `1-Presentation/MotorcycleRAG.AdminDesktop` | [README](../1-Presentation/MotorcycleRAG.AdminDesktop/README.md) | [Admin docs](MotorcycleRAG.AdminDesktop/) | Admin Desktop maintainers | 2026-07-11 | Current |
| MotorcycleRAG Mobile App | Application | `1-Presentation/MotorcycleRAG.MobileApp` | [README](../1-Presentation/MotorcycleRAG.MobileApp/README.md) | [Mobile docs](MotorcycleRAG.MobileApp/) | Mobile maintainers | 2026-07-11 | Current |
| MotorcycleRAG Web UI and BFF | Application pair | `1-Presentation/MotorcycleRag.WebUI`, `1-Presentation/MotorcycleRag.WebUI.BFF` | [README](../1-Presentation/MotorcycleRag.WebUI/README.md) | [Web UI docs](MotorcycleRag.WebUI/) | Web UI maintainers | 2026-07-11 | Current |
| Local Processing Service | Service | `2-Application/local-processing-service` | [README](../2-Application/local-processing-service/README.md) | [Processor docs](local-processing-service/) | Ingestion maintainers | 2026-07-11 | Current |
| Core, Application, Domain, and Persistence | Architecture layers | `0-Base` through `4-Persistence` | [root README](../README.md) | [architecture rules](rules/architecture-general.md) | Architecture maintainers | 2026-07-11 | Current |
| Test suites | Verification | `5-Test` | [root README](../README.md) | [system requirements](system/requirements.md) | Test maintainers | 2026-07-11 | Current |
| Azure Environment | Deployment environment | `7-Deployment/infrastructure` | [README](../7-Deployment/README.md) | [Azure Environment docs](AzureEnvironment/) | Platform maintainers | 2026-07-11 | Current |
| Database Setup CLI | CLI tool | `7-Deployment/DbSetup` | [README](../7-Deployment/DbSetup/README.md) | [database setup reference](DevOps/database-setup.md) | Platform maintainers | 2026-07-11 | Current |
| SkillForge | Vendored tool | `7-Deployment/tools/SkillForge` | [README](../7-Deployment/tools/SkillForge/README.md) | [SkillForge reference](reference/skillforge.md) | Developer-experience maintainers | 2026-07-11 | Current |
| Caching services | Shared library | `2-Application/MotorcycleRAG.Application/Services/Caching` | [README](../2-Application/MotorcycleRAG.Application/Services/Caching/README.md) | [architecture rules](rules/architecture-general.md) | Application maintainers | 2026-07-11 | Current |

## Catalog maintenance

Add a row before introducing a new runnable component or public integration. Update the row when ownership, source root, supported status, or canonical documentation changes. A `Needs review` item is not a license to leave documentation stale; it identifies the next documentation-verification task.
