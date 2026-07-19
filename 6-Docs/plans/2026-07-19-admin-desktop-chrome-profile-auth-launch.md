# Admin Desktop — Chrome profile enumeration and auth browser launch (macOS)

**Status:** Ready  
**Date:** 2026-07-19  
**Goal:** Fix Admin Desktop so Chrome profiles enumerate correctly on macOS and Login reliably opens the Entra auth page in the selected Chrome profile.

---

## 1. Problem / Motivation

**Symptoms (macOS):**

1. Sign-in Chrome profile dropdown shows only one profile (typically `Default`), despite the user having ~5+ Chrome profiles.
2. Selecting that profile and clicking Login does not open the Entra auth page.

**Root-cause chain (verified against live source + local probe):**

### Bug A — Only one profile in dropdown

1. `auth_list_chrome_profiles` → `list_chrome_profiles()` (`src-tauri/src/auth.rs`) resolves macOS path `~/Library/Application Support/Google/Chrome/Local State` and parses `profile.info_cache`.
2. On **any** failure (path missing, read `Err`, JSON parse `Err`, missing/non-object `info_cache`), the function returns a **synthetic** single-element list: `{ directory: "Default", name: "Default" }` with **no error surfaced** to the UI.
3. `SignInScreen` always renders that list as a real dropdown, so a silent enumeration failure is indistinguishable from “Chrome has only Default”.
4. Live probe on the reporter’s machine: `Local State` **exists** under the documented macOS path, but this agent process received `PermissionError: Operation not permitted` (EPERM) when reading it. That matches the silent-fallback path exactly. The Tauri app process must be verified separately (Cursor TCC ≠ app TCC), but the product bug remains: failures must not be collapsed into a fake “one profile” UI.
5. Existing unit test `test_chrome_local_state_parsing_structure` only asserts an inline JSON sample; it does **not** exercise `list_chrome_profiles()` against a fixture file / injectable path. `test_default_profile_when_no_chrome` asserts the fallback behavior and would pass even when enumeration is broken.

### Bug B — Auth page does not open on Login

1. `SignInScreen.handleSignIn` always calls `signIn(selectedProfile)` with a non-empty directory string (defaults to `"Default"`), so `sign_in` always takes the Chrome branch (`open_browser`), never the system-default-browser branch.
2. macOS `open_browser` runs:
   `open -a "Google Chrome" --args --profile-directory={dir} {url}`  
   **without** `-n` / `-na`.
3. On macOS, `open … --args` is ignored when Chrome is already running (common case). `Command::spawn` still succeeds, so Rust proceeds to wait on the loopback listener while **no auth tab appears**.
4. Frontend catch treats non-`Error` Tauri invoke failures as the generic string `"Sign-in failed."`, hiding the Rust error text (timeout / launch / CSRF / token exchange).

These two bugs are related in UX (both appear on the sign-in screen) but have distinct code roots: silent enumeration fallback vs broken macOS Chrome launch.

---

## 2. Approved decisions

| ID | Decision | Chosen approach |
|----|----------|-----------------|
| D1 | Chrome channel in scope | **Google Chrome stable only** (`…/Google/Chrome/Local State` and `/Applications/Google Chrome.app`). Chrome Beta/Canary/Chromium/Edge/Arc are out of scope. |
| D2 | Enumeration failure UX | Do **not** silently present a fake sole `Default` as if discovery succeeded. When Local State is readable, return all `info_cache` profiles. When unreadable/unparseable, show explicit “Could not read Chrome profiles” (or equivalent) and offer **System default browser** sign-in (`profileDirectory: null`). Do not invent FDA as a blocking prerequisite; escape hatch is sufficient. |
| D3 | macOS Chrome launch | Prefer launching the Chrome **binary** at `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome` with `--profile-directory={dir}` and the auth URL. If the binary is missing, fall back to `open -na "Google Chrome" --args --profile-directory={dir} {url}` (`-n` required). Do not keep the current `open -a` without `-n`. |
| D4 | Windows/Linux launch | Leave Windows/Linux command shapes as-is unless a regression is found while touching shared helpers; add shared tests around argument construction where practical. |
| D5 | Testability | Extract pure parse helper (`parse_chrome_profiles_from_local_state(json)`) and path resolution; unit-test multi-profile fixtures without depending on the host Chrome install. Integration/manual check on macOS with Chrome already running. |
| D6 | Docs | Update `6-Docs/MotorcycleRAG.AdminDesktop/authentication.md` Chrome Profile Selection / launch notes for macOS binary/`-na` behavior and failure UX. |

