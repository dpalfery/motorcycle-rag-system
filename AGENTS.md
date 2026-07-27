# MotorcycleRAG Repository Instructions

`AGENTS.md` files are mandatory instructions, not optional background material. Read this file before work anywhere in the repository, then read the nearest scoped `AGENTS.md` before changing files in that subtree.

## Non-negotiable rules

- **CodeGraph first:** Before using `grep`, `rg`, `find`, shell globbing, or direct file reads to locate or understand repository files or source code, use CodeGraph. Prefer the CodeGraph MCP tool; if it is unavailable, run `codegraph explore`. Use another discovery or read method only when CodeGraph fails to return a relevant, sufficiently complete source result, and state that failure before using the fallback.
- **Kyber-Weave for documentation:** Before grepping or reading files under `6-Docs/` to answer a question, use the Kyber-Weave MCP tool `mcp__kyber-weave__docs_explore`. It ranks on declared frontmatter identity, returns the one relevant `##` section rather than a whole runbook, and carries that document's resolved joins to the code graph. Before renaming, moving, or changing the contract of a code symbol, use `mcp__kyber-weave__docs_for_symbol` to find the documentation that must change with it: a `code-refs` entry is a formal claim of ownership, which grep cannot distinguish from a passing prose mention. There is no CLI equivalent of either tool — if the MCP server is unavailable, fall back to the [documentation index](6-Docs/README.md) and state that the tool was unavailable. The corpus excludes `6-Docs/archive/`, which is historical and is never retrieved as current guidance.
- Do not create infrastructure, deployment assets, dependencies, cross-cutting concerns, or documentation files without user approval. Ask before an architectural decision; present the trade-offs.
- Do not commit, push, reset, restore, checkout, clean, or rebase without explicit user approval. Keep agent-generated notes in the path declared as **Agent Scratchpad** in the Config Registry below, never at repository root and never under `6-Docs/`.
- Do not introduce fallbacks, stubs, or workarounds without explicit approval. Fix the root cause.
- Never commit secrets, tokens, connection strings, passwords, customer data, or `.env` files. Use approved configuration and Key Vault patterns; redact prompts and PII from logs.
- .NET code must use Azure App Configuration and Key Vault references for application configuration and secrets. Python local-processor runtime values set by Admin Desktop are the only approved environment-variable exception.
- Before every `az` read, verify the active subscription against the allowlist in [Azure agent access](6-Docs/AzureEnvironment/agent-access.md). Azure writes, local `pulumi up`, direct Docker builds, and ACR pushes are forbidden.
- Preserve Clean Architecture: inner layers never depend on outer layers; Contracts contains interfaces only; Contracts.Models contains shared DTOs only; business invariants belong in Domain; Application services belong in `Services`.
- Do not create new files or folders at repository root, with the single exception of the **Agent Scratchpad** declared in the Config Registry below. Scripts, tools, and deployment assets belong under `7-Deployment/`; documentation belongs under `6-Docs/`; generated notes belong in the scratchpad. `6-Docs/` holds canonical documentation only — never scratch output, vendored packages, or git-ignored working files.

Read the full [working agreement](6-Docs/system/agent-governance.md), [security directives](6-Docs/system/security.md), and [Azure environment rules](6-Docs/AzureEnvironment/agent-access.md) when the task touches their subject.

## Documentation and placement

Retrieve with `mcp__kyber-weave__docs_explore` first, per the non-negotiable rule above. The [documentation index](6-Docs/README.md) is the canonical entry point for browsing, and the fallback when the tool is unavailable. Before changing a cataloged component, read the [documentation standard](6-Docs/documentation-standard.md), [component catalog](6-Docs/catalog.md), the source-root README, and the component's detailed documentation. Update canonical documentation when the public interface, configuration, architecture, runtime, operations, or workflow changes.

## Plan and specification closeout

Plans and specifications are both **work in progress with a shelf life, never canonical guidance.** Each records what was intended and goes stale the moment implementation diverges. Both are governed identically.

For plan-backed work, implementation completion does not close the plan. After implementation verification is complete, the orchestrator SHALL assign a `docs-dev` plan-closeout task before reporting the work complete. The documentation specialist SHALL verify the plan's acceptance criteria against the implementation evidence, update the affected canonical documentation, and maintain the [plan index](6-Docs/plans/README.md). Only then may it archive the plan under `6-Docs/archive/plans/` with status `Archived`; otherwise the plan remains `Review required` or returns to an active status.

For specification-backed work the same rule applies, with the same agent and the same gate: when the tasks are delivered and their tests pass, the orchestrator SHALL assign a `docs-dev` specification-closeout task. The documentation specialist SHALL verify the specification's requirements against the implementation evidence, migrate the durable content into canonical documentation, and maintain the [specification index](6-Docs/specs/README.md). Only then may it move the whole `6-Docs/specs/{feature-name}/` directory to `6-Docs/archive/specs/` with status `Archived`. Archiving a specification before its durable content has been migrated is the failure this gate exists to prevent.

