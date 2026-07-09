# 1. Project Overview

Multi-agent RAG system for motorcycle information retrieval. OWASP ASVS Level 2 security. Clean Architecture + DDD.

At the beginning of each task/response, include: `[******Working Agreement: Active******]`
<!-- edit-test: dotnet-dev verified 2026-07-08 SUCCESS-2 -->

# 2. Global Rules

@6-Docs/agent-instructions/working-agreement.md
@6-Docs/agent-instructions/azure-environment.md
@6-Docs/agent-instructions/security.md

**Deprecated docs:** `6-Docs/archive/` holds retired/deprecated documents and agent definitions kept only for historical reference. Ignore it. Do not read, cite, follow, or copy anything from `6-Docs/archive/` as current guidance, and do not treat agent definitions found there as active agents.

# 3. Architecture

Before creating, moving, renaming, or choosing placement for any source or test file, or changing namespaces, project references, DTO placement, interface placement, or layer boundaries — read the architecture placement rules first at `6-Docs/rules/architecture-general.md`.

**Key rules always in effect:**
- 1 class or interface per file in C#
- Dependency Rule: inner layers never depend on outer layers
- `MotorcycleRAG.Contracts` = interfaces only (no DTOs/models)
- `MotorcycleRAG.Contracts.Models` = shared DTOs only (no interfaces, no implementations, no infrastructure deps)
- Application services belong in the Application `Services` folder unless an explicit architecture rule or user approval says otherwise.
- Do not create new feature/random folders such as `Pipeline` without explicit approval.
- Do not add new top-level repository folders or root-level tooling directories without explicit user approval. Existing root folders such as `tools/` and `scripts/` are intentional and should only grow when there is a clear repo-wide need.
- If a type enforces business rules/invariants → Domain. If it's for transport/serialization → Contracts.Models DTO.
