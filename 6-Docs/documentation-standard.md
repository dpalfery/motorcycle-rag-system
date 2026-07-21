---
id: system/documentation-standard
title: MotorcycleRAG Documentation Standard
doc-type: governance
status: current
owner: Maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# MotorcycleRAG Documentation Standard

## Purpose and scope

This is the authoritative standard for human- and agent-authored documentation in MotorcycleRAG. It applies to every maintained application, service, library, deployment tool, operational procedure, and public repository surface.

The repository uses Markdown as documentation-as-code. The root [README](../README.md) is the public entry point, and [catalog.md](catalog.md) is the complete component inventory.

## Locations and canonical sources

- A component README at its source root is a concise overview: purpose, boundaries, primary entry points, and links to its detailed documentation.
- Detailed documentation belongs in `6-Docs/`. Each application or runnable service has a dedicated `6-Docs/<component>/` folder containing `onboarding.md`, `architecture.md`, and `requirements.md`.
- System-wide documents belong in `6-Docs/system/` and use the same onboarding, architecture, and requirements layout.
- Deployment procedures belong in `6-Docs/DevOps/`; operating a deployed system belongs in `6-Docs/operations/`; reusable configuration and technical reference belongs in `6-Docs/reference/`.
- Directory placement expresses the document's *purpose*, not its subject. The component a document describes is carried by the `component` frontmatter key defined in the [documentation ontology](documentation-ontology.md). A component-specific runbook therefore lives in `6-Docs/operations/` and names its component in frontmatter; do not infer ownership from the folder.
- `6-Docs/` contains canonical documentation only. Scratch notes, vendored packages, and git-ignored working files SHALL NOT live anywhere beneath it. Agent scratch output belongs in the path declared as **Agent Scratchpad** in the root `AGENTS.md` Config Registry.
- Plans are working documents in `6-Docs/plans/`; its [plan index](plans/README.md) is the authoritative inventory and lifecycle record. Superseded or historical material belongs in `6-Docs/archive/` and must be visibly non-authoritative.
- GitHub-discovered community files remain at the repository root or under `.github/`: `LICENSE`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`, issue forms, pull-request templates, and `CODEOWNERS`.
- `REVIEW.md` remains at the repository root. This is a tooling exception, not a component doc: Anthropic's Code Review (the managed GitHub App and the local `/code-review` command) only auto-discovers review-customization instructions at that exact path, and its contents are injected verbatim, so `@`-imports and links to other files do not resolve. Keep it review-only — general project context stays in `AGENTS.md` and `6-Docs/`. Do not move or duplicate it into `6-Docs/`; `6-Docs/review-guidelines.md` is a pointer to it for exactly this reason.

Do not create a second canonical document for a topic. Link to the established source instead.

## Required content

### Root README

The root README SHALL state the project purpose, supported status, concise onboarding path, maintained component map, documentation entry point, and community links. It SHALL link each maintained component README, but SHALL not become a recursive file listing.

### Component README

A maintained component README SHALL state its purpose, boundary, primary technology/entry point, and links to detailed documentation. It SHALL not duplicate setup, architecture, or requirements maintained in `6-Docs`.

### Detailed application documentation

Each application or runnable service documentation folder SHALL contain:

1. `onboarding.md` — dependencies, setup, debug path, and non-standard operating procedures.
2. `architecture.md` — overview, architecture, components and interfaces, data models, error handling, and testing strategy. Use Mermaid only where it clarifies a relationship.
3. `requirements.md` — introduction plus numbered requirements, each with one user story and EARS acceptance criteria.

### Frontmatter

Every document in scope of the [documentation ontology](documentation-ontology.md) SHALL begin with a YAML frontmatter block conforming to that schema. The ontology is authoritative for the key set, the closed vocabularies, and the required-key matrix; this standard does not restate them.

Three rules are load-bearing:

1. **`id` is permanent.** It is assigned once and never changed, so graph edges survive file moves and renames.
2. **`code-refs` and `api-endpoints` values are taken from the CodeGraph index, never hand-written.** A value that does not resolve is entity drift, and it fails the build.
3. **A fact carried in frontmatter is not repeated in the body.** Remove the legacy `**Component:**` / `**Status:**` / `**Date:**` bold key-value lines when adding frontmatter that supersedes them.

Frontmatter conformance and code-entity resolution are enforced in CI; see [Validation](#validation).

### Coverage

Every runnable application, service, CLI, deployment tool, and public integration SHALL have a catalog entry, a source-root README, and a detailed documentation path. Shared libraries and test projects SHALL be represented in the catalog and linked to their canonical architectural or API documentation; document public contracts and non-obvious decisions, not every private class.

## Ownership, lifecycle, and review

- Each catalog entry has an owner, documentation status, and last-reviewed date.
- The catalog is the authoritative ownership and review-date record for active system, application, deployment, and operations documentation; update it when material changes occur.
- Feature plans SHALL use the lifecycle in [the plan index](plans/README.md). A completed plan is reviewed against the implemented behavior and its canonical documentation before it is archived. A plan that has not received that review is never implicitly considered complete.
- Archived documentation must never be followed as current guidance.
- Documentation changes are required when a component's public interface, configuration, architecture, supported runtime, operations, or user workflow changes.

## Writing, safety, and links

- Write for a defined reader and task. Prefer short headings, prerequisite-first instructions, stable relative links, and commands that are safe to copy.
- Never include credentials, tokens, passwords, private endpoints, customer data, or unsafe direct deployment commands. Use placeholders and link to the approved workflow.
- State facts verified from source. Label proposals, historical content, and environment-specific examples clearly.
- Check internal links whenever files move or a README changes. Use descriptive link text rather than raw URLs where practical.

## Plan lifecycle and agent workflow

The plan index is the only entry point for agent work on plans. Agents SHALL read the index before opening a plan and SHALL open only the plan selected by the task and listed there as `Draft`, `Ready`, `In progress`, or `Blocked`. `Draft` supports planning work only; implementation requires a `Ready`, `In progress`, or `Blocked` plan. `Review required`, `Completed`, `Superseded`, and `Archived` plans are historical records and are not implementation authority.

New plans SHALL contain `Status`, `Date`, and `Goal` fields directly below the title, and their status SHALL be kept in sync with the index. Use only these statuses:

- `Draft` — being prepared; not approved for implementation.
- `Ready` — approved and ready for implementation.
- `In progress` — implementation is underway.
- `Blocked` — work cannot proceed; the blocker must be stated in the plan.
- `Review required` — temporary migration state; implementation completion has not been verified. Agents must not act on it.
- `Completed` — implementation and documentation are verified; archive it promptly.
- `Superseded` — replaced by a named plan or canonical document; archive it promptly.
- `Archived` — historical only; this status is used in `6-Docs/archive/`.

When implementation completes, the owner SHALL: verify the plan's acceptance criteria, update the affected canonical documentation, add the implementation reference and archive date to the plan index, move the plan to `6-Docs/archive/plans/`, and change its status to `Archived`. Do not archive a plan merely because its Markdown was finalized.

## Agent workflow

Before changing code or documentation, an agent SHALL read this standard and the catalog, identify the affected components, and inspect their existing README and detailed documentation. If a task refers to a plan, the agent SHALL also read the plan index and follow the lifecycle above. After the change, the agent SHALL update the relevant canonical documentation and catalog entry, or explain why no documentation impact exists.

## Validation

Pull requests that change documentation or a cataloged component SHALL pass Markdown linting, internal-link validation, catalog/required-document validation, frontmatter schema validation, code-entity drift validation, and secret scanning.

Frontmatter validation runs in two tiers:

| Tier | Command | Rule codes | Runs when |
| --- | --- | --- | --- |
| Schema | `skillforge docs validate` | `SF-DOC-SPEC-001`–`006` | documentation changes |
| Drift | `skillforge docs drift` | `SF-DOC-DRIFT-001`–`003` | code or documentation changes |

The drift tier resolves every `code-refs` and `api-endpoints` value against `.codegraph/codegraph.db`. A renamed or deleted symbol that leaves a dangling documentation reference fails the pull request.
 Reviewers SHALL verify that the root README and `6-Docs/README.md` retain a navigable path to the affected content.

Legacy reference documents are brought into the Markdown lint scope when they are materially revised. Until then, link and secret checks still apply.
