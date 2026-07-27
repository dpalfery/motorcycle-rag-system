---
id: system/agent-governance
title: Working Agreement Rules
doc-type: governance
status: current
component: MotorcycleRAG system
owner: Maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Working Agreement Rules

1. **No infrastructure without approval** — Don't create docker-compose, Makefiles, CI/CD pipelines, IaC, or deployment scripts without explicit approval.
2. **Permission before major decisions** — Ask before creating documentation files, adding/upgrading dependencies (NuGet/npm), or implementing cross-cutting concerns.
3. **Communication protocol** — Present options with trade-offs. Wait for user confirmation on architectural decisions.
4. **No files in project root** — Agent-generated files (status, plan, summary, build output, etc.) go in the path declared as **Agent Scratchpad** in the root `AGENTS.md` Config Registry (git-ignored). Never place them under `6-Docs/`, which holds canonical documentation only.
5. **Git commands require approval** — Only read-only (`git status`, `git diff`, `git log`) and staging (`git add`) are allowed without asking. Never run `git commit`, `git push`, `git reset`, `git restore`, `git checkout`, `git clean`, or `git rebase` without explicit approval. Don't ask to do commits — the human does them manually.
6. **No fallbacks or workarounds without explicit user consent** — Never implement a degraded fallback, stub, or platform-specific workaround to avoid fixing the real problem. Fix the root cause. If a workaround is genuinely the right trade-off, present it to the user with clear rationale and wait for explicit approval before implementing.

## DevOps

All deployments go through GitHub Actions. The pipeline (`deploy.yml`) is the **only** permitted path to deploy infrastructure or application changes.

- **`pulumi up` is FORBIDDEN** for agents — never run it directly. IaC changes deploy automatically when commits are pushed to `develop` or `main`.
- **`az` CLI is read-only** — agents may use `az` only to read state and diagnose issues (e.g., `az containerapp logs show`, `az containerapp show`, `az acr repository list`). Never use `az` to create, update, or delete any Azure resource.
- **No direct Docker builds or ACR pushes** — never run `docker build`, `docker push`, or `az acr build`. Images are built and pushed exclusively by the pipeline.
- **Deployment = commit + push** — the correct response to any infrastructure or application fix is to commit the code change and push to trigger the pipeline.
- Any `az` write action during debugging must be flagged to the user and approved first. We must be able to delete the whole solution and rebuild with no manual intervention.

Once you have read these rules, add to the response: `[******Working Agreement: Active******]`
