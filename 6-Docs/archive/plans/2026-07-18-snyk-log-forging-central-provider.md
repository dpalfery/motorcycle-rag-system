# Snyk CWE-117 Log Forging — Central Provider Remediation

**Status:** Archived
**Date:** 2026-07-18
**Goal:** Eliminate Snyk CWE-117 (log forging) findings by replacing 126+ fragile per-call `LogSanitizer.Sanitize` sites with a single, well-known taint boundary implemented as an `ILoggerProvider` decorator registered in every .NET host.

---

## 1. Problem / Motivation

Snyk Code reports CWE-117 (log forging) on many `ILogger.LogXxx` call sites even though every flagged site already wraps user-controlled arguments in `LogSanitizer.Sanitize(...)`. The codebase is correct; the scanner is the problem.

Root cause (verified against live source, not speculation):

- `0-Base/MotorcycleRAG.Core/Utilities/LogSanitizer.Sanitize` is a textbook CWE-117 mitigator: escapes `\`, `\r`, `\n`, `\t`, `\0`, and every remaining C0/C1 control. The implementation is not at fault.
- Snyk Code's taint analysis recognizes only a closed set of well-known framework sanitizers (e.g., `HttpUtility.HtmlEncode`). A project-local `LogSanitizer.Sanitize` is opaque to the analysis, so the source-to-sink taint edge is never cut. Per-call sanitization therefore does not satisfy the scanner, regardless of correctness.
- Per-call mitigation is also not repeatable: every new `ILogger.LogXxx(...)` call that takes a user-controlled argument is a fresh finding. Suppression via `.snyk` policy is brittle because Snyk Code issue IDs change with file edits.

The same sites are independently reported by CodeQL (`security-extended`) and Semgrep (`p/default`) in `pr-gate.yml`. Per-tool suppression would have to be maintained in three places.

The existing `7-Deployment/DbSetup/MotorcycleRAG.DbSetup/SanitizingLogger<T>` looks like a partial solution but is misleading: it delegates to `SensitiveLogRedactor.SanitizeMessage`, which performs secret redaction (CWE-532) only — it does **not** escape control characters. The companion `SecureLoggerExtensions.AddSecureLogging` is a no-op (`return builder;`). Neither addresses CWE-117.

## 2. Approved decisions

- **D1 — Central `ILoggerProvider` decorator; decommission per-call sites.** Add `SanitizingLoggerProvider : ILoggerProvider` to `MotorcycleRAG.Core`. Register it via `ILoggerFactory.AddProvider` in every .NET host. Remove every per-call `LogSanitizer.Sanitize(...)` argument wrapper in source (controllers, middleware, services, repositories) and remove the BFF's private `SanitizeLogValue` helper. Future `ILogger.LogXxx` callers are safe by default; new code cannot introduce a finding without bypassing the provider.
- **D2 — Sanitize each structured-state value + the `{OriginalFormat}` template.** The decorator's `Log<TState>` walks `IReadOnlyList<KeyValuePair<string,object?>>`, sanitizes each non-`{OriginalFormat}` value via `LogSanitizer.Sanitize`, sanitizes the `{OriginalFormat}` template string, reconstructs an equivalent state, and forwards to the inner logger with an equivalent formatter. Structured-logging fields (App Insights customDimensions, Serilog properties) keep their placeholder names; only the values are cleaned. This is the only option that fully defeats forging in structured downstream sinks.
- **D3 — Six host projects in scope.** `MotorcycleRAG.API`, `MotorcycleRag.WebUI.BFF`, `MotorcycleRAG.DbSetup`, `MotorcycleRAG.AgentProvisioning`, `MotorcycleRAG.AdminDesktop`, `MotorcycleRAG.MobileApp`. Each host calls `builder.Logging.AddSanitizingLogger()` exactly once. Library projects (`0-Base`, `2-Application`, `3-Domain`, `4-Persistence`) receive `ILogger<T>` via DI and are covered automatically.
- **D4 — DbSetup scaffolding: remove `SanitizingLogger<T>` and the no-op extension; keep secret redaction.** Delete `SanitizingLogger.cs` and `SecureLoggerExtensions.cs`. Keep `SecureLogger.cs` and `SensitiveLogRedactor.cs` — they perform CWE-532 secret redaction, a distinct concern from CWE-117. Rewrite DbSetup call sites that used `factory.CreateSecureLogger<T>()` to use `factory.CreateLogger<T>()`; the Core provider covers control-char escaping.
- **D5 — Test strategy: separation of concerns.** A new `SanitizingLoggerProviderTests` suite in `MotorcycleRAG.Core.Tests` is the single source of truth for CWE-117 sanitization behavior. Controller / middleware / service tests drop sanitization-at-call-site assertions (those tests now exercise only behavior; they pass valid inputs and the spy receives raw values because no provider participates in unit-test wiring). Existing `LogSanitizerTests` is preserved — it still tests the canonical sanitizer that the provider consumes.

## 3. Investigation findings

- **Snyk integration** (`7-Deployment/.github/workflows/nightly.yml` L536–591): `snyk code test --severity-threshold=high` runs nightly at 03:00 UTC and on manual dispatch. SARIF uploads to GitHub Security under category `snyk-sast`. CLI pinned to `snyk@1.1293.0`. PR gate does not run Snyk; it runs CodeQL (L231), Semgrep (L318), and Trivy (L269) — all three report CWE-117 independently.
- **No `.snyk` policy file** exists at the repo root or under `7-Deployment/`. No `// snyk` / `// snyk-discard` annotations in source. No existing suppression mechanism.
- **Representative flagged sites (all already sanitized, all still flagged because Snyk does not recognize the custom sanitizer):**
  - `McpAdminController.cs:80, 85, 89` — `toolId` route parameter
  - `RequestPipelineTimingMiddleware.cs:64, 73` — `context.Request.Method`, `Request.Path`
  - `HostHeaderValidationMiddleware.cs` (BFF) L95, L153, L180 — Host header, request path
  - `IngestionAuditLogger.cs:28–37, 50–59` — uploadId, userId, eventName, errorCode
