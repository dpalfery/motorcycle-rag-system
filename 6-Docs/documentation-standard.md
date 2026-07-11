# MotorcycleRAG Documentation Standard

## Purpose and scope

This is the authoritative standard for human- and agent-authored documentation in MotorcycleRAG. It applies to every maintained application, service, library, deployment tool, operational procedure, and public repository surface.

The repository uses Markdown as documentation-as-code. The root [README](../README.md) is the public entry point, and [catalog.md](catalog.md) is the complete component inventory.

## Locations and canonical sources

- A component README at its source root is a concise overview: purpose, boundaries, primary entry points, and links to its detailed documentation.
- Detailed documentation belongs in `6-Docs/`. Each application or runnable service has a dedicated `6-Docs/<component>/` folder containing `onboarding.md`, `architecture.md`, and `requirements.md`.
- System-wide documents belong in `6-Docs/system/` and use the same onboarding, architecture, and requirements layout.
- Deployment procedures belong in `6-Docs/deployment/`; operating a deployed system belongs in `6-Docs/operations/`; reusable configuration and technical reference belongs in `6-Docs/reference/`.
- Plans are working documents in `6-Docs/plans/`; superseded or historical material belongs in `6-Docs/archive/` and must be visibly non-authoritative.
- GitHub-discovered community files remain at the repository root or under `.github/`: `LICENSE`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`, issue forms, pull-request templates, and `CODEOWNERS`.

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

### Coverage

Every runnable application, service, CLI, deployment tool, and public integration SHALL have a catalog entry, a source-root README, and a detailed documentation path. Shared libraries and test projects SHALL be represented in the catalog and linked to their canonical architectural or API documentation; document public contracts and non-obvious decisions, not every private class.

## Ownership, lifecycle, and review

- Each catalog entry has an owner, documentation status, and last-reviewed date.
- The catalog is the authoritative ownership and review-date record for active system, application, deployment, and operations documentation; update it when material changes occur.
- Feature plans SHALL be reviewed when work completes, then retained as active, superseded, or archived. Archived documentation must never be followed as current guidance.
- Documentation changes are required when a component's public interface, configuration, architecture, supported runtime, operations, or user workflow changes.

## Writing, safety, and links

- Write for a defined reader and task. Prefer short headings, prerequisite-first instructions, stable relative links, and commands that are safe to copy.
- Never include credentials, tokens, passwords, private endpoints, customer data, or unsafe direct deployment commands. Use placeholders and link to the approved workflow.
- State facts verified from source. Label proposals, historical content, and environment-specific examples clearly.
- Check internal links whenever files move or a README changes. Use descriptive link text rather than raw URLs where practical.

## Agent workflow

Before changing code or documentation, an agent SHALL read this standard and the catalog, identify the affected components, and inspect their existing README and detailed documentation. After the change, the agent SHALL update the relevant canonical documentation and catalog entry, or explain why no documentation impact exists.

## Validation

Pull requests that change documentation or a cataloged component SHALL pass Markdown linting, internal-link validation, catalog/required-document validation, and secret scanning. Reviewers SHALL verify that the root README and `6-Docs/README.md` retain a navigable path to the affected content.

Legacy reference documents are brought into the Markdown lint scope when they are materially revised. Until then, link and secret checks still apply.
