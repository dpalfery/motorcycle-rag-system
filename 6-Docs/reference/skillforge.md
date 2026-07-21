---
id: reference/skillforge
title: SkillForge Reference
doc-type: reference
status: current
component: SkillForge
owner: Developer-experience maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# SkillForge Reference

SkillForge is a vendored .NET CLI/library used to validate, lint, scan, and routing-test agent skills, harness agent definitions, and repository documentation. The local source is `7-Deployment/tools/SkillForge`.

## Upstream provenance

- Upstream project: [bonaniibm/SkillForge](https://github.com/bonaniibm/SkillForge)
- Upstream license: MIT; the vendored copy retains its own [LICENSE](../../7-Deployment/tools/SkillForge/LICENSE).
- Local provenance status: the checked-in copy does not retain a source commit. Before refreshing it, verify the upstream revision and record its commit SHA in the refresh pull request and the local README.

## Use

Treat skills and coding harness agent definitions (`.codex`, `.cursor`, `.claude`, `.github`, `.opencode`, `.kilo`) as supply-chain artifacts. Run SkillForge validation, routing lint, and security scanning from the vendored source according to the repository workflow. Its diagnostics assist review but do not replace human security review.

## Commands

- **Skills**: `skillforge validate`, `skillforge lint`, `skillforge scan`, `skillforge route`, `skillforge catalog`, `skillforge new`.
- **Agents**: `skillforge agent validate`, `skillforge agent sync-check`, `skillforge agent catalog`.
- **Documentation**: `skillforge docs validate`, `skillforge docs drift`, `skillforge docs graph`, `skillforge docs catalog`.

All three surfaces share one diagnostic model, rule-code convention, and renderer, so `--format table | json | sarif | markdown` behaves identically across them.

## Documentation governance

The `docs` branch enforces the [documentation ontology](../documentation-ontology.md) in two tiers.

| Command | Tier | Rule codes | Needs the CodeGraph index |
| --- | --- | --- | --- |
| `skillforge docs validate` | Frontmatter schema | `SF-DOC-SPEC-001`–`006` | No |
| `skillforge docs drift` | Code-entity resolution | `SF-DOC-DRIFT-001`–`003` | Yes |
| `skillforge docs graph --out <dir>` | Export | — | Yes |
| `skillforge docs catalog` | Coverage view | — | No |

Both tiers run in the `pr-gate.yml` pipeline: the schema tier in `docs-quality` on any documentation change, and the drift tier in `doc-graph-drift` whenever code **or** documentation changes — a rename on the code side breaks a documentation reference just as surely as editing the reference does.

The drift tier reads `.codegraph/codegraph.db` through one batched `sqlite3` invocation rather than a SQLite NuGet package: `Microsoft.Data.Sqlite`'s native dependency `SQLitePCLRaw.lib.e_sqlite3` carries advisory [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) at every published version with no patched release, and this repository runs blocking dependency scanning. The `sqlite3` CLI must therefore be on `PATH`; it is present by default on macOS and on GitHub's Ubuntu runners.

`skillforge docs graph` emits `nodes.jsonl` and `edges.jsonl`. Code nodes are referenced by CodeGraph id and are not duplicated into the export, so CodeGraph remains the single store for code structure.