- **Callers of `LogSanitizer.Sanitize`**: 158 sites across 36 files (126 callers of the `string?` overload + 32 callers of the `object?` overload), per `codegraph_explore` blast-radius data. Decommission surface spans `1-Presentation/MotorcycleRAG.API/Controllers/*`, `1-Presentation/MotorcycleRAG.API/Middleware/RequestPipelineTimingMiddleware.cs`, `1-Presentation/MotorcycleRag.WebUI.BFF/Middleware/HostHeaderValidationMiddleware.cs`, `2-Application/MotorcycleRAG.Application/Services/Ingestion/**`, `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/**`.
- **Sites with no user-taint today (constant strings or Guid-only)** — will not produce findings and require no change: `FileUploadController.LegacyDiskUploadGone`, `DataPipelineUploadController.UploadAsync`, `ManualsController.GetPage`.
- **DbSetup decorator addresses the wrong concern**: `SanitizingLogger<T>` calls `SensitiveLogRedactor.SanitizeMessage`, which performs regex-based secret replacement (`password|pwd|secret|token|key`). It does not escape `\n`, `\r`, `\t`, `\0`, or C0/C1 controls. It would not satisfy CWE-117 even if it were registered, which it is not (`AddSecureLogging` returns the builder unchanged).

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| T1 | 1 | `0-Base/MotorcycleRAG.Core/Logging/SanitizingLoggerProvider.cs` (new) | Implement `SanitizingLoggerProvider : ILoggerProvider`, `SanitizingLogger<T> : ILogger<T>`, and `SanitizingLoggerExtensions.AddSanitizingLogger(this ILoggingBuilder)`. The `Log<TState>` override walks the structured state list, sanitizes each non-`{OriginalFormat}` value via `LogSanitizer.Sanitize`, sanitizes the `{OriginalFormat}` template, reconstructs an equivalent state, and forwards to the inner logger. Pass through `BeginScope`, `IsEnabled`, and exceptions unchanged. Handles both compiler-generated `FormattedLogValues` (from `LoggerExtensions`) and source-generated `LoggerMessage` state shapes. | `dotnet-dev` |
| T2 | 1 | `5-Test/MotorcycleRAG.Core.Tests/Logging/SanitizingLoggerProviderTests.cs` (new) | Comprehensive CWE-117 coverage: CR, LF, CRLF, tab, null, every C0 char, every C1 char, backslash, backslash-then-control (reversibility), Unicode controls. Multi-value structured state with mixed taint. `{OriginalFormat}` template sanitization (forged template cannot inject placeholders). Exception object passthrough. Scope passthrough. Null and empty state. LoggerMessage source-generated message. Asserts the inner (spy) logger receives sanitized values and that structured placeholder names are preserved. | `test-dev` |
| T3 | 1 | `5-Test/MotorcycleRAG.Core.Tests/Logging/SanitizingLoggerExtensionsTests.cs` (new) | Verify `AddSanitizingLogger` registers exactly one `SanitizingLoggerProvider` and that the resulting factory produces loggers whose output is sanitized end-to-end. | `test-dev` |
| T4 | 2 | Six host `Program.cs` / `MauiProgram.cs` files | Add `builder.Logging.AddSanitizingLogger();` to `MotorcycleRAG.API/Program.cs`, `MotorcycleRag.WebUI.BFF/Program.cs`, `MotorcycleRAG.DbSetup/Program.cs` (or logging-setup file), `MotorcycleRAG.AgentProvisioning/Program.cs`, `MotorcycleRAG.AdminDesktop/.../Program.cs`, `MotorcycleRAG.MobileApp/MauiProgram.cs`. Position the call after other logging configuration so the provider wraps the inner loggers. | `dotnet-dev` |
| T5 | 2 | Per-host registration-gate test (one per host test project) | Assert each host's `ILoggingBuilder` registers `SanitizingLoggerProvider` (defense against misconfiguration; required because per-call defense-in-depth is gone). | `test-dev` |
| T6 | 3 | Decommission: `1-Presentation/MotorcycleRAG.API/**` | Remove every `LogSanitizer.Sanitize(...)` argument wrapper from `ILogger.LogXxx` calls. Strip the `using MotorcycleRAG.Core.Utilities;` directive where it becomes unused. Files include `Controllers/McpAdminController.cs`, `Controllers/AccessRequestsAdminController.cs`, `Controllers/PipelineProcessingController.cs`, `Controllers/UsersAdminController.cs`, `Middleware/RequestPipelineTimingMiddleware.cs`, and all other API call sites surfaced by `rg "LogSanitizer\.Sanitize\(" 1-Presentation/MotorcycleRAG.API`. | `dotnet-dev` |
| T7 | 3 | Decommission: `1-Presentation/MotorcycleRag.WebUI.BFF/Middleware/HostHeaderValidationMiddleware.cs` | Remove the private `SanitizeLogValue` helper and all its call sites; pass raw `hostValue`, `hostOnly`, `context.Request.Path` to `ILogger.LogXxx`. | `dotnet-dev` |
| T8 | 3 | Decommission: `2-Application/MotorcycleRAG.Application/**` | Remove every `LogSanitizer.Sanitize(...)` argument wrapper from `ILogger.LogXxx` calls across Application services (notably `Services/Ingestion/Audit/IngestionAuditLogger.cs`, `Services/Ingestion/IngestionJobService.cs`, and all other call sites surfaced by `rg "LogSanitizer\.Sanitize\(" 2-Application/MotorcycleRAG.Application`). | `dotnet-dev` |
| T9 | 3 | Decommission: `4-Persistence/MotorcycleRAG.Persistence/**` | Remove every `LogSanitizer.Sanitize(...)` argument wrapper from `ILogger.LogXxx` calls in Persistence (notably `Sql/Repositories/IndexedArtifactRepository.cs`, `Sql/Repositories/IngestionJobRepository.cs`, and other call sites surfaced by `rg "LogSanitizer\.Sanitize\(" 4-Persistence/MotorcycleRAG.Persistence`). | `dotnet-dev` |
| T10 | 3 | Decommission: existing test files with sanitization-at-call-site assertions | In `McpAdminControllerTests.cs`, `RequestPipelineTimingMiddlewareTests.cs`, `HostHeaderValidationMiddlewareTests.cs`, `IngestionAuditLoggerTests.cs`, and any other test that passes a tainted value and asserts a sanitized value reached the spy logger: drop the sanitization assertion (test now verifies only behavior with valid inputs) or remove the test if it existed solely to assert sanitization. Do not modify `LogSanitizerTests.cs`. | `test-dev` |
| T11 | 3 | DbSetup scaffolding cleanup | Delete `7-Deployment/DbSetup/MotorcycleRAG.DbSetup/SanitizingLogger.cs` and `7-Deployment/DbSetup/MotorcycleRAG.DbSetup/SecureLoggerExtensions.cs`. Update any `factory.CreateSecureLogger<T>()` call sites in DbSetup to use `factory.CreateLogger<T>()`. Keep `SecureLogger.cs` and `SensitiveLogRedactor.cs` unchanged (CWE-532 secret redaction, separate concern). | `dotnet-dev` |
| T12 | 4 | Verification: source-gate greps | Confirm `rg "LogSanitizer\.Sanitize\(" --type cs` matches only: (a) the `LogSanitizer` implementation itself, (b) the new `SanitizingLoggerProvider.cs`, (c) `LogSanitizerTests.cs`, and (d) `SanitizingLoggerProviderTests.cs`. Confirm `rg "SanitizeLogValue" --type cs` returns zero matches. Document the exact grep outputs in the implementation evidence. | `dotnet-dev` |
| T13 | 5 | Review | Code review by `code-reviewer` (correctness, structured-state reconstruction, no regressions) and security review by `security-review` (CWE-117 mitigation is correct, no CWE-532 regression introduced). | `code-review`, `security-review` |
| T14 | 6 | Plan closeout | `docs-dev` verifies the plan's acceptance criteria against implementation evidence, updates affected canonical documentation (notably the API and BFF security/architecture docs that currently describe per-call `LogSanitizer.Sanitize` as the mitigation, and `6-Docs/system/security.md` if it mentions log forging), and archives the plan under `6-Docs/archive/plans/`. | `docs-dev` |

