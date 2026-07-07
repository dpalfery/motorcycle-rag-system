# Database Setup

Use the existing project database setup path. Do not create new compose files or database scripts.

## Local SQL Server

The repo includes `docker-compose.yml` with a SQL Server service. Ask before starting it because this creates/starts a local container.

Preferred startup:

```sh
docker compose up -d sqlserver
docker ps
```

Do not run `docker build`, `docker push`, `az acr build`, or any image publishing command.

## Provision Schema and App User

Use the existing .NET setup CLI:

```sh
cd 7-Deployment/DbSetup/MotorcycleRAG.DbSetup
dotnet run -- --env-vars-in-proc
```

Interactive mode is preferred for local development because it prompts for the SQL Server SA password and avoids placing secrets in command history. Use `--env-vars-in-proc` by default so the CLI does not persist database passwords or connection strings as user-level environment variables.

The CLI:

- creates the database
- creates the application login/user
- deploys `4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql`
- optionally seeds test data
- sets process-level environment variables when invoked with `--env-vars-in-proc`

Ask separately before `--seed-test-data`.

The CLI's default behavior persists user-level environment variables. Do not use that default unless the user explicitly approves it after you explain that it stores database secrets outside the approved durable app-configuration path.

## Secrets

- Never print or log SA passwords, app passwords, or connection strings.
- Never write `.env` files.
- Do not paste secrets into command examples.
- If non-interactive mode is required, prefer already-set environment variables over command-line password arguments.

## Validation

After setup:

```sh
dotnet run --project 7-Deployment/DbSetup/MotorcycleRAG.DbSetup -- --help
docker ps
```

Use SQL connectivity checks only if credentials are available securely. Avoid echoing connection strings.
