---
id: specs/index
title: Specification Index
doc-type: index
status: current
owner: Maintainers
last-reviewed: 2026-07-22
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Specification Index

This is the authoritative inventory for feature specifications under `6-Docs/specs/`. A specification is a three-document set — `requirements.md`, `design.md`, `tasks.md` — produced by the product-owner planning flow before implementation begins.

Like a plan, a specification is **work in progress, not canonical guidance**. It records what was intended, and it goes stale the moment the implementation diverges from it. Once its tasks are delivered and verified, the durable content belongs in canonical documentation and the specification itself belongs in `6-Docs/archive/specs/`. Agents SHALL NOT cite an archived specification as current behaviour.

Agents SHALL read this index before opening a specification, and SHALL open only one listed here as `Draft`, `Ready`, `In progress`, or `Blocked`.

## Status vocabulary

The same statuses as plans, for the same reasons:

- `Draft` — being authored; not approved for implementation.
- `Ready` — approved and ready for implementation.
- `In progress` — implementation is underway.
- `Blocked` — work cannot proceed; the blocker must be stated in the specification.
- `Review required` — implementation is claimed complete but not verified. Agents must not act on it.
- `Completed` — implementation, tests, and documentation are verified; archive it promptly.
- `Superseded` — replaced by a named specification or canonical document; archive it promptly.
- `Archived` — historical only; this status is used in `6-Docs/archive/specs/`.

## Active inventory

| Specification | Status | Goal |
| --- | --- | --- |
| _None active._ | — | — |

## Archive register

Completed and superseded specifications live in [`../archive/specs/`](../archive/specs/). Add an entry here when archiving: title, archive date, outcome, and the canonical documentation that replaced its content.

| Specification | Archived | Outcome / canonical guidance |
| --- | --- | --- |
| [Admin Desktop local processor bootstrap](../archive/specs/admin-desktop-local-processor-bootstrap/) | 2026-07-22 | Archived during the specification-lifecycle introduction. Outcome and replacing documentation not yet recorded; reconstruct from the component's canonical documentation before citing. |
| [Ingestion job delete timeout](../archive/specs/ingestion-job-delete-timeout/) | 2026-07-22 | Archived during the specification-lifecycle introduction. Outcome and replacing documentation not yet recorded; reconstruct from the component's canonical documentation before citing. |
