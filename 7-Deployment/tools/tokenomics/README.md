# Tokenomics — Dev-Tooling Bootstrap

Idempotent bootstrap for the three agent-development tools used by MotorcycleRAG.
Run it to install CodeGraph at the project level, build its knowledge-graph index,
and verify the two supporting global binaries.

```bash
./7-Deployment/tools/tokenomics/setup-tokenomics.sh            # default
./7-Deployment/tools/tokenomics/setup-tokenomics.sh --rebuild   # full reindex
./7-Deployment/tools/tokenomics/setup-tokenomics.sh --help
```

## What it does

| Tool | Handling | Why |
| --- | --- | --- |
| **CodeGraph** | Project-level install (`npm ci` in `tools/codegraph/`) + index build via the **vendored pinned binary** (`@colbymchenry/codegraph@1.5.0`). First run `init`s; re-runs `sync`; `--rebuild` forces a full `index`. | The only tool with real project-level state — the `.codegraph/` graph that Kyber-Weave drift checks and the CodeGraph MCP tool read. |
| **RTK** (Rust Token Killer) | **Detect-only.** Verified on PATH and checked against the recommended floor (`0.42`); never auto-installed. | A global, stateless Rust CLI proxy with no project-level form. Install separately: `brew install rtk` or `cargo install brokk-rtk`. See `CLAUDE.md`. |
| **Kyber-Weave** | **Detect-only.** When present, runs `docs validate` + `docs drift` against the freshly built CodeGraph index. | A global governance binary (`@dpalfery/kyber-weave@0.1.1`, the CI pin). Install: `npm i -g @dpalfery/kyber-weave@0.1.1`, `brew install dpalfery/kyber-weave/kyber-weave`, or a [GitHub Release](https://github.com/dpalfery/kyber-weave/releases). |

**Exit status:** `0` when CodeGraph installs and indexes successfully. RTK and
Kyber-Weave checks are advisory (warnings only); a non-zero exit means CodeGraph
failed.

## References

- [Kyber-Weave reference](../../../6-Docs/reference/kyber-weave.md)
- Host overrides: [`.kyber-weave/kyber-weave.yml`](../../../.kyber-weave/kyber-weave.yml)
- MCP registration: [`.mcp.json`](../../../.mcp.json)
- CI install action: `.github/actions/install-kyber-weave`
