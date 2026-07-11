# SkillForge Reference

SkillForge is a vendored .NET CLI/library used to validate, lint, scan, and routing-test agent skills. The local source is `7-Deployment/tools/SkillForge`.

## Upstream provenance

- Upstream project: [bonaniibm/SkillForge](https://github.com/bonaniibm/SkillForge)
- Upstream license: MIT; the vendored copy retains its own [LICENSE](../../7-Deployment/tools/SkillForge/LICENSE).
- Local provenance status: the checked-in copy does not retain a source commit. Before refreshing it, verify the upstream revision and record its commit SHA in the refresh pull request and the local README.

## Use

Treat skills as supply-chain artifacts. Run SkillForge validation, routing lint, and security scanning from the vendored source according to the repository workflow. Its diagnostics assist review but do not replace human security review.
