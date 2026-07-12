# Infrastructure Instructions

## Applies to

`7-Deployment/infrastructure/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [DevOps documentation](../../6-Docs/DevOps/README.md)
- [Azure environment](../../6-Docs/AzureEnvironment/agent-access.md)
- [Azure naming reference](../../6-Docs/reference/azure-naming-standards.md)

## Scoped constraints

- Implement declarative infrastructure with explicit resource names, managed identity/RBAC, Key Vault-backed secrets, and configuration-driven environments.
- `pulumi preview` is allowed after the root Azure guard rail. Local `pulumi up` and `pulumi destroy` are forbidden; deployment occurs only through GitHub Actions.
- Do not run Azure write commands, direct Docker builds/pushes, or ACR builds. Do not expose secrets in outputs, state, or logs.

## Verify

Run the applicable static checks and `pulumi preview` only when authorized by the root Azure guard rail.
