# Kyber-Weave

**Governance for every artifact that shapes agent behaviour.** Skills, agent definitions, and documentation are all supply-chain artifacts, and Kyber-Weave gives all three the same treatment: parsed, validated against a closed spec, checked for drift against a source of truth, security-scanned, and made retrievable.

Each artifact class differs only in what its source of truth *is*:

| Artifact class | Source of truth it answers to |
|---|---|
| Documentation | the CodeGraph index — a documented symbol must still exist |
| Agent definitions | its sibling copies across the six supported harnesses |
| Skills | the Agent Skills open format spec |

> **Naming note.** "Kyber" collides with CRYSTALS-Kyber / ML-KEM, the NIST post-quantum KEM standardised as FIPS 203. In a repository running Snyk, Trivy, CodeQL and Semgrep, searches and scan output for "kyber" will surface both. "Weave" is safely non-cryptographic — unlike Lattice, Module, Ring or Key, which read as the algorithm itself.

---

## What it does

Three symmetric CLI branches, one per artifact class.

| Command | What it answers | Gate |
|---|---|---|
| `skill validate` | Is this a spec-conformant skill? (name rules, name↔folder match, description limits, broken references, angle-bracket safety) | fails on **error** |
| `skill lint` | Will the orchestrator route to it correctly, and is it context-efficient? (description routing score, progressive-disclosure budget, **collision & overlap detection**) | fails on **error** (name collision) |
| `skill scan` | Can I trust this skill? (prompt-injection, hidden-comment instructions, encoded payloads, secrets, risky scripts, provenance) | fails on **critical** (configurable) |
| `skill route` | **Which skill fires for this prompt?** Single-prompt simulation, or an **eval set** as a regression test (incl. negative "should fire nothing" cases) | fails below **--min-accuracy** |
| `skill catalog` | What skills exist across my tree, with version/owner/score? | — |
| `skill pack` | Bundle a skill into a Copilot Studio–compatible `.zip` | — |
| `skill new` | Scaffold a spec-correct skill from a template (sop / runbook / reference / checklist) | — |
| `agent validate` | Are coding harness agent manifests spec-conformant? | fails on **error** |
| `agent sync-check` | Are agent roles synchronized across all 6 harness folders with no instruction drift? | fails on **error** |
| `agent catalog` | Displays the role × harness governance parity matrix. | — |
| `docs validate` | Does documentation frontmatter conform to the ontology schema? | fails on **error** |
| `docs drift` | Do documented code references still resolve against the CodeGraph index? | fails on **error** |
| `docs graph` | Export the documentation graph as `nodes.jsonl` / `edges.jsonl`, joined to CodeGraph ids. | — |
| `docs catalog` | Doc-type coverage by component. | — |

---

## Run it

```bash
# from source
dotnet run --project src/KyberWeave.Cli -- <branch> <command> [args]

# spec conformance
kyber-weave skill validate ./samples/skills

# routing readiness, with the score broken down
kyber-weave skill lint ./samples/skills --explain

# trust-surface scan (emit SARIF for GitHub code scanning)
kyber-weave skill scan ./samples/skills --format sarif > kyber-weave-skills.sarif

# "which skill fires?" — single prompt
kyber-weave skill route "I'm locked out and forgot my password" --skills ./samples/skills

# routing regression test in CI
kyber-weave skill route --eval ./samples/routing-tests.yml --skills ./samples/skills --min-accuracy 0.85

# agent parity across the six harnesses
kyber-weave agent sync-check .

# documentation governance
kyber-weave docs validate .
kyber-weave docs drift .
kyber-weave docs graph . --out ./build/doc-graph
```

### `skill lint --explain` makes the routing score auditable

```
password-reset — routing score 100/100
  Dimension          Score   Detail
  Trigger clause     25/25   States when to use the skill.
  Negative boundary  20/20   States when NOT to use the skill — prevents over-firing.
  Specific opening   15/15   Opens with a concrete action verb.
  Trigger keywords   20/20   25 distinct content terms — concrete nouns/keywords help routing.
  Length budget      20/20   289 chars — within a healthy routing budget.
```

### `skill route` turns "which skill fires?" into a test

```
✔  I'm locked out of my laptop and forgot my password   →  password-reset
✔  Checkout is throwing 500 errors for a lot of users    →  incident-triage
✔  What's the weather in Kolkata tomorrow?               →  (no fire)
Routing accuracy: 100% (7/7), threshold 85%.
```

The default routing strategy is **lexical** — deterministic and offline, so it runs in CI with no API key. It's a fast baseline, not the orchestrator: a real orchestrator uses the live model over full context. `IRoutingStrategy` is the seam to plug in an embedding or LLM-as-judge strategy for higher fidelity. Treat `skill route` as a pre-deployment signal and regression test, and still confirm against your agent's reasoning view.

---

## The MCP server

`KyberWeave.Mcp` is a stdio MCP server that makes the documentation corpus retrievable by an agent instead of grep-able.

It is a **separate executable**, not a `kyber-weave mcp` subcommand: stdio JSON-RPC owns stdout and the CLI is built on Spectre.Console, which writes there. A separate entry point makes stream corruption structurally impossible rather than a matter of discipline. All logging is pinned to stderr.

