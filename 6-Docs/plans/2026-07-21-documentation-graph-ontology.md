---
id: plans/2026-07-21-documentation-graph-ontology
title: 2026-07-21 Documentation Graph Ontology and Frontmatter Governance
doc-type: plan
status: current
component: MotorcycleRAG system
owner: Maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# 2026-07-21 Documentation Graph Ontology and Frontmatter Governance

**Status:** In progress
**Date:** 2026-07-21
**Goal:** Give `6-Docs/` a rigid, closed ontology expressed as human-readable YAML frontmatter that joins deterministically to CodeGraph node identities, evict non-documentation from the documentation root, and enforce both schema conformance and code-entity drift in CI through the implemented SkillForge governance engine.

---

## Context

`6-Docs/` holds ~110 markdown files that read well for humans but are structurally opaque to a graph ingester.

- **No document carries frontmatter.** The only YAML-fronted files in the tree are vendored skill files under `6-Docs/DevOps/` (for example `DevOps/incremental-build.md`). Every real document encodes metadata as ad-hoc bold key-value pairs — `**Component:**`, `**Status:**`, `**Date:**`, `**Audience:**` — with no consistent key set, no closed vocabulary, and no machine-parseable form.
- **CodeGraph indexes zero markdown.** `.codegraph/codegraph.db` holds 22,296 nodes across 1,106 source files, and `select count(*) from files where path like '%.md'` returns **0**. The deterministic code graph and the semantic documentation corpus are two disconnected islands with no join key.
- **CodeGraph already emits the entities the ontology needs, canonicalized.** `route` (130 nodes, named as exact strings such as `GET /api/me/usage` with the controller prefix already resolved), `interface` (149), `class` (1,188), `method` (7,406) — each carrying `qualified_name`, `file_path`, and a stable `id`. These are the join keys; nothing needs LLM inference.
- **Directory placement is overloaded as ontology and is already failing.** The four `6-Docs/operations/data-protection-*.md` files are entirely `MotorcycleRag.WebUI.BFF`-scoped — each declares `**Component:** MotorcycleRag.WebUI.BFF` — yet nothing links to them from the Web UI documentation, [catalog.md](../catalog.md), or [6-Docs/README.md](../README.md). They are orphans. A directory alone cannot express "this runbook operates this component."
- **A git-ignored scratchpad sits inside the documentation root.** `.gitignore:33` ignores `6-Docs/agent-notes/**` in full; nothing beneath it is tracked. Every documentation glob, ingestion sweep, and link check must special-case it. It also holds a hand-cloned copy of `dpalfery/dpalfery-agent-package` — with its own nested `.git/` and roughly 65 markdown files — placed outside the location APM defines.

The intended outcome is an ontology that stays readable to humans, joins deterministically to CodeGraph identities rather than relying on inference, and is CI-enforced so that entity drift — a renamed symbol leaving a dangling documentation reference — fails the pull request instead of silently rotting the graph.

### Decisions taken

| Decision | Choice |
| --- | --- |
| Consumer | Both agent retrieval and an external graph tool. CodeGraph is the preferred single store. Documents must remain human-readable. |
| Restructure | Targeted moves plus frontmatter. No wholesale mirror of the code tree. |
| Enforcement | CI-blocking on both schema and entity drift, delivered through SkillForge. |
| Scope | Active canonical documentation only (~75 files). Excludes `archive/` and the scratchpad. |
| Scratchpad | Relocate out of `6-Docs/` to a root `.agent-scratch-pad/`; delete `6-Docs/agent-notes/`. |
| APM package | Adopt APM properly in place of the hand-placed clone. |

### Dependency: SkillForge agent governance (implemented and archived)

The SkillForge multi-harness agent governance work is implemented; its plan has been archived and is historical only. The dependency below is on the **shipped code**, verified in the working tree — not on the archived plan document. Its infrastructure is present and directly reusable, so this plan **consumes** it rather than coordinating with it:

