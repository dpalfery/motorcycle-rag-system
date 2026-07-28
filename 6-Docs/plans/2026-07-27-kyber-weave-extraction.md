---
id: plans/2026-07-27-kyber-weave-extraction
title: 2026-07-27 Kyber-Weave Extraction
doc-type: plan
status: current
component: Kyber-Weave
owner: Maintainers
last-reviewed: 2026-07-28
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Kyber-Weave Extraction

**Status:** In progress  
**Date:** 2026-07-27  
**Goal:** Extract Kyber-Weave into its own repository as a frictionless cross-ecosystem CLI/MCP (npm + GitHub Releases/Homebrew; optional GitHub Packages; nuget.org forbidden) while MotorcycleRAG consumes PATH binaries and supplies host-only overrides.

---

## 1. Problem / Motivation

Kyber-Weave lives under `7-Deployment/tools/KyberWeave/` inside MotorcycleRAG. It is already a coherent product (CLI + Core + MCP + tests + samples) with pack metadata on Core/Cli, but it is **not extractable as shipped**:

- Documentation ontology vocabularies, required-key matrix, default docs root `6-Docs`, `catalog.md` column layout, and vendored-file exclusions are **hardcoded** in `DocSpecValidator` / `DocumentLoader`.
- `HarnessCapabilityProfile.DefaultProfiles` hardcodes MotorcycleRAG **conductor→skill** satisfaction overrides.
- CodeGraph access is a concrete `CodeGraphResolver` (sqlite3 CLI against `.codegraph/codegraph.db`), not a pluggable port.
- MCP is launched via `dotnet run --project 7-Deployment/tools/KyberWeave/...` in `.mcp.json`; `KyberWeave.Mcp` is `IsPackable=false`.
- Host CI (`pr-gate.yml`, `nightly.yml`), `kyber-weave-validate.sh`, and `.pre-commit-config.yaml` build from the vendored tree.

Without extraction + portability, every consumer must vendor source; the deferred roadmap in `6-Docs/reference/kyber-weave.md` already names own-repo publish, config-driven ontology, and pluggable code-graph backend.

**Product boundary (authoritative inventory):** skill CLI + agent CLI + docs governance/MCP + instruction-surface scanner + SkillForge lineage (LICENSE/NOTICE) + nested `kyber-weave skill|agent|docs …` + ontology default+override + CodeGraph adapter + sample/template GitHub Actions. **Not product kernel:** host skill/agent/docs *content*, conductor host policy, live host workflows that only *consume* templates, CodeGraph indexer, SkillSpector, MotorcycleRAG apps/Azure.

---

## 2. Approved decisions

| ID | Decision |
| --- | --- |
| **D1** | Ontology: product ships **default + override** (`kyber-weave.yml` or equivalent). |
| **D2** | CodeGraph: **adapter pattern** — interface + CodeGraph adapter as default. |
| **D3** | Skill CLI, agent CLI, instruction-surface scanner, SkillForge heritage, deferred `skill_explore`/`agent_explore` (roadmap) are **in product scope**. |
| **D4** | “Skills and agents should not be part of kyber” means **host content / host-only policy**, not exclusion of governance tooling. |
| **D5** | Conductor→skill `HarnessCapabilityProfile` overrides are **host policy**, not product hardcoding. |
| **D6** | Keep product name **Kyber-Weave**; thesis emphasizes agent scanning + documentation (+ skill tooling). |
| **D7** | CLI shape: keep nested `kyber-weave skill\|agent\|docs …` (no breaking promote-to-top-level). |
| **D8** | Product **ships sample/template GitHub Actions** for skill + agent + docs gates; hosts copy/adapt. |
| **D9** | **REPLACED 2026-07-28.** MotorcycleRAG (and any host — Java/PHP/Python/.NET) consumes Kyber-Weave as a **cross-ecosystem CLI + MCP binary**, installed via frictionless channels — **not** as a .NET library PackageReference and **not** primarily via nuget.org / `dotnet tool`. Host CI invokes `kyber-weave` / `kyber-weave-mcp` from PATH after npm, Homebrew, or a pinned GitHub Release asset. |
| **D10** | New GitHub repository identity: **`dpalfery/kyber-weave`** (user confirmed R1; authenticated login `dpalfery`). |
| **D11** | **Distribution (R2 closed 2026-07-28):** First-class channels are **(B) npm registry** and **(C) GitHub Releases + OS installers** (Homebrew in Phase 2; Scoop/winget/Chocolatey as documented follow-ups). **(A) GitHub Packages** remains an **optional** advanced channel for NuGet-format `dotnet tool` consumers — **not** the primary story. **nuget.org remains forbidden.** |
| **D12** | **Ship self-contained, platform-specific binaries** (single-file `dotnet publish` per RID: at least `osx-arm64`, `osx-x64`, `linux-x64`, `win-x64`) so installers do **not** require a preinstalled .NET runtime. Native AOT is optional follow-up, not Phase 2. npm package wraps/selects the correct binary; Homebrew/Releases consume the same artifacts. |

