# MotorcycleRAG Azure Environment

`7-Deployment/` contains the infrastructure source, deployment assets, database setup tool, and vendored developer tooling used to run MotorcycleRAG in Azure.

## Components

- `infrastructure/` — declarative Pulumi infrastructure source.
- `DbSetup/` — local development database setup CLI.
- `tools/SkillForge/` — vendored .NET skill-validation tool.

## Operating model

GitHub Actions is the only approved path for infrastructure provisioning and application deployment. Local work may inspect, test, or preview supported configuration, but must not run direct cloud provisioning, image publication, or application deployment commands.

## Documentation

- [Azure Environment onboarding](../6-Docs/AzureEnvironment/onboarding.md)
- [Azure Environment architecture](../6-Docs/AzureEnvironment/architecture.md)
- [DevOps and tool references](../6-Docs/DevOps/)
- [Component catalog](../6-Docs/catalog.md)
