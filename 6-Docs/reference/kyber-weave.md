---
id: reference/kyber-weave
title: Kyber-Weave Reference
doc-type: reference
status: current
component: Kyber-Weave
owner: Developer-experience maintainers
last-reviewed: 2026-07-28
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Kyber-Weave Reference

Kyber-Weave is this repository's agent-and-documentation governance framework: a cross-ecosystem CLI and stdio MCP server. **Product source and releases live in the external repository** [dpalfery/kyber-weave](https://github.com/dpalfery/kyber-weave). MotorcycleRAG consumes installed `kyber-weave` / `kyber-weave-mcp` binaries from PATH and supplies host-only policy in [`.kyber-weave/kyber-weave.yml`](../../.kyber-weave/kyber-weave.yml).

The organising idea is that **every artifact that shapes agent behaviour — skills, agent definitions, and documentation — is a supply-chain artifact and gets the same treatment.** Each is parsed, validated against a closed spec, checked for drift against a source of truth, security-scanned, and made retrievable. The classes differ only in what their source of truth *is*: documentation answers to the code graph, an agent manifest to its sibling harness copies, a skill to the Agent Skills open format spec.

## Install

| Channel | How | Status |
| --- | --- | --- |
| **GitHub Releases** | Download RID archives from [releases](https://github.com/dpalfery/kyber-weave/releases) (MotorcycleRAG CI pins `0.1.1` via `.github/actions/install-kyber-weave`; SHA-256 verified) | Verified — primary host/CI channel |
| **npm** | `npm i -g @dpalfery/kyber-weave@<version>` (wrapper downloads Release assets; SHA-256 verified) | Published when the product repo `release.yml` job has `NPM_TOKEN` configured |
| **Homebrew** | `brew install dpalfery/kyber-weave/kyber-weave` | Published when the product repo `release.yml` job has `HOMEBREW_TAP_TOKEN` configured |

Self-contained binaries — no .NET runtime required for end users. **nuget.org is forbidden.** Optional advanced channel: GitHub Packages `dotnet tool` (product repo only).

Host MCP registration (`.mcp.json`) launches `kyber-weave-mcp` from PATH with `--repo-root .`.

## Naming hazard

"Kyber" collides with CRYSTALS-Kyber / ML-KEM, the NIST post-quantum KEM standardised as FIPS 203 and shipped in Apple iMessage. In a repository running Trivy, CodeQL and Semgrep, searching or scanning for "kyber" will surface both this tool and cryptographic material. "Weave" is deliberately non-cryptographic — unlike Lattice, Module, Ring or Key, which would read as the algorithm itself.

## Provenance

The skill-governance feature was absorbed from **[SkillForge](https://github.com/bonaniibm/SkillForge)** ([bonaniibm/SkillForge](https://github.com/bonaniibm/SkillForge)), an MIT-licensed project by the SkillForge contributors. Kyber-Weave now owns that code in [dpalfery/kyber-weave](https://github.com/dpalfery/kyber-weave): there is no upstream to track, refresh from, or contribute back to, and no ongoing sync with the originating repository. The MIT licence and SkillForge attribution are retained in that product's [LICENSE](https://github.com/dpalfery/kyber-weave/blob/main/LICENSE) and [NOTICE](https://github.com/dpalfery/kyber-weave/blob/main/NOTICE).

## Use

Treat skills and coding harness agent definitions (`.codex`, `.cursor`, `.claude`, `.github`, `.opencode`, `.kilo`) as supply-chain artifacts. Run Kyber-Weave validation, routing lint, and security scanning according to the repository workflow. Its diagnostics assist review but do not replace human security review.

## Security scanning

`skill scan` and `agent scan` share one instruction-surface scanner (`InstructionSurfaceScanner`): prompt-injection heuristics, hidden HTML comments, base64 blobs, hardcoded-secret patterns, and provenance infos (author / version / license). Skills emit `KW-SKILL-SEC-*`; agents emit the parallel `KW-AGENT-SEC-*` codes. Skill scans also cover bundled scripts under `scripts/`.

These are regex heuristics — necessary but not sufficient. CI also runs an advisory [NVIDIA SkillSpector](https://github.com/NVIDIA/SkillSpector) job (`skillspector-gate` in `pr-gate.yml` and `nightly.yml`): static analysis only (`--no-llm`), SARIF category `skillspector-skills`, `continue-on-error: true`. Pin and install details live in the workflow files.

## Commands

Three symmetric branches, one per artifact class.

- **Skills**: `kyber-weave skill validate|lint|scan|route|catalog|pack|new`.
- **Agents**: `kyber-weave agent validate|scan|sync-check|catalog`.
- **Documentation**: `kyber-weave docs validate|drift|graph|catalog`.

All three share one diagnostic model, rule-code convention, and renderer, so `--format table | json | sarif | markdown` behaves identically across them. The renderer's subject column is caller-supplied, so each branch labels it correctly — `Skill`, `Agent`, `Document` — and JSON emits `subject`.

Agent commands take a **project root**. Harness trees are discovered by convention as every `.harnessname/agents` directory under that root (`.codex`, `.cursor`, `.claude`, `.github`, `.opencode`, `.kilo`, and any other `.*` folder that has an `agents` child). Optional `--harness <name>` filters to one harness for parallel CI. `agent sync-check` always compares across all discovered harnesses.

`agent route`, `agent lint` and `agent new` have Core classes but no CLI verb. This is a known gap.

## Rule codes

Rule ids are `KW-*`, segmented by feature.

| Feature | Prefixes |
| --- | --- |
| Skill governance | `KW-SKILL-SPEC-*`, `KW-SKILL-LINT-*`, `KW-SKILL-SEC-*` |
| Agent governance | `KW-AGENT-SPEC-*`, `KW-AGENT-SYNC-*`, `KW-AGENT-LINT-*`, `KW-AGENT-SEC-*` |
| Documentation governance | `KW-DOC-SPEC-*`, `KW-DOC-DRIFT-*` |
| Parsing | `KW-PARSE-*` |

These replaced the earlier `SF-*` codes. Because the ids and the SARIF upload categories both changed, GitHub Code Scanning treats the results as new identities: alerts still open under the old `skillforge-*` categories will not auto-close and must be dismissed once.

## Documentation governance

The `docs` branch enforces the [documentation ontology](../documentation-ontology.md) in two tiers.

| Command | Tier | Rule codes | Needs the CodeGraph index |
| --- | --- | --- | --- |
| `kyber-weave docs validate` | Frontmatter schema | `KW-DOC-SPEC-001`–`006` | No |
| `kyber-weave docs drift` | Code-entity resolution | `KW-DOC-DRIFT-001`–`003` | Yes |
| `kyber-weave docs graph --out <dir>` | Export | — | Yes |
| `kyber-weave docs catalog` | Coverage view | — | No |

Both tiers run in the `pr-gate.yml` pipeline: the schema tier in `docs-quality` on any documentation change, and the drift tier in `doc-graph-drift` whenever code **or** documentation changes — a rename on the code side breaks a documentation reference just as surely as editing the reference does.

The drift tier reads `.codegraph/codegraph.db` through one batched `sqlite3` invocation rather than a SQLite NuGet package: `Microsoft.Data.Sqlite`'s native dependency `SQLitePCLRaw.lib.e_sqlite3` carries advisory [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) at every published version with no patched release, and this repository runs blocking dependency scanning. The `sqlite3` CLI must therefore be on `PATH`; it is present by default on macOS and on GitHub's Ubuntu runners.

That invocation deliberately does **not** pass `-readonly`. The CodeGraph daemon leaves the index in WAL journal mode, and opening a WAL database requires a shared-memory (`-shm`) file that `-readonly` forbids creating — so `-readonly` fails outright with "unable to open database file" whenever no other connection already holds the index open. Only `SELECT` statements are ever issued.

`kyber-weave docs graph` emits `nodes.jsonl` and `edges.jsonl`. Code nodes are referenced by CodeGraph id and are not duplicated into the export, so CodeGraph remains the single store for code structure. Edges labelled `REFERENCES`, `EXPOSES` and `DESCRIBES` therefore point at ids and paths that the export does not itself carry; that is by design, not a dangling reference.

## The MCP server

`kyber-weave-mcp` is a stdio MCP server registered in `.mcp.json` as `kyber-weave` and launched from PATH (install via Releases / npm / Homebrew). Its tools surface to agents as `mcp__kyber-weave__docs_explore` and `mcp__kyber-weave__docs_for_symbol`.

It is a separate executable rather than a `kyber-weave mcp` subcommand: stdio JSON-RPC owns stdout and the CLI is built on Spectre.Console, which writes there. A separate entry point makes stream corruption structurally impossible instead of a matter of discipline. All logging is pinned to stderr.

| Tool | What it returns |
| --- | --- |
| `docs_explore(query, maxDocs = 5, charBudget = 12000)` | Ranked documents for free text, or a symbol, route, component or doc-id name. Each carries its frontmatter identity, as much of its prose as the budget allows, and that document's resolved code joins. |
| `docs_for_symbol(symbol)` | The documents whose `code-refs` frontmatter formally claims a symbol. |

`docs_for_symbol` is the operation that justifies a tool over grep. A `code-refs` entry is a *claim of ownership*; a prose mention is not. Querying `DataProtectionHealthCheck` returns the five documents that claim it, where a case-insensitive grep across `6-Docs/` returns eight files.

Ranking weights exact frontmatter matches — `id`, `component`, `code-refs`, `api-endpoints` — above title and body similarity. Everything is computed offline; no API key is required.

Body relevance is **Okapi BM25** over corpus-wide term rarity, not plain term frequency. Term frequency alone ranks badly on a corpus entirely about one system: "user", "session" and "api" appear nearly everywhere, so under raw cosine they counted as much as a decisive term and the documents densest in shared vocabulary won regardless of the question. Asked "why does the user keep getting logged out", the tool returned three requirements documents and never surfaced the troubleshooting runbook that answers it almost verbatim.

Three corrections sit on top of BM25, each because rarity alone still misjudges something:

- **Ubiquitous terms are dropped outright.** A term in more than half the corpus is treated as carrying no information, so a question made only of such words scores zero rather than accumulating a middling score everywhere.
- **Question scaffolding is dropped by name.** Formal documentation rarely writes "why" or "getting", so rarity rates them as *highly* discriminating — "getting" occurs in one document out of seventy-seven. Treating them as evidence made ordinary questions unanswerable. This is a property of questions, not of the corpus, so it is a fixed list.
- **Scores scale by how much of the question is answered**, weighted by rarity rather than counted by word. Counting words punishes the way people actually ask; weighting by rarity punishes the right thing — "best hiking trails in patagonia" matched "best" and missed three terms the corpus has never seen.

Document **authority** multiplies the result. Term statistics measure wordiness, not standing: asked about frontmatter keys, BM25 correctly preferred the plan that says "frontmatter" twenty-five times over the standard that says it seven. Plans and specs are records of intent — the repository archives them once closed and never treats the archive as current guidance — so they are discounted, as are superseded and draft documents. A demoted document still wins when named outright, because an exact id match scores far above the discount.

Three further rules exist because the naive versions failed against the real corpus:

- **Partial identity matching.** A natural-language question never equals a document's `id` verbatim, so exact-equality scoring alone left free-text queries decided entirely by body similarity — and a generic doc-type word outvoted the subject the user named. Document ids are structured slugs (`webui/architecture`, `api/architecture`), which makes them the closest thing the corpus has to a controlled vocabulary, so the fraction of an id's own tokens that a query mentions is scored just below an exact match.
- **Fused adjacent tokens.** People write "WebUI"; the catalog writes "Web UI". Document text and identities are therefore indexed with each adjacent token pair also fused into one term, at half weight — a bridge for compound names, not evidence in its own right.
- **Length-damped section selection.** Raw cosine similarity prefers the shortest section sharing any term with the query, so a heading-only stub beat the paragraph holding the answer and the caller had to read the whole file anyway. Section scores are damped by length, and the title-only run before a document's first `##` is not emitted as a retrievable section at all.

### How much prose comes back

`charBudget` is a total across all returned documents, divided between them. Lowering `maxDocs` therefore *deepens* each result rather than merely shortening the list, which is the whole knob: `maxDocs=1` on a median document returns the entire file.

The budget exists because returning exactly one section — the original design — was calibrated wrongly for this corpus. Measured over the 67 documents that have `##` sections:

| | |
| --- | --- |
| Median section | ~640 characters |
| Median document body | ~5,100 characters |
| Share of a document in its top-1 section | 34% |
| Share in its top-3 sections | 70% |

So one section saved a few hundred characters and routinely forced the caller to read the whole file — retrieval as a net loss. Sections are *chosen* by relevance but *emitted* in document order, because prose written to be read in sequence is confusing when shuffled.

Whatever does not fit is named rather than silently dropped, and the two reasons are distinguished, because only one of them is worth a retry:

- `[omitted for space, ask again with a larger charBudget: …]` — the budget bit; more budget returns more.
- `[also in this document, not matching this query: …]` — dropped for irrelevance; more budget changes nothing, but the caller now knows what else the document covers.
- `[complete document]` — nothing was withheld.

The budget governs prose only; frontmatter identity and code joins are added on top. A single section that alone exceeds the budget is still returned, truncated at 6,000 characters — returning nothing but a path would make the tool a directory listing, which is precisely what sends callers back to reading files.

### How a code join picks among same-named symbols

Bare symbol names collide freely in this repository: `HostHeaderValidationMiddleware`, `CorsServiceConfiguration`, `WebApplicationExtensions` and `TelemetryServiceConfiguration` each exist in both the API and the BFF, and `AuthProvider` is simultaneously a React context provider in TypeScript and a C# claims property. Taking the index's first match joined documents to symbols in projects they do not describe.

A document already declares which subtree it is about, so resolution is scoped to its `source-root` first, preferring a declaration (class, interface, enum) over an incidental member of the same name. Repo-wide resolution is the fallback, and it is **labelled** in the output rather than hidden:

- `(outside this document's source-root)` — the name resolved nowhere beneath the component the document describes. Weak evidence; verify before acting.
- `(+N other same-named)` — N further symbols share the bare name. The join is a best guess.

Note that `docs drift` still resolves by bare name repo-wide, so a `code-refs` entry that names a symbol existing *somewhere* in the repository passes the drift gate even when nothing of that name exists under the document's own `source-root`. Tightening that would change which pull requests fail and is deliberately not done here.

### Misses are reported as misses

A document must clear a relevance floor to be returned at all. Without one, every query returned exactly `maxDocs` documents and a miss was indistinguishable from a hit — "how do I make a sandwich" came back with three confident results. For a tool the repository instructions make mandatory *before* grep, that is the most consequential possible failure: the caller has no signal to fall back and answers from whatever happened to be nearest.

When nothing clears the floor the response says so explicitly and names the fallbacks. The header on a successful call reports the relevance range, so a weak match is visibly weak rather than presented with the same confidence as a strong one.

Retrieval quality is pinned by a regression suite in the product repository's `KyberWeave.Tests` that runs against the **real** MotorcycleRAG corpus when that tree is present: plainly-worded questions paired with the document that answers them, asserted as "expected document in the top three", plus unanswerable questions asserted to return nothing. A synthetic fixture cannot catch the failure that matters, because the failure is a property of a real body of documents all about one system. The suite skips rather than fails when it cannot find the host repository.

### Staleness

The index is loaded once and rebuilt when its inputs change. The two inputs are tracked **separately**, because they change at wildly different rates: the CodeGraph daemon rewrites its database continuously while an agent session is active, whereas documentation is edited by hand. A documentation change rebuilds the corpus — the expensive half, which re-reads and re-vectorises every file. A code-graph change rebuilds only the joins. Folding both into one fingerprint meant a background daemon write forced the expensive rebuild to refresh the cheap half.

Reloading at all is required rather than optional: an index built only at startup would begin answering with stale joins within minutes of a rename.

`agent_explore` and `skill_explore` are the intended follow-on; the loader and index are written per artifact class behind a common shape rather than docs-specific plumbing.

### Why a routing rule, and not just tool descriptions

Shipping a retrieval tool does not make agents reach for it. MCP tool selection is model discretion driven by the tool's own description, which is a weak signal against the habit of grepping — so the repository root [AGENTS.md](../../AGENTS.md) carries a non-negotiable rule directing agents to `docs_explore` before reading files under `6-Docs/`, and to `docs_for_symbol` before renaming or changing the contract of a code symbol. This mirrors the existing "CodeGraph first" rule, which is the mechanism that demonstrably works in this repository.

The rule names the fallback explicitly because neither tool has a CLI equivalent: when the MCP server is not connected, agents fall back to the documentation index and say so. A stronger option — a `UserPromptSubmit` hook that pre-injects `docs_explore` results the way the CodeGraph hook does — was considered and not adopted, because it adds latency and tokens to every prompt regardless of whether the prompt concerns documentation.

## Host overrides

This repository's [`.kyber-weave/kyber-weave.yml`](../../.kyber-weave/kyber-weave.yml) supplies MotorcycleRAG-specific ontology and harness policy (docs root `6-Docs`, catalog column mapping, vendored-file exclusions, conductor→skill satisfaction). Product defaults ship without those host mappings.

## Deferred

`agent_explore` and `skill_explore`; wiring the remaining unwired agent subcommands (`route`, `lint`, `new`). Ontology config and the CodeGraph adapter shipped with the extracted product.