### Note for PM: what `PackAsTool` / NuGet is today (not a library)

`PackAsTool` on `KyberWeave.Cli` does **not** mean “add KyberWeave as a dependency of a .csproj.” It packages the **CLI executable** into a `.nupkg` so `dotnet tool install` can put `kyber-weave` on PATH — the same *kind* of job as shipping a Go binary or an npm global CLI, but using the NuGet *transport*. No host project needs to reference Core. Given the user’s cross-ecosystem / frictionless goal, that transport is **optional (GitHub Packages only)**; npm + Releases/brew are the default story.

---

## 3. Investigation findings

- Source tree: `KyberWeave.sln` with Core, Cli, Mcp, Tests; samples; LICENSE/NOTICE; README; `docs/alm-governance-playbook.md`.
- Cli: `PackAsTool=true`, `ToolCommandName=kyber-weave`, `PackageId=KyberWeave.Tool`, TFM `net10.0`.
- Core: `PackageId=KyberWeave.Core`, Markdig + YamlDotNet; `InternalsVisibleTo` Tests.
- Mcp: `IsPackable=false`; ModelContextProtocol 1.4.1; registered in `.mcp.json` as stdio `dotnet run --project`.
- Tests: xUnit project under `tests/KyberWeave.Tests` (~78 Fact/Theory sites across governance, retrieval, scanner, validation).
- Host touchpoints: `.github/workflows/pr-gate.yml` + `nightly.yml` (skill/agent/docs gates), `7-Deployment/scripts/kyber-weave-validate.sh`, `.pre-commit-config.yaml`, AGENTS.md Config Registry paths, `6-Docs/reference/kyber-weave.md`.
- Parent `Directory.Build.props` supplies analyzers/nullable; extracted repo needs its own minimal props so builds do not depend on MotorcycleRAG.
- Deferred in canonical reference (still true): config ontology, pluggable code-graph, `agent_explore`/`skill_explore`, own repo, publish — this plan covers extraction + portability prerequisites; **does not** implement explore MCP tools or unwired `agent route|lint|new`.

**Resolved inventory Qs:** product boundary reconciled.

**Phase 2 status:** D10 repo + **D11 multi-channel distribution** + **D9/D12 frictionless binaries** locked. nuget.org forbidden. Phase 2b unblocked after Phase 1.

---

## 4. Test contract

Runner for KW unit contracts (Phases 1 and post-move 2):

```bash
dotnet test 7-Deployment/tools/KyberWeave/tests/KyberWeave.Tests/KyberWeave.Tests.csproj
```

After extraction, the same project path relative to the **new** repo root. Existing suites must remain GREEN through refactors.

