# SkillForge

**A .NET-native toolkit to author, validate, lint, security-scan, and routing-test [Agent Skills](https://agentskills.io/) (`SKILL.md`) — built for enterprise Microsoft teams.**

Modern agents — Copilot Studio, the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/agents/skills), Claude Code, Codex — load **Skills** on demand: a `SKILL.md` carrying a name, a description, and instructions, optionally bundled with `scripts/`, `references/`, and `assets/`. The format is an open standard (originally from Anthropic). Once you have more than a handful of skills across several agents and environments, a skill stops being a text file and becomes a **software supply-chain artifact**: versioned, reviewed, security-scanned, and gated in CI.

Every existing linter/validator for `SKILL.md` is written in Python, JS/TS, Rust, or Dart. **SkillForge is the .NET one** — a `dotnet tool`, a NuGet library, and a CI gate that fits where Microsoft enterprise teams already work.

> Status: early (v0.1.0). The Agent Skills spec is young and Copilot Studio Skills are in preview; some rules are versioned and will track the spec. See [Caveats](#caveats).

---

## What it does

| Command | What it answers | Gate |
|---|---|---|
| `validate` | Is this a spec-conformant skill? (name rules, name↔folder match, description limits, broken references, angle-bracket safety) | fails on **error** |
| `lint` | Will the orchestrator route to it correctly, and is it context-efficient? (description routing score, progressive-disclosure budget, **collision & overlap detection**) | fails on **error** (name collision) |
| `scan` | Can I trust this skill? (prompt-injection, hidden-comment instructions, encoded payloads, secrets, risky scripts, provenance) | fails on **critical** (configurable) |
| `route` | **Which skill fires for this prompt?** Single-prompt simulation, or an **eval set** as a regression test (incl. negative "should fire nothing" cases) | fails below **--min-accuracy** |
| `catalog` | What skills exist across my tree, with version/owner/score? | — |
| `pack` | Bundle a skill into a Copilot Studio–compatible `.zip` | — |
| `new` | Scaffold a spec-correct skill from a template (sop / runbook / reference / checklist) | — |

---

## Install

```bash
# from a built package
dotnet tool install --global --add-source ./nupkg SkillForge.Tool

# or run from source
dotnet run --project src/SkillForge.Cli -- <command> [args]
```

Requires the .NET 8 SDK.

---

## Quick start

```bash
# Spec conformance
skillforge validate ./samples/skills

# Routing readiness, with the per-skill rubric
skillforge lint ./samples/skills --explain

# Trust-surface scan (emit SARIF for GitHub code scanning)
skillforge scan ./samples/skills --format sarif > skillforge.sarif

# "Which skill fires?" — single prompt
skillforge route "I'm locked out and forgot my password" --skills ./samples/skills

# Routing regression test in CI
skillforge route --eval ./samples/routing-tests.yml --skills ./samples/skills --min-accuracy 0.85

# Governance inventory
skillforge catalog ./samples/skills

# Scaffold a new skill, then check it
skillforge new leave-request --template sop
skillforge validate ./leave-request && skillforge lint ./leave-request --explain
```

### `lint --explain` makes the routing score auditable

```
password-reset — routing score 100/100
  Dimension          Score   Detail
  Trigger clause     25/25   States when to use the skill.
  Negative boundary  20/20   States when NOT to use the skill — prevents over-firing.
  Specific opening   15/15   Opens with a concrete action verb.
  Trigger keywords   20/20   25 distinct content terms — concrete nouns/keywords help routing.
  Length budget      20/20   289 chars — within a healthy routing budget.
```

### `route` turns "which skill fires?" into a test

```
✔  I'm locked out of my laptop and forgot my password   →  password-reset
✔  Checkout is throwing 500 errors for a lot of users    →  incident-triage
✔  What's the weather in Kolkata tomorrow?               →  (no fire)
Routing accuracy: 100% (7/7), threshold 85%.
```

The default routing strategy is **lexical** — deterministic and offline, so it runs in CI with no API key. It's a fast baseline, not the orchestrator: a real orchestrator uses the live model over full context. `IRoutingStrategy` is the seam to plug in an embedding or LLM-as-judge strategy for higher fidelity. Treat `route` as a pre-deployment signal and regression test, and still confirm against your agent's reasoning view.

---

## Use it as a CI gate

`.github/workflows/ci.yml` in this repo treats the bundled skills as governed artifacts — spec, routing, and security all must pass before merge:

```yaml
- run: skillforge validate ./samples/skills
- run: skillforge lint ./samples/skills --min-desc-score 70
- run: skillforge scan ./samples/skills --format sarif > skillforge.sarif
- uses: github/codeql-action/upload-sarif@v3
  with: { sarif_file: skillforge.sarif }
- run: skillforge route --eval ./samples/routing-tests.yml --skills ./samples/skills --min-accuracy 0.85
```

Point it at your own skills directory and you have spec, routing-readiness, security, and routing-regression gates on every PR — with findings surfaced in the GitHub Security tab via SARIF.

---

## Rule reference

**Spec (`validate`)** — `SF-SPEC-001` missing name · `-002` name too long · `-003` invalid name chars · `-004` name↔folder mismatch (silent-load failure) · `-005` missing description · `-006` description too long · `-007` compatibility too long · `-008` angle brackets in front matter · `-009` unknown key · `-010` `allowed-tools` is experimental · `-011` path traversal · `-012` broken reference.

**Lint (`lint`)** — `SF-LINT-001` description below routing-score threshold · `-002`/`-003` over token/line budget · `-004` no ALWAYS/NEVER directives · `-005` no worked example · `-006` empty body · `-010` name collision · `-011` description overlap.

**Security (`scan`)** — `SF-SEC-001..006` prompt-injection / persona hijack / hidden-comment instructions · `-007` base64 blob · `-010..013` risky script patterns (`curl|bash`, obfuscated `eval`, destructive commands) · `-020..023` hardcoded secrets · `-030..032` provenance (author / version / license).

Security scanning is **necessary but not sufficient** — heuristics miss semantic and multi-file attacks. Use `scan` as a gate that raises the bar, paired with human review.

---

## Architecture

```
src/
  SkillForge.Core/        # parse · model · validate · lint · scan · route  (NuGet library)
    Model/                #   Skill, SkillFrontmatter, SkillSet, SkillResource
    Parsing/              #   SkillParser (Markdig + YamlDotNet), SkillLoader
    Validation/           #   SpecValidator, DescriptionScorer, RoutingLinter
    Security/             #   SkillScanner
    Routing/              #   IRoutingStrategy, LexicalRoutingStrategy, RoutingEvaluator
    Text/                 #   TextVectorizer (offline lexical similarity)
  SkillForge.Cli/         # dotnet tool (Spectre.Console.Cli)
tests/SkillForge.Tests/   # xUnit
samples/
  skills/                 # 4 exemplar skills (sop, runbook, reference, checklist)
  bad-skills/             # deliberately broken + a defanged malicious skill
  routing-tests.yml       # routing eval set (positive + negative cases)
docs/                     # ALM & governance playbook
```

The library is usable on its own (`SkillForge.Core`) — parse and analyze skills inside your own .NET app, pipeline, or agent.

---

## Caveats

- **Spec is young.** Field set, `allowed-tools` status, and token-budget guidance may change. `allowed-tools` is experimental and **not a security control**.
- **Copilot Studio Skills are in preview** and per-agent-scoped; `pack`/`catalog` track current behavior and may need updates as the product evolves.
- **The routing simulator approximates, not replicates** the orchestrator. Confirm against the live reasoning view.
- **Static scanning has limits.** Pair `scan` with human review.

## Upstream attribution

This repository vendors SkillForge from [bonaniibm/SkillForge](https://github.com/bonaniibm/SkillForge). The upstream project is MIT-licensed; this copy retains the upstream [LICENSE](LICENSE). The checked-in copy does not retain an import commit, so any refresh must record the verified upstream commit SHA in its pull request and update this section.

## License

MIT. See [LICENSE](LICENSE). Built on [Markdig](https://github.com/xoofx/markdig), [YamlDotNet](https://github.com/aaubry/YamlDotNet), and [Spectre.Console](https://spectreconsole.net/).
