# Agent Context: MotorcycleRAG.Core (0-Base / Shared Kernel)

This file is **supplemental** to the root `AGENTS.md`. It exists to help agents make good decisions in the **0-Base** layer without re-stating the entire repo rules.

## What to read first (authoritative)
- Root rules: `AGENTS.md` (especially: working agreement, dependency rule, model vs DTO, secrets)
- System baseline: `specs/001-system-spec/spec.md` (security + clean architecture expectations)
- Security checklist: `specs/001-system-spec/checklists/asvs-v5-level2.md`
- Configuration rules: `6-Docs/environment-variables.md`

## What `MotorcycleRAG.Core` is for (in this repo)
Use Core for **low-level, framework-free** building blocks used broadly across layers.

Good fits here:
- Deterministic utilities (string/time/id helpers)
- Small primitives (results/errors, minimal exceptions)
- Non-business constants/enums that are stable and widely shared

Not a fit here:
- Anything that encodes business rules/invariants (belongs in `3-Domain`)
- Any I/O (HTTP/DB/files/Azure SDKs/Semantic Kernel)
- Anything that forces extra packages onto consumers

## Dependency boundaries (strict)
- Allowed: .NET BCL (`System.*`) and other 0-Base projects.
- Forbidden: Presentation/Application/Domain/Persistence references and any infrastructure SDKs.

## Security and logging gotchas
- Don’t add helpers that make it easy to log raw user input (queries/prompts/PII). If you must support sanitization, align with root logging rules (replace newlines/tabs before logging; structured logging only).
- Don’t introduce “config” helpers that read secrets from files. If a value might be secret, design a small option/abstraction and let outer layers populate it from environment variables.

## Quick decision test
If the change is not obviously reusable across **API + BFF + MAUI + Persistence** without pulling new dependencies, it probably does **not** belong in Core.
