---
id: plans/2026-07-21-kyber-weave-phase-1
title: 2026-07-21 Kyber-Weave Phase 1 — absorb SkillForge, unify the engine, ship the MCP server
doc-type: plan
status: draft
component: MotorcycleRAG system
owner: Maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---

# 2026-07-21 Kyber-Weave Phase 1 — absorb SkillForge, unify the engine, ship the MCP server

**Status:** Draft
**Date:** 2026-07-21
**Goal:** Name the agent-and-documentation governance framework Kyber-Weave, absorb the vendored SkillForge as two of its features, unify the shared engine and rule-code namespace, and ship a stdio MCP server so agents can retrieve documentation and its code joins without falling back to grep.

---

## Context

Three bodies of work have converged into one product without a name: skill governance (the vendored SkillForge), agent governance across six harnesses, and documentation governance (commit `0703f7fc`). They already share a diagnostic model, a renderer set, a frontmatter reader and a lexical similarity engine. **Kyber-Weave** is the name for the whole.

The unifying idea: **every artifact that shapes agent behaviour — skills, agent definitions, and documentation — is a supply-chain artifact and gets the same treatment.** Parsed, validated against a closed spec, checked for drift against its source of truth, security-scanned, and made retrievable. Each class differs only in what its source of truth *is*: documentation answers to the code graph, an agent manifest to its sibling harness copies, a skill to the open format spec.

**Phase 1 makes all of it work in place, under one name, with the MCP server.** Genericity work — config-driven ontology, pluggable code-graph backend — is deferred to a later extraction phase. Phase 1's job is to get the boundaries and naming right so extraction is later a move, not a rewrite.

### Decisions taken

| Decision | Choice |
| --- | --- |
| Name | **Kyber-Weave**. Namespace `KyberWeave.*`, CLI `kyber-weave`, MCP server `kyber-weave`. |
| SkillForge | **Absorb and fork.** Kyber-Weave owns the code. MIT attribution retained in `NOTICE`; upstream tracking and the `bonaniibm/SkillForge` PR intent are dropped. |
| Genericity | Deferred to extraction. |
| Location | `7-Deployment/tools/KyberWeave/`; `7-Deployment/tools/SkillForge/` ceases to exist. |

### Naming hazard, to be recorded in the overview

"Kyber" collides with CRYSTALS-Kyber / ML-KEM, the NIST post-quantum KEM standardized as FIPS 203 and shipped in Apple iMessage. In a repository running Snyk, Trivy, CodeQL and Semgrep this will collide in search and scan output. "Weave" is safely non-cryptographic — unlike Lattice, Module, Ring or Key, which read as the algorithm itself.

### Two defects in shipped work, fixed here

1. **`ReportRenderer` was never modified.** The preceding plan listed it as changed; commit `0703f7fc` does not touch it. `docs validate` and `docs drift` render a column headed `Skill` containing document ids, and emit `"skill": "<doc id>"` in JSON.
2. **`MarkdownFrontmatterReader` is half-extracted.** `DocumentLoader` uses it; `MarkdownAgentParser` still carries a duplicate copy of the same Markdig pipeline and YamlDotNet deserializer.

---

## Feature map

| # | Feature | Capabilities | Code today |
| --- | --- | --- | --- |
| 1 | **Skill governance** | Parse `SKILL.md`; spec validation; description scoring and collision detection; trust-surface scanning (injection, secrets, risky scripts); routing simulation and eval sets; inventory; Copilot Studio pack; scaffold | `SkillForge.Core/{Model,Parsing,Validation,Security,Routing}` |
| 2 | **Agent governance** | Parse 6 harnesses in 2 formats; role × harness matrix; cross-harness parity via role satisfaction; instruction drift by cosine similarity; manifest validation; prompt security scanning; dispatch evaluation | `SkillForge.Core/Agents/**` |
| 3 | **Documentation governance** | Ontology of record; frontmatter schema validation; code-entity drift against CodeGraph; graph export joined to CodeGraph ids; coverage by component | `SkillForge.Core/Docs/**` |
| 4 | **Retrieval (MCP)** | `docs_explore`, `docs_for_symbol`; agent and skill explore to follow on the same host | *new* |
| 5 | **Shared engine** | One diagnostic model and rule-code namespace; table/JSON/SARIF/markdown renderers; frontmatter reader; offline lexical similarity; CodeGraph resolver | `Diagnostics`, `Text`, `Parsing`, `Docs/CodeGraph` |
| 6 | **CI enforcement** | `skill-gate` (SARIF to code scanning), `docs-quality`, `doc-graph-drift`, `validate-docs.sh` | `pr-gate.yml`, `nightly.yml`, `7-Deployment/scripts/` |
| 7 | **Governed assets** | 17–18 agent roles across 6 harnesses; 22 skills; 71 in-scope documents | `.claude/`, `.codex/`, `.cursor/`, `.github/`, `.opencode/`, `.kilo/`, `.agents/skills/`, `6-Docs/` |

