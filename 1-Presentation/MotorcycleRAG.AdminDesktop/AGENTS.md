# Admin Desktop Instructions

## Applies to

`1-Presentation/MotorcycleRAG.AdminDesktop/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Admin Desktop onboarding](../../6-Docs/MotorcycleRAG.AdminDesktop/onboarding.md)
- [Admin Desktop architecture](../../6-Docs/MotorcycleRAG.AdminDesktop/architecture.md)
- [Admin Desktop requirements](../../6-Docs/MotorcycleRAG.AdminDesktop/requirements.md)

## Scoped constraints

- This Tauri v2 application hosts the local processor and provides administrative UI. The cloud API and local processor are distinct backends.
- Preserve the established React/TypeScript, Rust, Zustand, TanStack Query, and Tailwind patterns. Use Rust commands for local-processor HTTP; do not call the processor directly from the webview.
- Treat browser/client access tokens, persisted configuration, and processor secrets as sensitive. Follow the root security rules.

## Task-specific guides

Read only the guide that matches the change, in addition to the canonical Admin Desktop documentation:

| Change | Required guide |
| --- | --- |
| Sign-in, refresh, keyring, or Chrome profile behavior | [Authentication](../../6-Docs/MotorcycleRAG.AdminDesktop/authentication.md) |
| API client, upload timeout, or multipart behavior | [Cloud API client](../../6-Docs/MotorcycleRAG.AdminDesktop/api-client.md) |
| Stored settings or defaults | [Configuration](../../6-Docs/MotorcycleRAG.AdminDesktop/configuration.md) |
| Processor lifecycle, command bridge, or runtime variables | [Local processor integration](../../6-Docs/local-processing-service/local-processor.md) |
| Screen/API contract changes | [Screen-to-endpoint map](../../6-Docs/MotorcycleRAG.AdminDesktop/endpoint-map.md) |
| Component layout or visual conventions | [UI conventions](../../6-Docs/MotorcycleRAG.AdminDesktop/ui-conventions.md) |
| File ownership and location | [Project layout](../../6-Docs/MotorcycleRAG.AdminDesktop/project-layout.md) |

[Pending work](../../6-Docs/archive/plans/admin-desktop-pending-work.md) is planning/status context only. It is not implementation authority; verify planned or historical claims in source and canonical documentation.

## Verify

Run `npx tsc --noEmit`, `npm test`, and the relevant Rust check or test.
