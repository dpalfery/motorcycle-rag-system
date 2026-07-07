# Admin Desktop — Pending Work

| Task | Status | Notes |
|---|---|---|
| Entra PKCE auth | Implemented; untested end-to-end | Needs Entra app reg redirect URI `http://localhost` added |
| All screens | Implemented | DTOs are best-guess; adjust field names to match actual API responses |
| PyInstaller sidecar | Not started | Replace `python3 -m uvicorn` spawn in `processor_start` with `externalBin` |
| Tests | Not started | Vitest + RTL for view logic; `cargo test` for Rust commands |
| Retire MAUI project | Completed | Removed `MotorcycleRAG.Admin` + `MotorcycleRAG.Admin.Tests` from solution |