| Task # | Test project / file | Runner command | Behavior asserted (RED → GREEN) |
| --- | --- | --- | --- |
| T1 | `KyberWeave.Tests` / `OntologyConfigTests.cs` (new) | `dotnet test …/KyberWeave.Tests.csproj --filter OntologyConfig` | Shipped defaults reproduce current closed vocab + required-key matrix; a minimal override YAML adds/removes a required key and changes exclusions/docs-root/catalog-column mapping; invalid YAML fails with a parse diagnostic (not silent fallback). |
| T2 | `KyberWeave.Tests` / `HarnessProfileConfigTests.cs` (new) | `dotnet test … --filter HarnessProfileConfig` | Product default profiles include six harness directories **without** conductor→skill auto-satisfaction; loading a host override that maps `conductor`→`conductor` makes sync-check treat that role as satisfied via skill dir when present; without override, missing harness role still emits `KW-AGENT-SYNC-001`. |
| T3 | `KyberWeave.Tests` / `CodeGraphPortTests.cs` (new) | `dotnet test … --filter CodeGraphPort` | `DocDriftLinter` / `DocGraphExporter` / `DocumentIndex.Build` accept `ICodeGraphResolver`; a fake resolver drives deterministic drift failures and join emission; `CodeGraphResolver` adapter preserves resolve-by-name/route/`HasFilesUnder` semantics against a fixture DB or recorded sqlite3 stub where feasible. |
| T4 | `KyberWeave.Tests` / `McpPackagingTests.cs` (new) **or** publish smoke | `dotnet test … --filter McpPackaging` plus manual publish smoke | MCP entry point packs/publishes as a **standalone executable** (self-contained RID publish); tool descriptions are generic (no hard `MotorcycleRAG`). Optional GitHub Packages `PackAsTool` may remain but is **not** required for T4 GREEN. |
| T5 | **no-test** (template assets) | Manual: YAML parse + workflow lint | Sample GHA templates cover skill/agent/docs gates; pin placeholders for **npm version or release tag**, not nuget.org. |
| T6 | **no-test** (multi-channel publish) | Manual matrix: (1) `npm i -g @dpalfery/kyber-weave@<ver>` → `kyber-weave --help` + `kyber-weave-mcp --help`; (2) download GitHub Release asset for current RID → same; (3) Homebrew formula/cask from that Release → same; (4) optional: `dotnet tool install` from GitHub Packages only | All first-class channels work; **no** nuget.org step; no .NET SDK required for npm/Release/brew paths. |
| T7 | MotorcycleRAG host: contract via existing KW tests + **host override fixture** in KW tests named for host profile | `dotnet test … --filter MotorcycleRagHostProfile` (lives in KW tests until cutover, then may move to host or stay as sample) | Host-shaped `kyber-weave.yml` (docs-root `6-Docs`, catalog columns, vendored exclusions, conductor overrides) restores current MotorcycleRAG sync-check/docs-validate expectations. |
| T8 | **no-test** (host cutover wiring) | Manual CI dry-run: install via **npm** (or Release asset) in CI; `docs validate` / `skill validate` / `agent sync-check` exit 0; `.mcp.json` invokes `kyber-weave-mcp` on PATH | No `dotnet run --project` into vendored tree; no nuget.org; vendored KW tree removed or stub. |
| T9 | **no-test** (docs closeout) | Manual review against documentation standard | Docs describe npm / brew / Releases as primary; GitHub Packages optional; nuget.org absent. |

---

## 5. Task list

| # | Phase | Component | Description | Skills |
| --- | --- | --- | --- | --- |
| 1a | 1 – Portability | Tests | **RED:** author T1–T4 (+ T7 fixture) tests per §4; confirm they fail on current code. | test-dev |
| 1b | 1 – Portability | Core | **GREEN T1:** introduce ontology config model + loader; ship embedded/default ontology matching today’s enums/rules; wire `DocumentLoader`/`DocSpecValidator` to config; support `--config` / repo-root `kyber-weave.yml`. | dotnet-dev |
| 1c | 1 – Portability | Core | **GREEN T2:** move harness profiles to defaults + override; remove baked conductor skill map from product defaults; `AgentSyncLinter` reads config. | dotnet-dev |
| 1d | 1 – Portability | Core | **GREEN T3:** extract `ICodeGraphResolver`; rename/adapt `CodeGraphResolver` as default adapter; update Docs commands, `DocumentIndex`, `DocumentIndexHost`, drift, export. | dotnet-dev |
| 1e | 1 – Portability | Mcp / publish | **GREEN T4:** MCP as standalone entry; generic branding; groundwork for self-contained publish (D12). Optional `PackAsTool` only for GitHub Packages — demoted as primary. | dotnet-dev |
| 1f | 1 – Portability | Templates | **T5:** sample GHA templates; pin via npm version / release tag. | github-devops |
| 1g | 1 – Portability | Tooling | Extracted-repo `Directory.Build.props`; RID publish scripts/props for self-contained single-file (D12). | dotnet-dev |
| 2a | 2 – New repo | Repo | Create **`dpalfery/kyber-weave`** (D10); push tree; CI build/test; release workflow scaffolding. | github-devops |
| 2b | 2 – New repo | Publish | **T6 / D11 / D12:** Release matrix builds RID binaries for CLI + MCP; attach to GitHub Releases; publish npm **`@dpalfery/kyber-weave`** (bins `kyber-weave`, `kyber-weave-mcp`); Homebrew formula from Release; optional GitHub Packages `dotnet tool`; **never** nuget.org. | github-devops, dotnet-dev |
| 3a | 3 – Host cutover | MotorcycleRAG | Root `kyber-weave.yml` (T7); CI installs KW via **npm** (pinned) or Release asset — **not** `.config/dotnet-tools.json` as primary. | github-devops, dotnet-dev |
| 3b | 3 – Host cutover | CI/scripts | **T8:** workflows/scripts/pre-commit call PATH binaries; `.mcp.json` → `kyber-weave-mcp`; remove vendored tree. | github-devops |
| 4a | 4 – Docs | Docs | **T9:** frictionless install (npm/brew/Releases); optional GitHub Packages; nuget.org absent. | app-docs-standard |

