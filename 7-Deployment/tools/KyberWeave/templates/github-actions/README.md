# Kyber-Weave GitHub Actions templates

Sample workflows for host repositories that consume the **KyberWeave.Tool** `dotnet` tool. Copy these into your repo’s `.github/workflows/`, then adapt the env vars and path conventions below.

These are **product templates**, not live host CI. MotorcycleRAG (and any other host) should copy/adapt them; do not treat paths or secrets from a single host as required.

| Template | Gates |
| --- | --- |
| [`kyber-weave-skill-gate.yml`](kyber-weave-skill-gate.yml) | `skill validate` · `skill lint` · `skill scan` (SARIF) |
| [`kyber-weave-agent-gate.yml`](kyber-weave-agent-gate.yml) | `agent validate` · `agent scan` (per harness) · `agent sync-check` |
| [`kyber-weave-docs-gate.yml`](kyber-weave-docs-gate.yml) | `docs validate` · `docs drift` (needs a CodeGraph index) |

---

## How to copy

1. Copy one or more YAML files into `.github/workflows/` in the host repo.
2. Set **version pins** (see below). Never leave placeholder versions in production CI.
3. Adjust env paths to match the host layout:
   - Skills: `SKILL_DIRS` (space-separated directories that contain skill folders)
   - Agents: repository root (default `.`); harness matrix via `HARNESS_MATRIX`
   - Docs: `DOCS_ROOT` (passed as `--docs-root`; CLI default is `6-Docs`)
4. Optionally add a root `kyber-weave.yml` for ontology / harness overrides (host policy).
5. Wire the new workflow job names into branch protection if they should block merges.
6. Grant `security-events: write` if you keep SARIF upload steps (code scanning).

### Minimal smoke after copy

```bash
# Install the pinned tool locally, then run the same verbs CI will run
dotnet tool install --global KyberWeave.Tool --version <PINNED_VERSION>
kyber-weave skill validate .agents/skills --format table
kyber-weave agent sync-check . --format table
kyber-weave docs validate . --docs-root docs --format table
```

---

## Version pins (required)

Every template uses explicit placeholders. Replace them before enabling the workflow as a required check.

| Placeholder | Where | Purpose |
| --- | --- | --- |
| `KYBER_WEAVE_VERSION` | workflow `env` | Exact SemVer of `KyberWeave.Tool` (e.g. `0.1.0`). Prefer the same version as `KyberWeave.Core` / MCP packages. |
| `DOTNET_VERSION` | workflow `env` | SDK roll-forward band matching the tool TFM (currently `10.0.x` for `net10.0`). |
| Third-party `uses:` SHAs | each step | Already pinned to full commit SHAs with a `# vN` comment. Bump deliberately; do not switch to floating tags. |

```yaml
env:
  # PIN: published KyberWeave.Tool SemVer — replace before production use
  KYBER_WEAVE_VERSION: "0.1.0"
  DOTNET_VERSION: "10.0.x"
```

### Package feed

Until a public nuget.org publish exists, point `dotnet tool install` at your feed:

```yaml
# Optional: private feed (e.g. GitHub Packages). Add a PAT with read:packages
# as repository/environment secret NUGET_AUTH_TOKEN when required.
- name: Add Kyber-Weave NuGet source
  if: ${{ env.KYBER_WEAVE_NUGET_SOURCE != '' }}
  run: >
    dotnet nuget add source "${{ env.KYBER_WEAVE_NUGET_SOURCE }}"
    --name kyber-weave
    --username "${{ github.actor }}"
    --password "${{ secrets.NUGET_AUTH_TOKEN }}"
    --store-password-in-clear-text
```

Set `KYBER_WEAVE_NUGET_SOURCE` in the workflow `env` (or omit / leave empty for nuget.org).

### Local-source fallback (vendored tree only)

If the host still vendors Kyber-Weave source and has not switched to packages:

```yaml
- run: dotnet build path/to/KyberWeave.Cli.csproj -c Release
- run: >
    dotnet run --project path/to/KyberWeave.Cli.csproj
    -c Release --no-build -- skill validate .agents/skills --format table
```

Prefer the tool-install path once packages are published (extraction plan D9).

---

## Path conventions (adapt freely)

| Concern | Template default | Notes |
| --- | --- | --- |
| Skills | `.agents/skills` | Templates iterate `SKILL_DIRS` and **skip missing** directories. Add `.claude/skills`, `.kilo/skills`, etc. as needed. |
| Agents | repo root `.` | Harnesses discovered under `.*/agents`. Matrix default: `codex`, `cursor`, `claude`, `github`, `opencode`, `kilo`. |
| Docs root | `docs` | Override with `--docs-root` / `DOCS_ROOT`. Hosts that use `6-Docs` should set that explicitly. |
| CodeGraph | `.codegraph/codegraph.db` | Required for `docs drift`. Templates leave install/sync as a commented host step — CodeGraph is **not** part of the Kyber-Weave product kernel. |

---

## Permissions checklist

| Need | Permission |
| --- | --- |
| Checkout | `contents: read` |
| Upload SARIF to code scanning | `security-events: write` |
| Private NuGet (GitHub Packages) | secret `NUGET_AUTH_TOKEN` (or equivalent) |

Default workflow permissions in the samples are least-privilege (`contents: read`); SARIF jobs raise `security-events` at the job level.

---

## What these templates intentionally omit

- Host-only path filters, continuation-on-error TODOs, and merge-summary aggregation
- NVIDIA SkillSpector (advisory companion scanner — host-owned)
- MotorcycleRAG layout (`6-Docs`, `7-Deployment/tools/...`) as required inputs
- Azure / deployment secrets