**User lock-in (2026-07-19):** Finalize Draft → Ready; Chrome stable only; proceed without additional FDA guidance beyond D2 escape hatch.

---

## 3. Investigation findings

| Fact | Evidence |
|------|----------|
| Path for macOS Local State is correct | `chrome_local_state_path()` joins `HOME` + `Library/Application Support/Google/Chrome/Local State` |
| Silent single-profile fallback | `list_chrome_profiles()` returns `vec![default_chrome_profile()]` on all error arms |
| UI cannot tell fallback from real Default | `SignInScreen` maps `profiles` directly into `<select>` |
| Auth always uses Chrome path when a selection exists | `signIn(selectedProfile)` never passes `undefined`/`null` |
| macOS launch omits `-n` | `open_browser` macOS cfg block |
| Parse unit test does not cover production function | `test_chrome_local_state_parsing_structure` uses local `json!` only |
| No covering tests for `open_browser` | CodeGraph blast-radius note |
| Canonical auth docs describe intended flow | `6-Docs/MotorcycleRAG.AdminDesktop/authentication.md` |
| Scoped instructions | `1-Presentation/MotorcycleRAG.AdminDesktop/AGENTS.md` |
| Identifier typo history (`com.palfery` vs `com.palfrey`) | Unrelated to browser open; do not expand scope |

**Resolved open questions**

- Is the macOS Local State path wrong? **No** — path matches Chrome’s documented location; failure is read/parse/fallback behavior and/or process permission, not wrong folder spelling.
- Is the dropdown filtering client-side to one item? **No** — UI renders whatever Rust returns.
- Expand to Beta/Canary? **No** — D1 Chrome stable only.
- Require FDA before shipping? **No** — D2 explicit failure UX + system default browser escape hatch.

---

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| T1 | Test contract | Rust auth | Add failing tests first: (a) `parse_chrome_profiles_from_local_state` with fixture containing Default + Profile 1..N returns N+1 sorted profiles; (b) missing/invalid JSON returns empty or explicit error type (not a silent fake success); (c) macOS launch argv builder includes Chrome binary path + `--profile-directory` + URL, or `open -na` fallback args. Files: `src-tauri/src/auth.rs` tests. | `test-dev`, `tauri-dev` |
| T2 | Rust | Profile enumeration | Refactor `list_chrome_profiles` / path helpers per D2/D5: injectable path or pure parse; preserve sort (Default first); return diagnostic on I/O failure; stop presenting silent fake-only-Default as successful discovery. Symbols: `chrome_local_state_path`, `list_chrome_profiles`, `default_chrome_profile`; command `auth_list_chrome_profiles` in `lib.rs` if return shape changes. | `tauri-dev` |
| T3 | Rust | macOS `open_browser` | Implement D3 binary-first launch; keep `spawn` error mapping clear (`failed to open Chrome on macOS: …`). Symbol: `open_browser` in `auth.rs`. | `tauri-dev` |
| T4 | React | SignInScreen / auth UX | Surface enumeration failure; add **System default browser** option (pass `null`/`undefined` to `auth_sign_in`); fix invoke error extraction so Tauri string errors display. Files: `SignInScreen.tsx`, `auth.ts` as needed. | `react-dev` |
| T5 | Verify | Unit + manual | `cargo test` auth module; `npm test` / `npx tsc --noEmit` as applicable; manual macOS: Chrome already running + ≥2 real profiles → dropdown lists them; Login opens Entra URL in selected profile; default-browser path still works. | `test-dev`, `tauri-dev`, `react-dev` |
| T6 | Docs | Canonical auth doc | Update `6-Docs/MotorcycleRAG.AdminDesktop/authentication.md` for enumeration failure UX and macOS launch command. | `app-docs-standard` |
| T7 | Review | Quality gates | Code review + security review of auth launch / path handling (no command injection via profile directory; validate profile directory as Chrome profile folder name pattern). | `code-review`, `security-review` |

