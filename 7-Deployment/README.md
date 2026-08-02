# MotorcycleRAG Azure Environment

`7-Deployment/` contains the infrastructure source, deployment assets, database setup tool, and vendored developer tooling used to run MotorcycleRAG in Azure.

## Components

- `infrastructure/` — declarative Pulumi infrastructure source.
- `DbSetup/` — local development database setup CLI.
- `tools/codegraph/` — vendored, pinned CodeGraph CLI (`@colbymchenry/codegraph@1.5.0`); builds the `.codegraph/` knowledge-graph index.
- `tools/KyberWeave/` — stub pointing at the extracted product ([dpalfery/kyber-weave](https://github.com/dpalfery/kyber-weave)); host overrides live in root `kyber-weave.yml`.
- `tools/tokenomics/` — dev-tooling bootstrap (`setup-tokenomics.sh`): installs CodeGraph, builds/syncs the index, and verifies RTK and Kyber-Weave on PATH.

## Operating model

GitHub Actions is the only approved path for infrastructure provisioning and application deployment. Local work may inspect, test, or preview supported configuration, but must not run direct cloud provisioning, image publication, or application deployment commands.

## Documentation

- [Azure Environment onboarding](../6-Docs/AzureEnvironment/onboarding.md)
- [Azure Environment architecture](../6-Docs/AzureEnvironment/architecture.md)
- [DevOps and tool references](../6-Docs/DevOps/)
- [Component catalog](../6-Docs/catalog.md)