| Implemented asset | Path | Reuse |
| --- | --- | --- |
| `Diagnostic` / `DiagnosticReport` / `Severity` | `SkillForge.Core/Diagnostics/Diagnostic.cs` | Rule codes, severity roll-ups, exit-code policy |
| Frontmatter parsing stack | `SkillForge.Core/Agents/Parsing/MarkdownAgentParser.cs` | Markdig `UseYamlFrontMatter` + YamlDotNet `HyphenatedNamingConvention`, `IgnoreUnmatchedProperties` |
| `ReportRenderer` | `SkillForge.Cli/Rendering/ReportRenderer.cs` | `OutputFormat { Table, Json, Sarif, Markdown }` — complete, no changes needed |
| CLI branch pattern | `SkillForge.Cli/Program.cs:40` (`config.AddBranch("agent", …)`) | Mirror as `AddBranch("docs", …)` |
| Model/loader/validator shape | `SkillForge.Core/Agents/{Model,Parsing,Validation}` | `AgentModel`/`AgentSet`/`AgentLoader`/`AgentSpecValidator` are the structural template for their `Docs` peers |

Note that only three of the seven originally planned `skillforge agent` subcommands are wired in `Program.cs` (`validate`, `sync-check`, `catalog`), although `AgentPromptScanner` and `AgentRoutingEvaluator` exist in Core. That gap is outside this plan's scope and does not block it — the `docs` branch registers independently.

Two consequences follow directly from the implemented code and are load-bearing for this plan:

1. **Frontmatter keys must be hyphenated, not snake_case.** The deserializer is configured with `HyphenatedNamingConvention`. The schema below uses `doc-type`, `source-root`, `code-refs`, `api-endpoints`, `last-reviewed`, `decided-by` accordingly.
2. **`Diagnostic.SkillName` needs generalizing.** The record's subject field and the `ReportRenderer` table column labelled `Skill` are skill-specific. Rename the field to `Subject` with a `SkillName` compatibility alias, and make the column header format-supplied — a small, backwards-compatible change that keeps the upstream PR to `bonaniibm/SkillForge` coherent for both artifact classes.

---

## The Ontology

A closed set. Adding a type is a change to the ontology document, not an authoring decision.

### Node types

**Code-side — never hand-authored; always projected from `.codegraph/codegraph.db`:**

| Node | CodeGraph source | Identity |
| --- | --- | --- |
| `API_Endpoint` | `kind='route'` | `name` — exact `METHOD /path` string |
| `Service` | `kind='class'` under `2-Application/**/Services/**` | `qualified_name` |
| `Repository` | `kind='class'` under `4-Persistence/**` | `qualified_name` |
| `Interface` | `kind='interface'` | `qualified_name` |
| `Symbol` | any other `class` / `method` / `function` | `qualified_name` |
| `SourceFile` | `kind='file'` | `file_path` |

**Documentation-side — one node per markdown file, from frontmatter:**

| Node | Discriminator |
| --- | --- |
| `Document` | `doc-type` field |

**Concept-side — authored, closed vocabulary:**

| Node | Source |
| --- | --- |
| `Component` | must match a Component name in [catalog.md](../catalog.md) |
| `Design_Decision` | one per file in `6-Docs/adr/` |
| `Team` | `owner` field, closed to the 11 owner values already in the catalog |

### Edge types

| Edge | Derived from | Determinism |
| --- | --- | --- |
| `DOCUMENTS` | `component` → `Component` | exact string match against the catalog |
| `DESCRIBES` | `source-root` → `SourceFile*` | path prefix; must exist on disk |
| `REFERENCES` | `code-refs[]` → `Symbol` / `Service` / `Repository` / `Interface` | resolved to a CodeGraph node `id` |
| `EXPOSES` | `api-endpoints[]` → `API_Endpoint` | exact match against a `route` node `name` |
| `OWNED_BY` | `owner` → `Team` | closed vocabulary |
| `DECIDED_BY` | `decided-by[]` → `Design_Decision` | document id match |
| `SUPERSEDES` | `supersedes[]` → `Document` | document id match |
| `LINKS_TO` | markdown body relative links | already validated by `7-Deployment/scripts/validate-docs.sh` |
| `CALLS` / `IMPLEMENTS` / `CONTAINS` | CodeGraph `edges` table | pre-existing, 60,188 edges |