### Acceptance criteria / test contract

**AC1 — Enumeration**

- Given a Local State fixture with 5+ `info_cache` entries, `list_chrome_profiles` (or pure parse helper) returns all of them with correct `directory` / `name` / `userName`.
- Given unreadable/missing Local State, UI does **not** look like a successful single-profile discovery; user can still sign in via system default browser.

**AC2 — Launch (macOS)**

- With Google Chrome already running, Login with a selected profile opens the Entra authorize URL (loopback redirect completes or at least the auth page is visible).
- Launch failure returns an actionable Rust error string visible in the SignInScreen error banner.

**AC3 — Regression**

- Windows/Linux profile path resolution unchanged in behavior.
- Existing keyring / PKCE / restore / refresh flows untouched except shared helpers.

---

## 5. Sequencing / dependency graph

```text
T1 (failing tests)
  → T2 (enumeration) + T3 (macOS launch)   [can parallelize after T1 shapes APIs]
  → T4 (SignInScreen UX / error strings)   [depends on T2 return shape]
  → T5 (automated + manual verify)
  → T6 (docs)
  → T7 (reviews)
```

---

## 6. Residual decisions / risks

| Item | Owner / condition |
|------|-------------------|
| App process may still get EPERM reading Chrome Local State on some Macs | Implementer notes outcome in T5; product response is D2 escape hatch (no FDA blocker) |
| Chrome installed under non-default path | Binary missing → `-na` fallback; both fail → clear error |
| Multi-browser support (Edge/Brave) | Out of scope (D1); open follow-up if needed |

---

## 7. Out of scope

- Entra app registration / redirect URI changes (already documented).
- Keyring service name typo (`com.palfery` vs `com.palfrey`) unless it blocks sign-in after browser opens.
- MSAL adoption; auth stack remains `oauth2` + loopback PKCE.
- Chrome Beta/Canary/Chromium/Edge profile enumeration.
- Changing CSP / Tauri capabilities unless a concrete permission error requires it (profile read uses Rust `std::fs`, not the webview FS API).
- Full Disk Access documentation as a shipping gate.

---

## 8. Required skills

- `tauri-dev` (Rust commands, macOS process launch, Chrome Local State)
- `react-dev` (SignInScreen, error UX, default-browser option)
- `test-dev` (Rust unit fixtures; frontend tests if SignInScreen coverage is added)
- `app-docs-standard` (authentication.md)
- `code-review`
- `security-review`

---

## 9. Verification harness

| Gate | Expectation |
|------|-------------|
| Unit tests | New parse/launch-argv tests green; existing auth module tests still pass |
| Typecheck | `npx tsc --noEmit` in Admin Desktop |
| Manual macOS | Multi-profile dropdown + auth page with Chrome already running |
| `code-review` | Approve / changes-requested on branch diff |
| `security-review` | Profile directory not shell-interpolated unsafely; no secret leakage in new logs |
| Docs closeout | authentication.md matches implemented behavior; plan index updated on archive |

---

## Primary files

- `1-Presentation/MotorcycleRAG.AdminDesktop/src-tauri/src/auth.rs` — `chrome_local_state_path`, `list_chrome_profiles`, `open_browser`, `sign_in`
- `1-Presentation/MotorcycleRAG.AdminDesktop/src-tauri/src/lib.rs` — `auth_list_chrome_profiles`, `auth_sign_in`
- `1-Presentation/MotorcycleRAG.AdminDesktop/src/screens/SignInScreen.tsx`
- `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/auth.ts`
- `6-Docs/MotorcycleRAG.AdminDesktop/authentication.md`