| Tool | What it does |
|---|---|
| `docs_explore(query, maxDocs = 5, charBudget = 12000)` | Free text, or a symbol, route, component or doc-id name. Returns ranked documents with their frontmatter identity, as much of their prose as the budget allows, and that document's resolved code joins as `symbol → file:line`. The budget is shared across the returned documents, so lowering `maxDocs` deepens each result instead of just shortening the list. |
| `docs_for_symbol(symbol)` | The precise reverse lookup: the documents whose `code-refs` frontmatter *formally claims* a symbol. A claim of ownership, not a textual occurrence — which is exactly what grep cannot distinguish. |

Registered in the repository's `.mcp.json` as `kyber-weave`, launched via `dotnet run --project`, so it is always current with no install step. Tools surface to agents as `mcp__kyber-weave__docs_explore` and `mcp__kyber-weave__docs_for_symbol`.

The index is loaded once and rebuilt whenever an in-scope document's mtime, the document count, or `.codegraph/codegraph.db` changes. Reloading is required rather than optional: the CodeGraph daemon rewrites that file continuously, so an index built only at startup would start answering with stale joins within minutes of a rename.

`agent_explore` and `skill_explore` are the intended follow-on; the loader and index are shaped per artifact class behind a common shape rather than docs-specific plumbing.

---

## Rule reference

Rule ids are segmented by feature. All are stable identifiers suitable for suppression and SARIF.

**Skill spec (`skill validate`)** — `KW-SKILL-SPEC-001` missing name · `-002` name too long · `-003` invalid name chars · `-004` name↔folder mismatch (silent-load failure) · `-005` missing description · `-006` description too long · `-007` compatibility too long · `-008` angle brackets in front matter · `-009` unknown key · `-010` `allowed-tools` is experimental · `-011` path traversal · `-012` broken reference.

**Skill lint (`skill lint`)** — `KW-SKILL-LINT-001` description below routing-score threshold · `-002`/`-003` over token/line budget · `-004` no ALWAYS/NEVER directives · `-005` no worked example · `-006` empty body · `-010` name collision · `-011` description overlap.

**Skill security (`skill scan`)** — `KW-SKILL-SEC-001..006` prompt-injection / persona hijack / hidden-comment instructions · `-007` base64 blob · `-010..013` risky script patterns (`curl|bash`, obfuscated `eval`, destructive commands) · `-020..023` hardcoded secrets · `-030..032` provenance (author / version / license).

**Agent governance (`agent …`)** — `KW-AGENT-SPEC-001..004` missing name / description / instructions, broken reference · `KW-AGENT-SYNC-001` role unsatisfied on a harness · `-002` instruction drift · `KW-AGENT-LINT-001` low routing score · `KW-AGENT-SEC-001..003` safety bypass, hardcoded secret, risky directive.

**Documentation governance (`docs …`)** — `KW-DOC-SPEC-001..006` missing frontmatter, invalid vocabulary, missing required key, unknown catalog value, missing source root, bad reference · `KW-DOC-DRIFT-001..003` unresolved code ref, unresolved endpoint, source root not indexed.

**Parsing** — `KW-PARSE-000` a file could not be parsed at all.

Security scanning is **necessary but not sufficient** — heuristics miss semantic and multi-file attacks. Use `skill scan` as a gate that raises the bar, paired with human review.

---

## Architecture

```
src/
  KyberWeave.Core/        # the shared engine plus one namespace per artifact class
    Diagnostics/          #   Diagnostic, DiagnosticReport, Severity
    Text/                 #   TextVectorizer (offline lexical similarity)
    Parsing/              #   MarkdownFrontmatterReader — one frontmatter reader for all three
    CodeGraph/            #   CodeGraphResolver — symbol and route resolution
    Skills/               #   Model · Parsing · Validation · Security · Routing
    Agents/               #   Model · Parsing · Validation · Security · Routing
    Docs/                 #   Model · Parsing · Validation · Export · Search
  KyberWeave.Cli/         # dotnet tool (Spectre.Console.Cli): skill | agent | docs
  KyberWeave.Mcp/         # stdio MCP server
tests/KyberWeave.Tests/   # xUnit
samples/
  skills/                 # 4 exemplar skills (sop, runbook, reference, checklist)
  bad-skills/             # deliberately broken + a defanged malicious skill
  routing-tests.yml       # routing eval set (positive + negative cases)
docs/                     # ALM & governance playbook
```

`Diagnostics`, `Text`, `Parsing` and `CodeGraph` are engine, shared by all three features. `CodeGraphResolver` lives outside `Docs/` because symbol resolution is not a documentation concern — agent and skill governance will want it too.

Tests live inside the tool tree, following the precedent for self-contained tools rather than the `5-Test` layer convention that governs application code.

---

## Caveats

- **The Agent Skills spec is young.** Field set, `allowed-tools` status, and token-budget guidance may change. `allowed-tools` is experimental and **not a security control**.
- **Copilot Studio Skills are in preview** and per-agent-scoped; `skill pack`/`skill catalog` track current behaviour and may need updates as the product evolves.
- **The routing simulator approximates, not replicates** the orchestrator. Confirm against the live reasoning view.
- **Static scanning has limits.** Pair `skill scan` with human review.
- **`docs drift` needs a CodeGraph index and the `sqlite3` CLI.** Without both it reports critical rather than silently passing.
- **Four agent subcommands are not wired.** `agent scan`, `agent route`, `agent lint` and `agent new` have Core classes but no CLI verb. Known gap.

## Licence

MIT. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Built on [Markdig](https://github.com/xoofx/markdig), [YamlDotNet](https://github.com/aaubry/YamlDotNet), [Spectre.Console](https://spectreconsole.net/), and the [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) C# SDK.
