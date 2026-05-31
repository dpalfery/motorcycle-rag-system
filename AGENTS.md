# 1. Working Agreement Rules

1. **No infrastructure without approval** — Don't create docker-compose, Makefiles, CI/CD pipelines, IaC, or deployment scripts without explicit approval.
2. **Permission before major decisions** — Ask before creating documentation files, adding/upgrading dependencies (NuGet/npm), or implementing cross-cutting concerns.
3. **Communication protocol** — Present options with trade-offs. Wait for user confirmation on architectural decisions.
4. **No files in project root** — Agent-generated files (status, plan, summary, build output, etc.) go in `6-Docs/agent-notes/` (git-ignored).
5. **Git commands require approval** — Only read-only (`git status`, `git diff`, `git log`) and staging (`git add`) are allowed without asking. Never run `git commit`, `git push`, `git reset`, `git restore`, `git checkout`, `git clean`, or `git rebase` without explicit approval. Don't ask to do commits — the human does them manually.
6. **DevOps** — All deployments go through GitHub Actions. The pipeline (`deploy.yml`) is the **only** permitted path to deploy infrastructure or application changes.
   - **`pulumi up` is FORBIDDEN** for agents — never run it directly. IaC changes deploy automatically when commits are pushed to `develop` or `main`.
   - **`az` CLI is read-only** — agents may use `az` only to read state and diagnose issues (e.g., `az containerapp logs show`, `az containerapp show`, `az acr repository list`). Never use `az` to create, update, or delete any Azure resource.
   - **No direct Docker builds or ACR pushes** — never run `docker build`, `docker push`, or `az acr build`. Images are built and pushed exclusively by the pipeline.
   - **Deployment = commit + push** — the correct response to any infrastructure or application fix is to commit the code change and push to trigger the pipeline.
   - Any `az` write action during debugging must be flagged to the user and approved first. We must be able to delete the whole solution and rebuild with no manual intervention.

# 2. Development Environment

- Windows 11, WSL2, Docker
- Assume Windows-native solutions unless easy Docker alternative exists

## Azure Tenant Map

- **EPAM tenant**: compute and hosting tenant for this solution. This is where the Azure hosting resources for the repo live, including the subscription backed by MSDN credits.
- **`palfery.onmicrosoft.com` tenant**: David's primary Entra ID tenant and the home for personal Azure subscriptions.
- **`0f8f8a52-f135-43af-af88-e0b54ca9ff91` tenant**: the Entra External ID tenant associated with `palfery.onmicrosoft.com`.
- **Do not assume** Azure hosting resources, personal subscriptions, workforce app registrations, and External ID objects live in the same tenant.
- Before using `az` or checking Entra objects, verify which tenant actually owns the target resource or identity.

# 3. Project Overview

Multi-agent RAG system for motorcycle information retrieval. OWASP ASVS Level 2 security. Clean Architecture + DDD.


# 4. Architecture

**Use the `clean-architecture` skill** for all folder structure, layer dependency, and DTO placement rules.

**Key rules always in effect:**
- 1 class or interface per file in C#
- Dependency Rule: inner layers never depend on outer layers
- `MotorcycleRAG.Contracts` = interfaces only (no DTOs/models)
- `MotorcycleRAG.Contracts.Models` = shared DTOs only (no interfaces, no implementations, no infrastructure deps)
- If a type enforces business rules/invariants → Domain. If it's for transport/serialization → Contracts.Models DTO.

# 5. Security Directives

These are **non-optional** and apply to all code, tests, config, scripts, and docs.

### Secrets
- **NEVER** hardcode secrets, connection strings, tokens, or passwords in any file — ever.
- C#/.NET application code must use Azure App Configuration for configuration and Azure Key Vault references for secrets. Do not read application settings or secrets directly with `Environment.GetEnvironmentVariable()` in C#/.NET code.
- The only approved environment-variable usage is the Python local processor runtime values that the Admin app sets at run time.
- No `.env` files. Appsettings files must never contain secrets.
- It is better the app not work than for a secret to be exposed.

### Input Handling
- SQL: parameterized queries ONLY. Never string concatenation.
- HTML/UI: encode output to prevent XSS.
- Logging: use structured logging with placeholders. Never concatenate user input into log strings. Redact PII and query text.

### Auth & Access
- Default = no access. Permissions explicitly granted.
- Authorize every action (e.g., `[Authorize(Policy = "mcr-api-admin")]`).
- Admin endpoints must validate `azp` matches the Admin App Client ID.
- Enforce rate limiting on public APIs.

### Communication
- Enforce HTTPS + HSTS on all web server configurations.

# 6. Task Header Markers

At the beginning of each task/response, include:
`[******Working Agreement: Active******]`


