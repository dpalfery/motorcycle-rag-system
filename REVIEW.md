# Review instructions

MotorcycleRAG is a Clean Architecture .NET solution (API, Web UI + BFF, Admin Desktop, MAUI mobile) with a Python local-processing service and Pulumi (C#) infrastructure. Repository policy lives in `AGENTS.md` files and `6-Docs/`; the rules below are the ones reviews must enforce.

## What Important means here

Reserve Important for findings that would break behavior, leak data, or violate a non-negotiable repository rule:

- Incorrect logic, broken edge cases, unhandled failure paths, or race conditions.
- Any secret, token, connection string, password, or customer data in code, config, tests, scripts, or docs — including `.env` files and appsettings values. No exceptions.
- .NET code reading application settings or secrets with `Environment.GetEnvironmentVariable()` or hardcoded values instead of Azure App Configuration and Key Vault references. The Python local-processing-service runtime values set by Admin Desktop are the only approved environment-variable use.
- SQL built by string concatenation or interpolation; queries must be parameterized.
- User input concatenated into log messages; PII or query text logged without redaction; unencoded output rendered into HTML.
- An endpoint or action without explicit authorization (default is no access); an admin endpoint that does not validate the `azp` claim against the Admin App client ID; a new public API without rate limiting.
- Clean Architecture dependency violations: an inner layer referencing an outer layer (Domain → Application/Persistence/Presentation, Application → Persistence or Presentation); framework or infrastructure code (EF Core, Azure SDK, HTTP) in Domain or Application; non-interface types added to `MotorcycleRAG.Contracts`; non-DTO types added to `Contracts.Models`; business invariants implemented outside Domain.
- A fallback, stub, or degraded workaround that masks a failure instead of fixing the root cause — especially one that silently swallows errors.
- Anything that deploys outside GitHub Actions: scripts, workflow steps, or docs that run `pulumi up` locally, perform `az` write operations, or run `docker build` / `docker push` / `az acr build` directly. The `deploy.yml` pipeline is the only deployment path.

Style, naming, formatting, and refactoring preferences are Nit at most.

## Cap the nits

Report at most five Nits per review. If you found more, say "plus N similar items" in the summary instead of posting them inline. If everything you found is a Nit, lead the summary with "No blocking issues."

## Do not report

- Anything CI already enforces: markdownlint, documentation structure and link checks (`7-Deployment/scripts/validate-docs.sh`, lychee), gitleaks on documentation, code formatting, and the coverage gates in `comprehensive-testing.yml`.
- `6-Docs/archive/**` — historical content, explicitly not current guidance.
- `.agent-scratch-pad/**` — scratch notes, not deliverables.
- `7-Deployment/tools/KyberWeave/**` — stub only; product source is [dpalfery/kyber-weave](https://github.com/dpalfery/kyber-weave).
- Generated code and lockfiles: `**/api/generated/**`, `*.lock`, `packages.lock.json`, `pnpm-lock.yaml`.
- Test-only code that intentionally violates production rules (fakes, fixtures, in-memory doubles).

## Always check

- A change to a documented public interface, configuration surface, architecture, runtime, or workflow updates its canonical documentation under `6-Docs/` (and adds a `6-Docs/catalog.md` row for a new component). Documentation is part of the change; report a missing update as Important.
- New or changed behavior comes with focused test updates.
- React UI changes stay CSP-safe: MUI with Pigment CSS only — no Emotion, no styling that requires `unsafe-inline`.
- New C# types follow the placement rules: one class or interface per file, in the layer and folder `6-Docs/rules/architecture-general.md` prescribes.

## Verification bar

Claims about behavior need a `file:line` citation from the actual source, not an inference from names. Before reporting a layer violation, cite the offending project reference or `using` directive.

## Re-reviews

After the first review of a PR, suppress new Nits and post Important findings only.

## Summary shape

Open the review body with a one-line tally by severity (for example, "1 Important, 3 Nits"). Lead with "No blocking issues" when there are none.