## 5. Sequencing / dependency graph

```
T1 (provider) ─┬─> T2 (provider tests)        ─┐
               └─> T3 (extensions tests)       ─┤
                                               ├─> T4 (host registration) ─┬─> T5 (registration gates)
                                               │                            │
                                               │                            ├─> T6  (API decommission)
                                               │                            ├─> T7  (BFF decommission)
                                               │                            ├─> T8  (Application decommission)
                                               │                            ├─> T9  (Persistence decommission)
                                               │                            ├─> T10 (test assertion cleanup)
                                               │                            └─> T11 (DbSetup scaffolding)
                                               │                                        │
                                               │                                        v
                                               │                                       T12 (grep gates)
                                               │                                        │
                                               │                                        v
                                               └───────────────────────────────────────>T13 (review)
                                                                                        │
                                                                                        v
                                                                                       T14 (closeout)
```

T1 is the critical path. T4 (host registration) cannot be done until T1 builds clean. T6–T11 (decommission) can run in parallel once T4 lands — they are file-scope independent and do not touch the provider. T10 depends on T6–T9 because the test assertions being removed live next to the code being decommissioned. T12 is the verification gate after all decommission lands. T13 is review after green builds. T14 is documentation closeout after T13 approves.

## 6. Residual decisions / risks

