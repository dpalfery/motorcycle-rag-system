# Local processor outbound HTTP / SSRF hardening

**Status:** Draft
**Date:** 2026-07-19
**Goal:** One consistent, policy-bound outbound HTTP story for local-processor model-provider calls — fix localhost multi-address dial, close SSRF theater gaps, and lock the contract with tests.

---

## 1. Problem / Motivation

Embedding model discovery fails for `http://localhost:1234/v1` while the same LM Studio instance is healthy for graph/metadata probes and plain `httpx`, because discovery alone uses `security.safe_http`, and `_SafeTransport` / `_SafeAsyncTransport` dial only `dial_targets[0]` (often `::1`) after validating all loopback answers.

Root-cause chain (verified against live source):

1. `validate_model_discovery_endpoint` correctly allows literal-loopback HTTP (`localhost` / `127.0.0.1` / `::1`) and public HTTPS (prior D4 / CodeQL residual intent).
2. `_SystemAddressResolver` / `_SystemAsyncAddressResolver` resolve all A/AAAA answers; `require_loopback_addresses` keeps them.
3. `_SafeTransport.handle_request` / `_SafeAsyncTransport.handle_async_request` call `_send_request(..., dial_targets[0], ...)` only — no connect fallback.
4. On macOS, `localhost` typically yields `::1` then `127.0.0.1`. LM Studio listens on IPv4 `127.0.0.1:1234` only → connection refused → `ModelDiscoveryError`.
5. After discovery succeeds (or when bypassing via `127.0.0.1`), `OpenAIEmbedder` / `MetadataExtractor` / `GraphExtractor` use plain OpenAI SDK + `httpx` (Happy Eyeballs / multi-address), so they work with `localhost` while remaining outside DNS-pin / redirect policy.
6. Docs claim outbound calls go through `url_validation` / `safe_http`; that is true for `ApiClient` and discovery, not for embed/graph/metadata LLM traffic.

Rewriting config `localhost` → `127.0.0.1` masks (3); it is not the durable fix.

---

## 2. Approved decisions

| ID | Decision |
| --- | --- |
| D1 | Preserve operator model-endpoint probe policy: **public HTTPS** or **literal-loopback HTTP**; no private/link-local remote HTTP; no redirects; DNS answers re-validated before dial; TLS verify remains on for HTTPS. |
| D2 | Unify **all OpenAI-compatible model-provider HTTP** (discovery, embed create, embed `check_status`, metadata probe + chat, graph chat) onto policy-bound `safe_http` transports — not discovery-only. |
| D3 | After policy validation, try **all** validated dial targets on **connection-establishment** failure (`httpx.ConnectError` / underlying connection refused; not HTTP 4xx/5xx). Preserve Host header and TLS SNI of the original hostname. Not a config rewrite. |
| D4 | Do **not** install OS trust stores or weaken TLS for cloud/API clients. `ApiClient` stays on `API_HTTPS` / `create_api_https_async_client`. |
| D5 | **Ollama = O1:** Validate Ollama host (`OLLAMA_BASE_URL` / `OLLAMA_HOST` / factory-supplied host) with the same endpoint policy at construct; leave `ollama.AsyncClient` for embed/list/health; document an explicit residual (no mid-flight DNS pin / redirect control inside the Ollama SDK). |
| D6 | Multi-address connect fallback applies to **all** `EndpointPolicy` values (LOOPBACK_HTTP, PUBLIC_HTTPS, API_HTTPS), not only loopback. |
| D7 | Design approach is **Option A (transport-first unify):** shared model-provider client factory + OpenAI `http_client` injection + dial fallback. Dial-fix-only (B) and hybrid (C) are rejected. |

---

## 3. Investigation findings

### Outbound HTTP surface map

