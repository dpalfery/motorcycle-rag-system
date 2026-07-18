# Database Setup CLI

The Database Setup CLI provisions a local development SQL database and schema. Its source-root [README](../../7-Deployment/DbSetup/README.md) is the canonical command reference.

The overall schema deployment strategy — including the canonical `schema.sql` file, idempotency patterns, the CI/CD `sqlcmd` path, and known limitations — is documented in the [Database Schema Deployment](database-schema.md) document.

Use it only with a local or explicitly approved development database. Supply passwords through secure prompts or environment/secret mechanisms; never add real values to documentation, scripts, or issue reports.

## Internal architecture

`Program` is the CLI composition root and wires the CLI-local `SqlDbSetupConnectionFactory` into the provisioner. That factory is the sole production owner of `SqlConnection` construction; preflight, provisioning, and schema operations receive connections through the injected factory. The boundary keeps command behavior unchanged while permitting unit tests to substitute fake connections without a SQL Server.

### Secure SQL transport

`SqlDbSetupConnectionFactory` normalizes every connection string through `SqlConnectionStringBuilder` and forces:

- `Encrypt = Mandatory`
- `TrustServerCertificate = false`

Certificate-trust bypass is not part of the production factory. The only approved exception is an explicit local negative test that proves insecure transport is rejected.

### Trusted script catalog

`SqlScriptExecutor` executes only the approved catalog scripts:

| Script enum | Canonical relative path |
| --- | --- |
| `DbSetupScript.Schema` | `4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql` |
| `DbSetupScript.TestData` | `7-Deployment/DbSetup/sql/test-data.sql` |

Candidates are resolved under the configured catalog root with containment checks. Paths that escape the root or contain symbolic links are rejected before a connection is opened. Arbitrary caller-supplied script paths are not accepted. System-wide SQL and logging rules are in [security directives](../system/security.md).

## Verification

Run the isolated CLI boundary tests with:

```bash
dotnet test 5-Test/MotorcycleRAG.DbSetup.Tests/MotorcycleRAG.DbSetup.Tests.csproj --no-restore
```
