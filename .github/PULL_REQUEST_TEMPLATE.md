## Summary

<!-- Describe the problem being solved and the outcome. Explain the "why" more than the "how." -->

## Type of Change

<!-- Select exactly one. If more than one applies, pick the most impactful and note others in the summary. -->

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Refactor (code change that neither fixes a bug nor adds a feature)
- [ ] Documentation update

## Component and boundaries

<!-- Identify the owning component in 6-Docs/catalog.md and confirm scoped instructions were followed. -->

- [ ] I identified the owning component in `6-Docs/catalog.md`.
- [ ] I followed the relevant scoped `AGENTS.md` instructions and Clean Architecture boundaries.
- [ ] I verified that inner layers do not depend on outer layers.

## Related Issues

<!-- Link to GitHub issues this PR addresses. Use "Closes #n" to auto-close on merge, or "Part of #n" for partial progress. -->

## Validation

<!-- List focused build, test, and manual validation performed. Be specific: which suites, what environments, any new tests added. -->

- [ ] Build succeeds (`dotnet build --configuration Release` / platform equivalent).
- [ ] Relevant test suites pass locally (unit, integration, E2E as applicable).
- [ ] Markdown and code linting pass.
- [ ] New or updated tests cover the change (describe coverage below).

## Documentation impact

<!-- Documentation updates are required when public interface, configuration, architecture, runtime, operations, or workflow changes. See 6-Docs/documentation-standard.md. -->

- [ ] I updated the affected README, canonical documentation (under `6-Docs/`), and catalog entry.
- [ ] No documentation update is needed because:

<!-- Explain why: e.g. "Internal refactor with no public interface change" or "Bug fix for existing behavior." -->

## Security and operations

- [ ] This change contains no credentials, tokens, connection strings, passwords, customer data, or `.env` files.
- [ ] I documented any new configuration, deployment, or operational impact in the relevant `6-Docs/` documentation.
- [ ] I have reviewed the CodeQL, Trivy, Semgrep, and Checkov findings (if any) and addressed or annotated HIGH/CRITICAL items.

---

<!-- For detailed guidance on filling out this template, see .agents/skills/create-pull-request/SKILL.md -->
