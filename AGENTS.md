# MotorcycleRAG Repository Instructions

`AGENTS.md` files are mandatory instructions, not optional background material. Read this file before work anywhere in the repository, then read the nearest scoped `AGENTS.md` before changing files in that subtree.

## Non-negotiable rules

- Do not create infrastructure, deployment assets, dependencies, cross-cutting concerns, or documentation files without user approval. Ask before an architectural decision; present the trade-offs.
- Do not commit, push, reset, restore, checkout, clean, or rebase without explicit user approval. Keep agent-generated notes under `6-Docs/agent-notes/`, never at repository root.
- Do not introduce fallbacks, stubs, or workarounds without explicit approval. Fix the root cause.
- Never commit secrets, tokens, connection strings, passwords, customer data, or `.env` files. Use approved configuration and Key Vault patterns; redact prompts and PII from logs.
- .NET code must use Azure App Configuration and Key Vault references for application configuration and secrets. Python local-processor runtime values set by Admin Desktop are the only approved environment-variable exception.
- Before every `az` read, verify the active subscription against the allowlist in [Azure agent access](6-Docs/AzureEnvironment/agent-access.md). Azure writes, local `pulumi up`, direct Docker builds, and ACR pushes are forbidden.
- Preserve Clean Architecture: inner layers never depend on outer layers; Contracts contains interfaces only; Contracts.Models contains shared DTOs only; business invariants belong in Domain; Application services belong in `Services`.

Read the full [working agreement](6-Docs/system/agent-governance.md), [security directives](6-Docs/system/security.md), and [Azure environment rules](6-Docs/AzureEnvironment/agent-access.md) when the task touches their subject.

## Documentation and placement

Before changing a cataloged component, read the [documentation standard](6-Docs/documentation-standard.md), [component catalog](6-Docs/catalog.md), the source-root README, and the component's detailed documentation. Update canonical documentation when the public interface, configuration, architecture, runtime, operations, or workflow changes.

Before creating, moving, renaming, or placing source/test files, or changing namespaces, project references, DTO placement, interfaces, or layer boundaries, read the [architecture placement rules](6-Docs/rules/architecture-general.md).

`6-Docs/archive/` is historical only: do not follow, cite, or copy it as current guidance.

## Task routing

| Work area | Mandatory scoped instructions | Read when relevant |
| --- | --- | --- |
| API | [API AGENTS](1-Presentation/MotorcycleRAG.API/AGENTS.md) | [API documentation](6-Docs/MotorcycleRAG.API/) |
| Web UI and BFF | [Web UI AGENTS](1-Presentation/MotorcycleRag.WebUI/AGENTS.md), [BFF AGENTS](1-Presentation/MotorcycleRag.WebUI.BFF/AGENTS.md) | [Web UI documentation](6-Docs/MotorcycleRag.WebUI/) |
| Admin Desktop | [Admin Desktop AGENTS](1-Presentation/MotorcycleRAG.AdminDesktop/AGENTS.md) | [Admin Desktop documentation](6-Docs/MotorcycleRAG.AdminDesktop/) and task-specific guides |
| Mobile App | [Mobile AGENTS](1-Presentation/MotorcycleRAG.MobileApp/AGENTS.md) | [Mobile documentation](6-Docs/MotorcycleRAG.MobileApp/) |
| Local processor | [Processor AGENTS](2-Application/local-processing-service/AGENTS.md) | [Processor documentation](6-Docs/local-processing-service/) |
| Core, Application, Domain, Contracts, Persistence | nearest scoped `AGENTS.md` | [Architecture rules](6-Docs/rules/architecture-general.md) |
| Tests | nearest test-suite `AGENTS.md` | affected component documentation and requirements |
| Infrastructure | [Infrastructure AGENTS](7-Deployment/infrastructure/AGENTS.md) | [Deployment documentation](6-Docs/deployment/) and [Azure environment](6-Docs/AzureEnvironment/) |
| Codex configuration | [.codex AGENTS](.codex/AGENTS.md) | repository rules in this file remain controlling |

## Instruction hierarchy

1. This root file supplies repository-wide mandatory policy.
2. The nearest scoped `AGENTS.md` supplies additional rules for its subtree; it may not weaken this file.
3. Canonical system, component, deployment, and environment documentation provides detailed task-specific guidance. Scoped instructions link directly to the owning document; they are not a substitute for root policy.