- **Truncation lost**: Several per-call sites use `LogSanitizer.Sanitize(value, maxLength: N)` for log-volume shaping, not security. After decommission, those sites pass the full value. **Owner**: implementing `dotnet-dev` agent to flag any site where truncation was load-bearing (e.g., file paths or blob URIs that could be very long); the plan does not reintroduce truncation but the agent should call out sites that warrant a follow-up.
- **Snyk recognition of `ILoggerProvider`**: Assumes Snyk Code treats the provider's `Log<TState>` call into the inner logger as the sink and cuts the taint edge at the provider boundary. This is the standard behavior for taint analyzers, but is not verifiable locally (Snyk runs nightly, requires `SNYK_TOKEN`). **Mitigation**: if the post-merge nightly still reports CWE-117 at the inner `_innerLogger.Log(...)` call inside the provider, add `// snyk-discard` on that single line with a justification comment, and update the canonical documentation to explain the suppression. Single suppression point is acceptable; per-site suppression is not.
- **Defense-in-depth lost**: With per-call sites removed, a misconfigured host has no fallback sanitization. **Mitigation**: T5 per-host registration-gate tests fail the build if `AddSanitizingLogger` is missing.
- **`LoggerMessage` source-generated state shape**: Source-generated strongly-typed log methods produce a different state type than `LoggerExtensions.LogXxx`. T1 must handle both shapes; T2 must include a source-generated-message test case. **Owner**: implementing `dotnet-dev` to verify against an actual `LoggerMessage` partial method in the codebase.
- **Performance**: Every log call now walks the state list and runs `LogSanitizer.Sanitize` on each value. Cost is O(total state length) per call. Negligible for typical log volumes; no action required unless profiling reveals a hot path.