Multi-hop reasoning falls out of this without inference: *runbook → `REFERENCES` → `DataProtectionHealthCheck` → `CALLS` → `IDataProtectionBlobProbe` → `IMPLEMENTS` → `AzureBlobDataProtectionProbe`*.

### Frontmatter schema

Deliberately small — eleven keys, most optional — so a human reading the raw file is not wading through metadata.

```yaml
---
id: webui-bff/data-protection-operations      # stable slug; never changes once assigned
title: Data Protection Key Persistence — Operations Guide
doc-type: runbook                              # closed vocabulary, below
status: current                                # current | draft | needs-review | superseded
component: MotorcycleRAG Web UI and BFF        # must match the catalog Component column
source-root: 1-Presentation/MotorcycleRag.WebUI.BFF
owner: Web UI maintainers                      # must match the catalog Owner column
last-reviewed: 2026-07-19
code-refs:                                     # exact CodeGraph symbol names
  - DataProtectionHealthCheck
  - IDataProtectionBlobProbe
  - AddBffDataProtection
api-endpoints: []                              # exact 'METHOD /path' route strings
decided-by: []                                 # ADR ids
supersedes: []
---
```

**`doc-type` closed vocabulary**, derived from the eight document shapes that exist in the tree today:
`architecture` · `onboarding` · `requirements` · `adr` · `plan` · `spec` · `runbook` · `reference` · `rule` · `governance` · `index`

**Required-key matrix:**

| doc-type | additionally required |
| --- | --- |
| `architecture` | `component`, `source-root`, non-empty `code-refs` |
| `requirements` | `component` |
| `onboarding` | `component`, `source-root` |
| `runbook` | `component`, non-empty `code-refs` |
| `adr` | `status`, `last-reviewed` |
| `plan`, `spec` | `status`, `component` |
| `reference`, `rule`, `governance`, `index` | base keys only |

A document that explains an API endpoint **must** carry the exact route string in `api-endpoints`. This is the strict frontmatter policy, made checkable.

### Naming and layout conventions

- File names are `kebab-case.md`. Component-scoped operational documents take a component prefix: `operations/webui-bff-data-protection-operations.md`, not `operations/data-protection-operations.md`.
- Component documentation folders keep their existing names, which already mirror source project names (`6-Docs/MotorcycleRAG.API` ↔ `1-Presentation/MotorcycleRAG.API`). `source-root` makes that mirror explicit and verifiable rather than implied.
- The directory tree stays topic-based. Frontmatter, not path, carries the component relation.
- `6-Docs/` contains only canonical documentation: no scratch, no vendored packages, no ignored subtrees.

---

## Implementation

### Phase 1 — Ontology of record

1. Add `6-Docs/documentation-ontology.md` containing the node, edge, and frontmatter tables above. It is the single source of truth for the schema; the validator and the exporter both cite it.
2. Amend [documentation-standard.md](../documentation-standard.md):
   - Add a **Frontmatter policy** section under "Required content" linking to the ontology document and stating that the policy is CI-enforced.
   - Amend the "Locations and canonical sources" bullet so that the component relation is carried by `component:` rather than by directory, removing the ambiguity that stranded the data-protection documents.
3. Register **Documentation Ontology** and **Agent Scratchpad** in the Config Registry table in the root `AGENTS.md`, so skills reference them by property name rather than by embedded path.

### Phase 2 — Evict non-documentation from `6-Docs/`

**2a. Relocate the agent scratchpad.** `6-Docs/agent-notes/**` is already fully git-ignored, so nothing tracked is lost; this is a pure relocation.