Kyber-Weave is a framework identity plus tooling, not a folder containing everything. Feature 7's assets live at paths their harnesses mandate and cannot move.

## Architecture

```text
7-Deployment/tools/KyberWeave/
  KyberWeave.sln
  LICENSE                        MIT, retained
  NOTICE                         attribution for the absorbed work
  README.md
  src/KyberWeave.Core/
    Diagnostics/                 Diagnostic, DiagnosticReport, Severity
    Text/                        TextVectorizer
    Parsing/                     MarkdownFrontmatterReader
    CodeGraph/                   CodeGraphResolver  (promoted out of Docs)
    Skills/                      feature 1
    Agents/                      feature 2
    Docs/                        feature 3
  src/KyberWeave.Cli/            kyber-weave skill|agent|docs
  src/KyberWeave.Mcp/            stdio MCP server
  tests/KyberWeave.Tests/
```

`CodeGraphResolver` is promoted out of `Docs/` because it is engine, not a docs concern; agent and skill governance will want symbol resolution too.

Tests stay inside the tool tree, following the existing precedent for self-contained tools rather than the `5-Test` layer convention that governs application code.

## CLI surface

Three symmetric branches. The current top-level skill verbs become a `skill` branch, which is what makes three asset classes read consistently.

| Before | After |
| --- | --- |
| `skillforge validate\|lint\|scan\|route\|catalog\|pack\|new` | `kyber-weave skill …` |
| `skillforge agent validate\|sync-check\|catalog` | `kyber-weave agent …` |
| `skillforge docs validate\|drift\|graph\|catalog` | `kyber-weave docs …` |

This is a breaking CLI change; `7-Deployment/scripts/skillforge-validate.sh` and both workflows move with it. Wiring the four unwired agent subcommands (`scan`, `route`, `lint`, `new`, whose Core classes already exist) is **not** in this plan and remains a known gap.

## Rule-code namespace

`SF-*` becomes `KW-*`, segmented by feature.

| Before | After |
| --- | --- |
| `SF-SPEC-*`, `SF-LINT-*`, `SF-SEC-*`, `SF-PARSE-*` | `KW-SKILL-SPEC-*`, `KW-SKILL-LINT-*`, `KW-SKILL-SEC-*`, `KW-PARSE-*` |
| `SF-AGENT-SPEC-*`, `SF-AGENT-SYNC-*`, `SF-AGENT-SEC-*` | `KW-AGENT-*` |
| `SF-DOC-SPEC-*`, `SF-DOC-DRIFT-*` | `KW-DOC-*` |

> **Operational consequence, accepted deliberately.** SARIF results upload under categories `skillforge-agents` and `skillforge-claude`. Renaming the categories makes GitHub Code Scanning treat them as new categories: existing open alerts under the old names will not auto-close and must be dismissed once. The rule ids change regardless, so the alerts are new identities either way — accept the one-time re-baseline.

## MCP server

`KyberWeave.Mcp` is a separate executable, not a `kyber-weave mcp` subcommand: stdio JSON-RPC owns stdout and `KyberWeave.Cli` is built on Spectre.Console, which writes there. A separate entry point makes stream corruption structurally impossible rather than a matter of discipline.

Host setup per `ModelContextProtocol` 1.4.1, with logging pinned to stderr:

```csharp
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
```

**Tools shipped in Phase 1:**

- `docs_explore(query, maxDocs = 5)` — free text, or symbol, route, component or doc-id names. Returns ranked documents with frontmatter identity, the most relevant `##` section body rather than the whole file, and that document's resolved code joins as `symbol → file:line`.
- `docs_for_symbol(symbol)` — the precise reverse lookup. `DataProtectionHealthCheck` returns the 5 documents whose `code-refs` formally claim it, where grep returns 7 with 2 false positives. This is the operation that justifies a tool over grep.

The host is left open for `agent_explore` and `skill_explore` as an immediate follow-on; the loader and index are written per asset class behind a common shape rather than docs-specific plumbing.

**Supporting changes:**

- `DocumentModel` gains `Body` and `Sections` split on `##`; it currently retains `BodyLinks` only. Roughly 600 KB across 71 documents.
- Ranking reuses `TextVectorizer.Cosine`, weighting exact frontmatter matches (`id`, `component`, `code-refs`, `api-endpoints`) above body similarity.
- Long-lived process: load once, reload when any in-scope document's mtime or `.codegraph/codegraph.db`'s mtime changes. The codegraph daemon rewrites that file continuously, so this is required rather than optional.
- Output caps mirroring `codegraph_explore`'s `maxFiles` discipline.

**Registration:** `.mcp.json` gains a `kyber-weave` stdio entry alongside `codegraph`, launched via `dotnet run --project` — always current, no install step, about 1.3 s once per session. Tools surface as `mcp__kyber-weave__docs_explore`.

## Migration