**Acceptance criteria (plan done):** all §4 GREEN/manual checks pass; MotorcycleRAG CI uses frictionless install (npm or Release); no nuget.org; no required vendored KW ProjectReference; host override restores prior governance behavior; docs describe the split.

---

## 6. Sequencing / dependency graph

```
1a (RED T1–T4,T7)
 ├── 1b (T1 ontology) ──┐
 ├── 1c (T2 harness)  ──┼── 1g (isolated props + RID publish) ── 2a (repo) ── 2b (npm+Releases+brew [+ optional GH Packages])
 ├── 1d (T3 codegraph) ─┤         ▲                      │
 └── 1e (T4 mcp/binary)─┘         │                      │
 1f (T5 templates) ───────────────┘                      │
                                                         ▼
                                              3a (host yml + npm/Release pin)
                                                         │
                                                         ▼
                                              3b (CI/mcp/scripts; remove tree)
                                                         │
                                                         ▼
                                              4a (docs closeout)

Phase 2 (2a/2b) unblocked (D10/D11/D12) after Phase 1 GREEN.
nuget.org remains forbidden.
Test-dev RED tasks precede corresponding GREEN implementation tasks (conductor-v3).
```

---

## 7. Residual decisions / risks

### Decision ledger

| ID | Status | Resolution |
| --- | --- | --- |
| **R1** | **Approved → D10** | Repo `dpalfery/kyber-weave`. |
| **R2** | **Approved → D11** | npm + GitHub Releases/OS installers first-class; GitHub Packages optional; nuget.org forbidden. |
| **D9** | **Replaced** | Frictionless cross-ecosystem CLI/MCP — not NuGet-primary host consumption. |
| **D12** | **Approved** | Self-contained single-file RID binaries; npm name **`@dpalfery/kyber-weave`**. Native AOT deferred. |

### Risks

| Risk | Mitigation |
| --- | --- |
| Self-contained binary size | Accept larger Release assets; AOT later if needed. |
| npm package must not require Node at CLI runtime | Wrapper only selects/execs native binary; Node needed only for `npm i -g`. |
| Homebrew maintenance | Formula in same release workflow; bumps with tags. |
| Optional GitHub Packages confuses “no nuget” | Docs: optional for .NET specialists; not nuget.org; not required. |
| Conductor parity | T2 + T7 host YAML. |
| Parent `Directory.Build.props` drift | Task 1g isolated build. |

---

## 8. Out of scope

| Item | Why / where it belongs |
| --- | --- |
| Implement `skill_explore` / `agent_explore` MCP tools | Product roadmap |
| Wire unwired `agent route` / `agent lint` / `agent new` | Separate plan |
| CodeGraph indexer / `codegraph` CLI | External dependency |
| NVIDIA SkillSpector jobs | Adjacent host scanner |
| Changing MotorcycleRAG skill/agent/docs **content** | Host authorship |
| **nuget.org** publishing | Forbidden |
| Native AOT publish | Follow-up after D12 single-file works |
| Scoop / winget / Chocolatey formulas | Follow-up after Homebrew |
| Azure / app / Pulumi changes | Unrelated |

---

## 9. Required skills

- `test-dev` — RED contracts T1–T4, T7
- `dotnet-dev` — Core/Cli/Mcp portability, self-contained RID publish
- `github-devops` — repo CI, Releases, npm publish, Homebrew, optional GitHub Packages, host cutover
- `app-docs-standard` — canonical docs + AGENTS registry closeout (Phase 4)

---

## 10. Verification harness

Plan complete only when:

1. Every §4 automated contract is GREEN; T5/T6/T8/T9 manual checks recorded (npm + Release + brew; no nuget.org).
2. `code-reviewer` APPROVED for Phase 1–3 code/CI changes.
3. `security-review` on release/npm publish, binary download integrity, MCP stdio surface.
4. MotorcycleRAG CI runs governance via PATH binaries from frictionless install + host `kyber-weave.yml`.
5. Vendored product source under `7-Deployment/tools/KyberWeave` is gone or non-authoritative stub.
6. Docs closeout (4a) updated; plan archive only after docs-dev verification.

**Implementation authority:** plan is **In progress**. Phase 2 publish **unblocked** under D10/D11/D12 after Phase 1 acceptance.