| Surface | Module / symbols | Transport today | Target under this plan |
| --- | --- | --- | --- |
| Embedding discovery sync/async | `model_discovery` → `create_model_discovery_(async_)client` | `safe_http` | Keep; gain dial fallback |
| OpenAI embed create | `OpenAIEmbedder._create_client` | Plain OpenAI/`httpx` | Inject safe async httpx (D2) |
| OpenAI embed health | `OpenAIEmbedder.check_status` | Plain `httpx` | Same policy client + `require_non_redirect_success` |
| Ollama embed + health | `OllamaEmbedder` → `ollama.AsyncClient` | Ollama SDK | Policy validate at construct only (D5/O1); SDK residual documented |
| Metadata probe | `MetadataExtractor.check_connectivity` | Plain `httpx` | Policy client |
| Metadata LLM | `MetadataExtractor` → `openai.AsyncOpenAI` | Plain OpenAI/`httpx` | Inject safe async httpx |
| Graph LLM | `GraphExtractor` → `openai.AsyncOpenAI` | Plain OpenAI/`httpx` | Inject safe async httpx |
| Cloud API | `ApiClient` → `create_api_https_async_client` | `safe_http` API_HTTPS | Keep; gain dial fallback (D6) |
| `ApiClientExtended` | plain `httpx` | Not used from `main.py` | Out of scope |

### Confirmed dial bug

```273:280:2-Application/local-processing-service/src/security/safe_http.py
    def handle_request(self, request: httpx.Request) -> httpx.Response:
        ...
        dial_targets = _validated_targets(self._policy, original_host, addresses)
        return _send_request(request, dial_targets[0], original_host)
```

Same pattern at async lines 258–265. Existing tests cover single-target dial + Host preservation; they do **not** cover multi-address connect fallback.

### Resolved open question

- **D5 / Ollama:** User selected **O1** (2026-07-19). O2/O3 are out of scope for this plan.

---

## 4. Task list

| # | Phase | Component | Description | Skills | Deps |
|---|-------|-----------|-------------|--------|------|
| T1 | Red | `safe_http` dial contract | Failing tests in `5-Test/local-processing-service.Tests/test_safe_http.py`: (a) LOOPBACK — resolver returns `("::1", "127.0.0.1")`, first dial raises connect failure, second succeeds; Host + SNI preserved on success; (b) PUBLIC_HTTPS multi-A same connect-fallback rule; (c) mixed/unsafe DNS still rejected before any dial; (d) redirects still blocked; (e) all targets failing raises last connect error. | `test-dev` | — |
| T2 | Green | `_SafeTransport` / `_SafeAsyncTransport` | Ordered dial across all validated targets on connection-establishment errors only; close each failed pool; stop on first successful response start. Symbols: `handle_request`, `handle_async_request`, and/or a shared dial helper around `_send_request` / `_send_async_request`. Acceptance: T1 green. | `python-dev` | T1 |
| T3 | Red | Model-provider client contract | Tests covering: (a) `OpenAIEmbedder` construct + `check_status` reject non-policy endpoints and use injected/policy client (mock resolver/transport); (b) `MetadataExtractor` / `GraphExtractor` construct validates `GRAPH_EXTRACTION_ENDPOINT` and do not use bare `httpx.AsyncClient` for probe; (c) `OllamaEmbedder` construct rejects non-loopback HTTP / unsafe hosts per D5. Prefer new focused modules under `5-Test/local-processing-service.Tests/` if existing files are already large. | `test-dev` | — |
| T4 | Green | Shared model-provider factory | In `security/safe_http.py`: add `validate_model_provider_endpoint` (alias or rename of `validate_model_discovery_endpoint` with thin backward-compatible alias); add `create_model_provider_(async_)client(policy)` with thin aliases for existing `create_model_discovery_*` names so discovery call sites stay clear for CodeQL comments. Acceptance: existing discovery tests still import working names. | `python-dev` | — |
| T5 | Green | Wire OpenAI-compatible callers | `OpenAIEmbedder`, `MetadataExtractor`, `GraphExtractor`: validate endpoint at construct; build policy-bound async httpx via T4 factory; inject into `openai.AsyncOpenAI(http_client=...)` (confirm pinned SDK kwarg); replace plain probe clients with policy client + `require_non_redirect_success`. Acceptance: T3 green for OpenAI-compatible paths. | `python-dev` | T3, T4 |
| T6 | Green | Ollama O1 | `OllamaEmbedder.__init__` (and factory-supplied host path): validate host/URL with `validate_model_provider_endpoint` before creating `ollama.AsyncClient`. Add/adjust tests from T3(c). Do **not** wrap Ollama SDK traffic in `safe_http`. Acceptance: invalid hosts fail at construct; valid loopback/public-HTTPS (if ever used) pass validation; residual documented in T7. | `python-dev`, `test-dev` | T3, T4 |
| T7 | Docs | Canonical docs | Update `6-Docs/system/security.md` Communication section and `6-Docs/local-processing-service/architecture.md` (onboarding only if operator-facing localhost guidance needs it): one model-provider transport story, multi-address dial fallback, OpenAI-compatible coverage, **explicit Ollama SDK residual (D5)**. Plan closeout / archive per documentation standard after implementation verification. | `app-docs-standard` | T2, T5, T6 |
| T8 | Review | Quality + security | Code review + security review of dial fallback, OpenAI injection, Ollama construct-only validation; confirm no TLS weaken, no redirect follow, no private SSRF widening. | `code-review`, `security-review` | T2, T5, T6, T7 |