- Create root `.agent-scratch-pad/` with a committed `.gitkeep` and `README.md` stating the folder's purpose; ignore its contents.
- Delete `6-Docs/agent-notes/` and its `.gitignore` entries.
- Update the seven instructional references across six files: root `AGENTS.md` lines 9 and 15, `6-Docs/system/agent-governance.md:6`, `REVIEW.md:29`, `6-Docs/specs/admin-desktop-local-processor-bootstrap/design.md:141`, and `.claude/settings.local.json`.
- The "no new files or folders at repository root" rule in `AGENTS.md:15` requires an explicit carve-out naming `.agent-scratch-pad/`, or it contradicts itself.
- **Repair two references that point into ignored content** and are therefore already broken on a fresh clone: `6-Docs/DevOps/overview.md:143` links to `../agent-notes/branch-protection-update.md`, and [plans/README.md](README.md) line 52 cites the same file as operational guidance. Promote that content into `DevOps/overview.md` or drop the references. A scratchpad must never be cited as canonical.
- Per the agent-configuration synchronization rule in root `AGENTS.md`, apply the scratchpad path change across all six harness configurations (`.codex/`, `.cursor/`, `.github/`, `.opencode/`, `.kilo/`, `.claude/`).

**2b. Adopt APM properly.** Per the [APM install documentation](https://microsoft.github.io/apm/consumer/install-packages/), a consuming repository declares dependencies in a root `apm.yml`; `apm install` materializes them into a root `apm_modules/` cache that is git-ignored and rebuilt from `apm.lock.yaml`; and the harness directories APM writes into are committed. This repository currently has no root `apm.yml` and no `apm_modules/`.

- Add a root `apm.yml` declaring `dpalfery/dpalfery-agent-package` — the package currently cloned at `6-Docs/agent-notes/dpalfery-apm-package/`, remote `https://github.com/dpalfery/dpalfery-agent-package.git` — under `dependencies.apm`.
- Add `apm_modules/` to `.gitignore`. The hand-placed clone is removed by 2a.
- **The install step is deliberately blocked.** Inspection on 2026-07-21 found the upstream package to be a stale snapshot of this repository's agent roster: 11 agents against the repository's 17; four of them (`orchestrator`, `design-architect`, `task-planner`, `requirements-author`) are deliberately retired here and live in `6-Docs/archive/`; `frontend-dev` was renamed `react-dev`; and shared roles are roughly half the size of the current definitions (`dotnet-dev`: 3.6 KB upstream against 6.6 KB here). Running `apm install` would overwrite governed harness files under `.claude/`, `.codex/`, `.cursor/`, `.github/`, and `.opencode/` with stale content and resurrect the four retired roles.
- The dependency is therefore declared in `apm.yml` with an inline block explaining the hold, so the structure is in place and the hazard is documented at the point of use. `apm.lock.yaml` does not yet exist. Refreshing upstream from this repository's current agents is a separate outbound change to another repository and belongs in its own plan.
- The package's MCP declarations (`context7`, `microsoft-learn`, `playwright`, `azure`) stay out of `apm.yml`: its `azure` entry hardcodes an absolute path to a machine-local VS Code extension that must not become repository configuration. When install is eventually unblocked, use `apm install --only apm`.

**2c. Targeted documentation moves.**

| Action | Rationale |
| --- | --- |
| Create `6-Docs/MotorcycleRag.WebUI.BFF/` with `architecture.md`, `onboarding.md`, `requirements.md` | The BFF is a distinct runnable component with its own source root and `AGENTS.md`, but has no source-root `README.md` and no documentation folder — its content is folded into `MotorcycleRag.WebUI/`. |
| Move `operations/data-protection-architecture.md` → `MotorcycleRag.WebUI.BFF/data-protection.md` | Architecture belongs with the component; only operating procedure belongs in `operations/`. |
| Rename the three remaining `operations/data-protection-*.md` with a `webui-bff-` prefix | Explicit naming convention; disambiguates when other components add runbooks. |
| Add a catalog row for the BFF and link the four data-protection documents from `6-Docs/README.md` | They are currently orphans — nothing links to them. |

Update the `application_docs` array and the `components` array in `7-Deployment/scripts/validate-docs.sh` to cover the new BFF folder.

### Phase 3 — Frontmatter rollout

Apply frontmatter to the ~75 in-scope documents, ordered so the highest-value edges land first:

1. `system/` (4), `rules/` (1), `adr/` (2) — the spine.
2. Component triads for `MotorcycleRAG.API`, `MotorcycleRag.WebUI`, `MotorcycleRag.WebUI.BFF`, `MotorcycleRAG.AdminDesktop`, `MotorcycleRAG.MobileApp`, `local-processing-service`, `AzureEnvironment` (22).
3. `operations/` (4), `reference/` (8), `DevOps/` (12 — skip the five vendored skill files, which carry unrelated frontmatter of their own).
4. `plans/` and `specs/` (5).

Populate `code-refs` and `api-endpoints` by querying the index rather than guessing: `codegraph query "<symbol>" -j`, and `sqlite3 .codegraph/codegraph.db "select name from nodes where kind='route'"`. A value that does not resolve must not be written — that is precisely the drift this plan exists to prevent.

Remove the existing bold key-value headers wherever frontmatter now carries the same fact, so there is exactly one source of truth per fact.

### Phase 4 — `SkillForge.Core.Docs`

Implemented as a peer to the existing `SkillForge.Core.Agents` namespace, reusing the assets listed in the dependency table.

**Shared-infrastructure change (small, backwards-compatible):**
- Rename `Diagnostic.SkillName` to `Subject`, retaining `SkillName` as an alias property; make the `ReportRenderer` subject column header caller-supplied instead of the literal `Skill`.
- Extract the frontmatter read from `MarkdownAgentParser` into a reusable `MarkdownFrontmatterReader` in `SkillForge.Core/Parsing/`, preserving the current Markdig and YamlDotNet configuration exactly.

**New — `SkillForge.Core/Docs/`:**
- `Model/DocumentModel.cs`, `Model/DocumentSet.cs` — parsed frontmatter plus body-link edges; peers of `AgentModel` / `AgentSet`.
- `Parsing/DocumentLoader.cs` — walks `6-Docs/`, honouring the scope exclusions.
- `Validation/DocSpecValidator.cs` — schema tier, no external dependencies:
  - `SF-DOC-SPEC-001` missing or unparseable frontmatter
  - `SF-DOC-SPEC-002` `doc-type` or `status` outside the closed vocabulary
  - `SF-DOC-SPEC-003` required key missing for the declared `doc-type`
  - `SF-DOC-SPEC-004` `component` or `owner` absent from the catalog
  - `SF-DOC-SPEC-005` `source-root` does not exist on disk
  - `SF-DOC-SPEC-006` duplicate `id`, or unresolvable `decided-by` / `supersedes`
- `CodeGraph/CodeGraphResolver.cs` — reads `.codegraph/codegraph.db` directly via `Microsoft.Data.Sqlite`; no CLI shell-out.
- `Validation/DocDriftLinter.cs` — the entity-drift tier:
  - `SF-DOC-DRIFT-001` a `code-refs` value resolves to no CodeGraph node
  - `SF-DOC-DRIFT-002` an `api-endpoints` value matches no `kind='route'` node `name`
  - `SF-DOC-DRIFT-003` `source-root` has no indexed files
  - Each diagnostic names the document, the dangling reference, and the nearest surviving symbol by name similarity, so the fix is obvious. `TextVectorizer` in `SkillForge.Core/Text/` already provides the similarity primitive.
  - Grep cannot substitute for CodeGraph here: CodeGraph resolves `[Route("api/me")]` plus `[HttpGet("usage")]` into `GET /api/me/usage`, a string that appears nowhere in source.
- `Export/DocGraphExporter.cs` — Phase 5.

**CLI** — mirror the `agent` branch in `SkillForge.Cli/Program.cs`:
- `skillforge docs validate` — schema tier
- `skillforge docs drift` — entity-drift tier
- `skillforge docs graph --out <dir>` — export
- `skillforge docs catalog` — doc-type × component matrix, peer to `skillforge agent catalog`

**Tests** in `SkillForge.Tests`, peers of `AgentParserTests` / `AgentSyncLinterTests`: `DocSpecValidatorTests`, `DocDriftLinterTests` (fixture graph containing a deliberately renamed symbol), `DocGraphExporterTests`.

### Phase 5 — Graph export

`DocGraphExporter` emits `nodes.jsonl` and `edges.jsonl` into a git-ignored build directory. Document nodes come from frontmatter; code nodes are referenced by CodeGraph `id` and are **not** duplicated into the export — the exporter emits only the documentation-to-code join edges. This keeps CodeGraph the single store while still giving an external ingester a portable file.

One JSON object per line:

```json
{"type":"node","id":"doc:webui-bff/data-protection-operations","label":"Document","docType":"runbook","title":"…","path":"6-Docs/operations/webui-bff-data-protection-operations.md"}
{"type":"edge","label":"REFERENCES","from":"doc:webui-bff/data-protection-operations","to":"method:09426503497132a12f29f2b00a1380d4"}
```

### Phase 6 — CI enforcement and plan lifecycle

Extend the existing `docs-quality` job in `.github/workflows/pr-gate.yml` rather than adding a parallel one:

1. Keep `validate-docs.sh` for required-file, catalog, and link validation — it already owns those.
2. Add `skillforge docs validate` immediately after it. No index required; runs on every documentation pull request.
3. Add a `doc-graph-drift` step gated on code-or-docs changes: restore `.codegraph/` from `actions/cache` keyed on a hash of tracked source files, then `codegraph sync` on a cache hit or `codegraph index` on a miss (`npm i -g @colbymchenry/codegraph`, v1.4.1 today), then `skillforge docs drift`. Caching matters — a cold index is 1,106 files and 96 MB.
4. Upload the Phase 5 export as a build artifact.
5. Extend the `docs` path filter and add the new step to the job summary aggregation.

Plan lifecycle: register this plan in the active inventory of [plans/README.md](README.md).

---

## Files affected

**Created**
- `6-Docs/documentation-ontology.md`
- `6-Docs/MotorcycleRag.WebUI.BFF/{architecture,onboarding,requirements}.md`
- `.agent-scratch-pad/{README.md,.gitkeep}`
- `apm.yml`, `apm.lock.yaml` (repository root)
- `7-Deployment/tools/SkillForge/src/SkillForge.Core/Parsing/MarkdownFrontmatterReader.cs`
- `7-Deployment/tools/SkillForge/src/SkillForge.Core/Docs/**` — `DocumentModel`, `DocumentSet`, `DocumentLoader`, `DocSpecValidator`, `CodeGraphResolver`, `DocDriftLinter`, `DocGraphExporter`
- `7-Deployment/tools/SkillForge/src/SkillForge.Cli/Commands/Docs/**`
- `7-Deployment/tools/SkillForge/tests/SkillForge.Tests/{DocSpecValidator,DocDriftLinter,DocGraphExporter}Tests.cs`

**Modified**
- `6-Docs/documentation-standard.md` — frontmatter policy section
- `6-Docs/catalog.md` — BFF row
- `6-Docs/README.md` — link the orphaned data-protection documents
- `6-Docs/plans/README.md` — register this plan; correct the SkillForge plan status; repair the scratchpad citation
- `6-Docs/DevOps/overview.md` — replace the link into ignored content
- `AGENTS.md` — scratchpad path, root-folder carve-out, Config Registry entries
- `REVIEW.md`, `6-Docs/system/agent-governance.md`, and the six harness agent configurations — scratchpad path
- `7-Deployment/scripts/validate-docs.sh` — extend `application_docs` and `components`
- `7-Deployment/tools/SkillForge/src/SkillForge.Core/Diagnostics/Diagnostic.cs` — `Subject` rename with alias
- `7-Deployment/tools/SkillForge/src/SkillForge.Cli/Rendering/ReportRenderer.cs` — caller-supplied subject column header
- `7-Deployment/tools/SkillForge/src/SkillForge.Cli/Program.cs` — `docs` command branch
- `.github/workflows/pr-gate.yml` — SkillForge documentation steps, drift step, path filter, summary row
- `.gitignore` — remove `6-Docs/agent-notes/**`; add `.agent-scratch-pad/` contents and `apm_modules/`
- **~75 markdown files** across `6-Docs/` — frontmatter prepended, redundant bold headers removed. Representative: `6-Docs/system/architecture.md`, `6-Docs/MotorcycleRAG.API/architecture.md`, `6-Docs/operations/data-protection-operations.md`

**Deleted**
- `6-Docs/agent-notes/` — fully git-ignored, including the misplaced APM clone and its nested `.git/`

**Moved**
- `6-Docs/operations/data-protection-architecture.md` → `6-Docs/MotorcycleRag.WebUI.BFF/data-protection.md`
- `6-Docs/operations/data-protection-{operations,disaster-recovery,troubleshooting}.md` → `webui-bff-` prefixed

---

## Acceptance criteria and verification

1. **Build and unit tests** — `dotnet build 7-Deployment/tools/SkillForge/SkillForge.sln` and `dotnet test 7-Deployment/tools/SkillForge/tests/SkillForge.Tests/SkillForge.Tests.csproj` pass, including the new `Docs` test classes and the existing `Agent` tests unchanged (the `Subject` rename is backwards-compatible).
2. **Schema gate** — `skillforge docs validate` passes. Then break one document per rule (invalid `doc-type`, unknown `owner`, `architecture` document missing `component`, duplicate `id`) and confirm each emits the expected `SF-DOC-SPEC-*` code naming the file and the offending key.
3. **Drift gate** — `skillforge docs drift` passes against the current index. Then reproduce the entity-drift pitfall end to end: rename `AddBffDataProtection` in `1-Presentation/MotorcycleRag.WebUI.BFF/Configuration/Services/DataProtectionServiceConfiguration.cs`, run `codegraph sync`, re-run, and confirm `SF-DOC-DRIFT-001` names the runbook that still references the old symbol. Revert.
4. **Route join** — take a documented endpoint and confirm its `api-endpoints` value byte-matches a row from `sqlite3 .codegraph/codegraph.db "select name from nodes where kind='route'"`. Confirm a fabricated route (`GET /api/does-not-exist`) raises `SF-DOC-DRIFT-002`.
5. **Multi-hop** — from the exported graph, traverse runbook → `REFERENCES` → `DataProtectionHealthCheck` → CodeGraph `calls`/`implements` → `AzureBlobDataProtectionProbe`, and confirm the path resolves with no LLM step.
6. **Export shape** — `skillforge docs graph --out ./build/doc-graph`; assert every `edge.from` resolves to an emitted document node and every `edge.to` resolves to a CodeGraph node id or another document node. No dangling ids.
7. **Output formats** — `skillforge docs validate --format sarif` produces valid SARIF through the existing `ReportRenderer`, with no regression to `skillforge agent scan --format sarif`.
8. **Scratchpad and APM** — after `apm install`, `git status` is clean apart from `apm.yml`, `apm.lock.yaml`, and harness directories; `apm_modules/` and `.agent-scratch-pad/` contents are ignored; `grep -rn "agent-notes" .` returns no hits outside `6-Docs/archive/`; `bash 7-Deployment/scripts/validate-docs.sh` still passes.
9. **Human readability** — read three converted documents raw (`architecture`, `runbook`, `adr`) and confirm the frontmatter block is under roughly twelve lines and no fact is stated twice.
10. **CI** — open a documentation-only pull request and a code-only pull request; confirm schema validation runs on the first, drift validation on both, and that the cache-hit path uses `codegraph sync` rather than a full reindex.

---

## References

- [APM — install packages](https://microsoft.github.io/apm/consumer/install-packages/)
- [microsoft/apm](https://github.com/microsoft/apm)
- [SkillForge reference](../reference/skillforge.md) — canonical documentation for the toolkit this plan extends
