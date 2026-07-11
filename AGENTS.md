# 1. Project Overview

Multi-agent RAG system for motorcycle information retrieval. OWASP ASVS Level 2 security. Clean Architecture + DDD.

## Applications

The system is composed of four distinct applications, all located in the `1-Presentation` layer:

1. **`MotorcycleRAG.API`**: The core backend REST API providing search, data ingestion, and multi-agent orchestration.
   - **Subsystems**: Includes the Graph Database, Azure Foundry IQ, and Web Search modules.
   - **Dependencies**: `MotorcycleRAG.Application`, `MotorcycleRAG.Domain`, `MotorcycleRAG.Persistence`, `MotorcycleRAG.Core`, `MotorcycleRAG.Contracts`.
2. **`MotorcycleRag.WebUI`**: The web frontend application. It tightly couples a Vite-based SPA with a dedicated Backend-For-Frontend (BFF).
   - **Subsystems**: A Vite/TypeScript frontend and an ASP.NET Core BFF (`MotorcycleRag.WebUI.BFF`) for OIDC auth and YARP reverse-proxying.
   - **Dependencies**: The BFF routes to the `MotorcycleRAG.API`.
3. **`MotorcycleRAG.AdminDesktop`**: A desktop application whose primary purpose is to host and run the **Local Processor** (the intelligent knowledge ingestion engine). System administration features are secondary.
   - **Subsystems**: Built with Tauri (Rust backend + TypeScript/Vite frontend). The core subsystem is the Local Processor.
   - **Dependencies**: Integrates with the `MotorcycleRAG.API`.
4. **`MotorcycleRAG.MobileApp`**: A cross-platform mobile application for end-users.
   - **Subsystems**: Built with .NET MAUI (XAML/MVVM) and local SQLite storage.
   - **Dependencies**: Calls the `MotorcycleRAG.API` and uses MSAL for auth.

# 2. Global Rules

@6-Docs/agent-instructions/working-agreement.md
@6-Docs/agent-instructions/azure-environment.md
@6-Docs/agent-instructions/security.md

## Documentation

Before changing a cataloged component, read `6-Docs/documentation-standard.md` and `6-Docs/catalog.md`. Update the component's canonical README and detailed documentation when its public interface, configuration, architecture, supported runtime, operations, or workflow changes.

**Deprecated docs:** `6-Docs/archive/` holds retired/deprecated documents and agent definitions kept only for historical reference. Ignore it. Do not read, cite, follow, or copy anything from `6-Docs/archive/` as current guidance, and do not treat agent definitions found there as active agents.

# 3. Architecture

Before creating, moving, renaming, or choosing placement for any source or test file, or changing namespaces, project references, DTO placement, interface placement, or layer boundaries — read the architecture placement rules first at `6-Docs/rules/architecture-general.md`.

**Key rules always in effect:**
- **Folder Structure Responsibilities**: See the [Repository Folder Structure](README.md#repository-folder-structure) and [Applications](README.md#applications) sections in the main README for definitions of what belongs in each root folder and details on the system's apps.
- 1 class or interface per file in C#
- Dependency Rule: inner layers never depend on outer layers
- `MotorcycleRAG.Contracts` = interfaces only (no DTOs/models)
- `MotorcycleRAG.Contracts.Models` = shared DTOs only (no interfaces, no implementations, no infrastructure deps)
- Application services belong in the Application `Services` folder unless an explicit architecture rule or user approval says otherwise.
- Do not create new feature/random folders such as `Pipeline` without explicit approval.
- Do not add new top-level repository folders or root-level tooling directories without explicit user approval. Existing root folders such as `tools/` and `scripts/` are intentional and should only grow when there is a clear repo-wide need.
- If a type enforces business rules/invariants → Domain. If it's for transport/serialization → Contracts.Models DTO.