Plan-level acceptance:

- Unit: discovery against `localhost` succeeds when DNS is `::1` then `127.0.0.1` and only the IPv4 target accepts (fake resolver + connect failure on first target).
- OpenAI-compatible embed/graph/metadata paths use policy-bound clients; no new bare `httpx.AsyncClient` on those paths.
- Ollama: construct-time policy validation only; SDK residual documented.
- Existing API HTTPS + redirect-block tests remain green.
- Manual smoke: LM Studio on `127.0.0.1:1234` with config `http://localhost:1234/v1` for GRAPH + EMBEDDING — discovery and metadata probe both succeed without rewriting to `127.0.0.1`.

---

## 5. Sequencing / dependency graph

```
T1 → T2 ─────────────┐
T3 → T4 → T5 ─────────┼→ T7 → T8
         └→ T6 ───────┘
```

T1/T2 parallel with T3/T4. T5 and T6 both need T4; T5 also needs T3. T7 after T2+T5+T6. T8 last.

---

## 6. Residual decisions / risks

| Item | Owner / condition |
| --- | --- |
| No remaining plan-blocking user decisions | — |
| CodeQL `py/partial-ssrf` may still flag intentional operator URL probes | Security-review + narrow dismiss evidence (prior #405 posture) if needed after merge |
| OpenAI SDK must support httpx client injection kwarg used in T5 | Implementer verifies against pinned `openai` in `pyproject` during T5; fail the task if absent — do not weaken TLS as workaround |
| `ApiClientExtended` still uses plain httpx | Out of scope (not on `main.py` path); follow-up delete-or-harden if product reuses it |
| Ollama mid-flight DNS / redirects (D5 residual) | Accepted risk; documented in T7; future plan if operators need parity |

---

## 7. Out of scope

- Config rewrite `localhost` → `127.0.0.1` as the fix
- OS trust-store install / `verify=False` / custom TLS bypass
- Admin Desktop Refresh vs Start env / healthy-vs-degraded UX
- Replacing or wrapping Ollama SDK traffic (O2/O3)
- Hardening or deleting `ApiClientExtended` (separate cleanup if desired)
- Cloud API auth / MSAL changes

---

## 8. Required skills

`test-dev`, `python-dev`, `app-docs-standard`, `code-review`, `security-review`

---

## 9. Verification harness

1. **Unit:** T1 and T3 red then green via T2/T4/T5/T6; full non-integration `local-processing-service.Tests` suite green from `2-Application/local-processing-service/` with the suite’s documented pytest invocation.
2. **Manual smoke:** LM Studio IPv4-only + `localhost` URL for embedding discovery and metadata probe — both succeed.
3. **Reviews:** `code-review` APPROVED; `security-review` APPROVED (fallback does not weaken address policy).
4. **Docs closeout:** Canonical security + processor architecture match implementation; plan archived only after docs-dev plan-closeout per repo governance.