The documentation standard defines both lifecycles in detail, and the rules for [architecture decision records](6-Docs/documentation-standard.md), which unlike plans and specifications stay current until superseded.

## Kyber-Weave

**Kyber-Weave** is the name of this repository's agent-and-documentation governance framework: the CLI, library, CI gates and MCP server that govern skills, agent definitions, and documentation as one. Its premise is that every artifact shaping agent behavior is a supply-chain artifact — parsed, validated against a closed spec, checked for drift against a source of truth, security-scanned, and made retrievable. The commands named throughout this file (`kyber-weave skill|agent|docs …`) and the `KW-*` rule codes are its surface. See the [Kyber-Weave reference](6-Docs/reference/kyber-weave.md).

## Agent configuration synchronization

Shared agent-role behavior SHALL remain synchronized across all six supported development-tool configurations: Codex (`.codex/agents/`), Cursor (`.cursor/agents/`), GitHub Copilot (`.github/agents/`), OpenCode (`.opencode/agents/`), Kilo (`.kilo/agents/`), and Claude (`.claude/agents/`). When changing an agent's instructions, scope, workflow, responsibilities, or policy, update the corresponding role in every one of these locations in the same change. Platform-specific metadata such as model names, tools, permissions, and front matter may differ, but the role behavior must remain equivalent. If a required counterpart is missing or cannot represent the change, resolve or explicitly report the discrepancy before claiming the agent update is complete.

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
| Infrastructure | [Infrastructure AGENTS](7-Deployment/infrastructure/AGENTS.md) | [DevOps documentation](6-Docs/DevOps/) and [Azure environment](6-Docs/AzureEnvironment/) |

## Instruction hierarchy

1. This root file supplies repository-wide mandatory policy.
2. The nearest scoped `AGENTS.md` supplies additional rules for its subtree; it may not weaken this file.
3. Canonical system, component, deployment, and environment documentation provides detailed task-specific guidance. Scoped instructions link directly to the owning document; they are not a substitute for root policy.

## Repository Configuration & Paths Registry (Config Reg)

Agents and skills should look up the following properties dynamically to find the relevant documentation and references for this repository:

- **Documentation Index:** `6-Docs/README.md`
- **Documentation Standard:** `6-Docs/documentation-standard.md`
- **Documentation Ontology:** `6-Docs/documentation-ontology.md`
- **Governance Framework (Kyber-Weave):** `7-Deployment/tools/KyberWeave/` — reference: `6-Docs/reference/kyber-weave.md`; CLI project: `7-Deployment/tools/KyberWeave/src/KyberWeave.Cli`; MCP server project: `7-Deployment/tools/KyberWeave/src/KyberWeave.Mcp`
- **Skill Validation Script:** `7-Deployment/scripts/kyber-weave-validate.sh`
- **Agent Scratchpad:** `.agent-scratch-pad/`
- **Clean Architecture Rules:** `6-Docs/rules/architecture-general.md`
- **Component Catalog:** `6-Docs/catalog.md`
- **Plan Index:** `6-Docs/plans/README.md`
- **Specification Index:** `6-Docs/specs/README.md`
- **Architecture Decision Records:** `6-Docs/adr/` (archived: `6-Docs/archive/ADRs/`)
- **Developer Setup Standard:** `6-Docs/DevOps/developer-setup-standard.md`
- **MSBuild Modernization:** `6-Docs/DevOps/msbuild-modernization.md`
- **MSBuild Anti-patterns:** `6-Docs/DevOps/msbuild-antipatterns.md`
- **Directory.Build Organization:** `6-Docs/DevOps/directory-build-organization.md`
- **Build Performance:** `6-Docs/DevOps/build-performance.md`
- **Incremental Build:** `6-Docs/DevOps/incremental-build.md`
- **Test Coverage Config:** `5-Test/scripts/coverage-config.json`
- **Test Runner Scripts:** `5-Test/scripts/run-comprehensive-tests.sh` (macOS/Linux), `5-Test/scripts/run-comprehensive-tests.ps1` (Windows)
- **Configuration Policy:** `6-Docs/reference/environment-variables.md`
- **Auth Design:** `6-Docs/reference/auth-design.md`
- **Azure Naming Standard:** `6-Docs/reference/azure-naming-standards.md`

Skills and other portable instruction files SHALL reference these paths by the property name above (e.g. "the path declared as **Test Coverage Config** in the repository root `AGENTS.md`") rather than embedding a relative link that traverses out of the skill's own directory (e.g. `../../5-Test/...`). This keeps skill files self-contained and correct if repository layout changes — only this table needs updating.