1. `git mv 7-Deployment/tools/SkillForge 7-Deployment/tools/KyberWeave`, then rename projects, assemblies, root namespaces, and `ToolCommandName` to `kyber-weave`.
2. Restructure `Program.cs` into the three branches; move skill verbs under `skill`.
3. Reorganise `Core` into `Skills/`, `Agents/`, `Docs/`, promoting `CodeGraph/` and keeping `Diagnostics/`, `Text/`, `Parsing/` as engine.
4. Rename rule codes `SF-*` to `KW-*`.
5. Fix defect 1: make the renderer's subject column caller-supplied so each branch labels it correctly (`Skill`, `Agent`, `Document`) and emits `subject` in JSON. Keep `Diagnostic.Subject`; the `SkillName` alias can be deleted outright now that there is no upstream to stay compatible with.
6. Fix defect 2: point `MarkdownAgentParser` at the shared `MarkdownFrontmatterReader` and delete its duplicate pipeline.
7. Add `KyberWeave.Mcp`; register in `.mcp.json`.
8. Add `NOTICE` recording the MIT-licensed origin; keep `LICENSE`; strip upstream-provenance and PR language from the reference document.
9. Update the 18 referencing files outside the tool tree.

## Files affected

**Created** — `KyberWeave.Mcp/{Program.cs,DocsTools.cs}`, `KyberWeave.Core/Docs/Search/DocumentIndex.cs`, `NOTICE`, `6-Docs/reference/kyber-weave.md`

**Moved or renamed** — the entire `7-Deployment/tools/SkillForge` tree; `7-Deployment/scripts/skillforge-validate.sh` to `kyber-weave-validate.sh`; `6-Docs/reference/skillforge.md` to `kyber-weave.md`

**Modified**

- `.mcp.json` — `kyber-weave` server entry
- `.github/workflows/pr-gate.yml` — `skill-gate` project path and SARIF categories; `docs-quality` and `doc-graph-drift` command names; docs path filters
- `.github/workflows/nightly.yml` — skill gate job
- `7-Deployment/scripts/validate-docs.sh` — required-file paths
- `6-Docs/catalog.md` — SkillForge row becomes Kyber-Weave
- `AGENTS.md` — Config Registry entry; Kyber-Weave named as the governing framework
- `6-Docs/documentation-ontology.md`, `6-Docs/documentation-standard.md` — command names and `KW-DOC-*` rule codes
- `README.md`, `REVIEW.md`, `7-Deployment/README.md`, `6-Docs/DevOps/overview.md`, `6-Docs/reference/README.md`, `6-Docs/plans/README.md`, `.agents/skills/create-pull-request/SKILL.md`

## Acceptance criteria and verification

1. **Build and tests** — `dotnet build KyberWeave.sln`; `dotnet test` reproduces all 49 tests green (28 skill and agent, 21 docs) after the namespace and rule-code rename.
2. **SkillForge is gone** — `test ! -d 7-Deployment/tools/SkillForge`, and a case-insensitive `skillforge` search outside `6-Docs/archive/` and the scratchpad returns nothing.
3. **Parity across all three features** — `kyber-weave skill validate|lint|scan .agents/skills` matches current results; `kyber-weave agent sync-check` still resolves `conductor` as satisfied by skill mapping for Claude and Cursor and native for Kilo and OpenCode; `kyber-weave docs validate|drift` return 0 findings across 71 documents; `docs graph` returns 89 nodes, 339 edges, 0 dangling.
4. **Defect 1 fixed** — `docs validate --format table` shows a `Document` column and `skill validate` shows `Skill`; JSON emits `subject`.
5. **Defect 2 fixed** — `MarkdownAgentParser` contains no Markdig pipeline of its own and the agent parser tests still pass.
6. **Drift still catches a rename** — rename `AddBffDataProtection`, run `codegraph sync`, expect `KW-DOC-DRIFT-001` on all three runbooks, then revert.
7. **MCP handshake** — pipe `initialize` then `tools/list` into `KyberWeave.Mcp` over stdio; both tools are advertised with descriptions. Assert the first byte on stdout is `{`, proving nothing but JSON-RPC reaches the stream.
8. **`docs_for_symbol` beats grep** — `DataProtectionHealthCheck` returns exactly 5 documents, not grep's 7.
9. **`docs_explore` returns sections** — a key-rotation query returns the relevant `##` section of the disaster-recovery runbook, not all 380 lines.
10. **Staleness** — with the server running, edit frontmatter and confirm the next call reflects it without a restart; likewise after `codegraph sync`.
11. **Live in session** — reconnect the MCP server and call `mcp__kyber-weave__docs_explore`.
12. **CI** — a documentation-only and a code-only pull request each pass; the one-time SARIF category re-baseline is understood and old alerts dismissed.

## Out of scope

Config-driven ontology (a `kyber-weave.yml` replacing the `DocType` enum, required-key matrix, exclusion lists and catalog column positions); pluggable code-graph backend; `agent_explore` and `skill_explore` MCP tools; wiring the four unwired agent subcommands; moving the framework to its own repository; publishing as a tool or NuGet package.