## 7. Out of scope

- **Python `local-processing-service`** — uses Python `logging`, separate sanitizer stack. Belongs in a Python-specific plan if needed.
- **WebUI TypeScript frontend** — browser-side `console.*` logging; out of server-side CWE-117 scope.
- **CWE-532 secret redaction** — kept in DbSetup's `SecureLogger` / `SensitiveLogRedactor`. If a separate plan wants to lift secret redaction into a Core provider analogous to the CWE-117 provider, that is its own design exercise; this plan does not pre-empt it.
- **CodeQL, Semgrep, Trivy finding remediation beyond CWE-117** — those scanners will benefit from this change for CWE-117 specifically, but any unrelated findings they report are out of scope.
- **The "124 open legacy CodeQL on develop" backlog (T17 from the prior security-quality plan)** — separate plan, separate work.
- **MobileApp and AdminDesktop detailed logging-audit** — in scope only for provider registration (T4) and registration-gate test (T5). Deep audit of those hosts' user-input logging is out of scope; the provider gives them automatic coverage by construction.

## 8. Required skills

- `dotnet-dev` — provider implementation, host registration, decommission across all four .NET layers, DbSetup scaffolding cleanup, source-gate grep verification.
- `test-dev` — new `SanitizingLoggerProviderTests` and `SanitizingLoggerExtensionsTests` suites; per-host registration-gate tests; cleanup of existing test files that assert sanitization at call sites.
- `code-review` — correctness review (structured-state reconstruction, no behavior regressions).
- `security-review` — CWE-117 mitigation correctness and CWE-532 non-regression.
- `docs-dev` — plan closeout, canonical documentation updates.

## 9. Verification harness

**Build gates (Release, all must be 0 warnings / 0 errors for the affected files):**
- `MotorcycleRAG.Core` — provider implementation.
- `MotorcycleRAG.API`, `MotorcycleRag.WebUI.BFF`, `MotorcycleRAG.DbSetup`, `MotorcycleRAG.AgentProvisioning`, `MotorcycleRAG.AdminDesktop`, `MotorcycleRAG.MobileApp` — hosts with provider registered.
- `MotorcycleRAG.Application`, `MotorcycleRAG.Persistence` — libraries with decommissioned call sites.
- `MotorcycleRAG.Core.Tests`, `MotorcycleRAG.API.Tests`, `MotorcycleRAG.Application.Tests`, `MotorcycleRAG.Persistence.Tests` — test projects.

**Test gates:**
- `SanitizingLoggerProviderTests` and `SanitizingLoggerExtensionsTests` pass the full CWE-117 control-char matrix (CR, LF, CRLF, tab, null, C0, C1, backslash, backslash-then-control, Unicode controls).
- Per-host registration-gate tests pass.
- All pre-existing test suites that do not assert sanitization still pass.

**Static verification (grep gates):**
- `rg "LogSanitizer\.Sanitize\(" --type cs` matches only `LogSanitizer.cs` (implementation), `SanitizingLoggerProvider.cs` (provider), `LogSanitizerTests.cs` (unit tests), and `SanitizingLoggerProviderTests.cs` (provider tests).
- `rg "SanitizeLogValue" --type cs` returns zero matches.
- `rg "AddSecureLogging|CreateSecureLogger|new SanitizingLogger" --type cs` returns zero matches (DbSetup scaffolding fully removed).

**Review gates:**
- `code-reviewer` approval — APPROVE verdict.
- `security-review` approval — confirms CWE-117 is correctly mitigated at the provider boundary and CWE-532 secret-redaction behavior in DbSetup is unchanged.

**Out-of-band verification (post-merge):**
- Next Snyk nightly run (`snyk code test --severity-threshold=high`) reports zero CWE-117 findings. If any remain, the residual-decision path in §6 applies. Tracked via the plan's `Review required` state until the nightly confirms.
