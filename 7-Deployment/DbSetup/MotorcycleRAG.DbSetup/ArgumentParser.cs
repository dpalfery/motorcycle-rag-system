using System.CommandLine;

namespace MotorcycleRAG.DbSetup;

public class ArgumentParser
{
    public string? ProjectSlug { get; private set; }
    public string? Server { get; set; }
    public int Port { get; set; } = 1433;
    public string? DatabaseName { get; set; }
    public string? AppUser { get; set; }
    public string? AppPassword { get; set; }
    public string? SaPassword { get; set; }
    public bool NonInteractive { get; private set; }
    public bool SeedTestData { get; private set; }
    public bool EnvVarsInProcess { get; private set; }

    public ArgumentParser(string[] args)
    {
        // Check for help before setting up commands
        if (args.Contains("--help") || args.Contains("-h") || args.Contains("-?"))
        {
            ShowHelp();
            Environment.Exit(0);
        }

        var projectSlugOption = new Option<string>(
            name: "--project-slug",
            description: "Project identifier for environment variables (e.g., 'motorcyclerag' creates MOTORCYCLERAG_DB_* vars)",
            getDefaultValue: () => "motorcyclerag")
        {
            IsRequired = false
        };

        var serverOption = new Option<string>(
            name: "--server",
            description: "SQL Server instance (e.g., localhost or localhost\\SQLEXPRESS)")
        {
            IsRequired = false
        };

        var portOption = new Option<int>(
            name: "--port",
            description: "SQL Server port",
            getDefaultValue: () => 1433)
        {
            IsRequired = false
        };

        var databaseNameOption = new Option<string>(
            name: "--db-name",
            description: "Target database name")
        {
            IsRequired = false
        };

        var appUserOption = new Option<string>(
            name: "--app-user",
            description: "Application database user/login name")
        {
            IsRequired = false
        };

        var appPasswordOption = new Option<string>(
            name: "--app-password",
            description: "Application user password (auto-generated if not provided)")
        {
            IsRequired = false
        };

        var saPasswordOption = new Option<string>(
            name: "--sa-password",
            description: "SA password for SQL Server")
        {
            IsRequired = false
        };

        var nonInteractiveOption = new Option<bool>(
            name: "--non-interactive",
            description: "Run without interactive prompts for CI")
        {
            IsRequired = false
        };

        var seedTestDataOption = new Option<bool>(
            name: "--seed-test-data",
            description: "Seed database with test data after schema deployment")
        {
            IsRequired = false
        };

        var envVarsInProcessOption = new Option<bool>(
            name: "--env-vars-in-proc",
            description: "Set environment variables at process level (for CI/CD) instead of user level (for local persistence)")
        {
            IsRequired = false
        };

        var rootCommand = new RootCommand("Motorcycle RAG Database Setup CLI - Automated database provisioning");

        rootCommand.AddOption(projectSlugOption);
        rootCommand.AddOption(serverOption);
        rootCommand.AddOption(portOption);
        rootCommand.AddOption(databaseNameOption);
        rootCommand.AddOption(appUserOption);
        rootCommand.AddOption(appPasswordOption);
        rootCommand.AddOption(saPasswordOption);
        rootCommand.AddOption(nonInteractiveOption);
        rootCommand.AddOption(seedTestDataOption);
        rootCommand.AddOption(envVarsInProcessOption);

        rootCommand.SetHandler((context) =>
        {
            ProjectSlug = context.ParseResult.GetValueForOption(projectSlugOption);
            Server = context.ParseResult.GetValueForOption(serverOption);
            Port = context.ParseResult.GetValueForOption(portOption);
            DatabaseName = context.ParseResult.GetValueForOption(databaseNameOption);
            AppUser = context.ParseResult.GetValueForOption(appUserOption);
            AppPassword = context.ParseResult.GetValueForOption(appPasswordOption);
            SaPassword = context.ParseResult.GetValueForOption(saPasswordOption);
            NonInteractive = context.ParseResult.GetValueForOption(nonInteractiveOption);
            SeedTestData = context.ParseResult.GetValueForOption(seedTestDataOption);
            EnvVarsInProcess = context.ParseResult.GetValueForOption(envVarsInProcessOption);
        });

        rootCommand.Invoke(args);
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Description:");
        Console.WriteLine("  Motorcycle RAG Database Setup CLI - Automated database provisioning");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --project-slug <slug>                          Project identifier for environment variables [default: motorcyclerag]");
        Console.WriteLine("  --server <server>                              SQL Server instance [default: localhost]");
        Console.WriteLine("  --port <port>                                  SQL Server port [default: 1433]");
        Console.WriteLine("  --db-name <db-name>                            Target database name [default: MotorcycleRAG]");
        Console.WriteLine("  --app-user <app-user>                          Application database user/login name [default: motorcyclerag_app]");
        Console.WriteLine("  --app-password <app-password>                  Application user password (auto-generated if not provided)");
        Console.WriteLine("  --sa-password <sa-password>                    SA password for SQL Server");
        Console.WriteLine("  --non-interactive                              Run without interactive prompts");
        Console.WriteLine("  --seed-test-data                               Seed database with test data");
        Console.WriteLine("  --env-vars-in-proc                             Set env vars at process level (for CI/CD)");
        Console.WriteLine("  --version                                      Show version information");
        Console.WriteLine("  -?, -h, --help                                 Show help and usage information");
        Console.WriteLine();
        Console.WriteLine("Environment Variables:");
        Console.WriteLine("  MOTORCYCLERAG_DB_SERVER                        SQL Server instance");
        Console.WriteLine("  MOTORCYCLERAG_DB_PORT                          SQL Server port");
        Console.WriteLine("  MOTORCYCLERAG_DB_NAME                          Database name");
        Console.WriteLine("  MOTORCYCLERAG_DB_APP_USER                      Application user name");
        Console.WriteLine("  MOTORCYCLERAG_DB_APP_PASSWORD                  Application user password");
        Console.WriteLine("  MOTORCYCLERAG_DB_SA_PASSWORD                   SA password");
    }

    public void ApplyEnvironmentOverrides()
    {
        // Build environment variable prefix from project slug
        var envPrefix = $"{ProjectSlug!.ToUpperInvariant()}_DB";

        // Apply environment variable fallbacks if CLI args not provided
        Server ??= Environment.GetEnvironmentVariable($"{envPrefix}_SERVER");

        var portStr = Environment.GetEnvironmentVariable($"{envPrefix}_PORT");
        if (!string.IsNullOrEmpty(portStr) && int.TryParse(portStr, out var envPort))
        {
            Port = envPort;
        }

        DatabaseName ??= Environment.GetEnvironmentVariable($"{envPrefix}_NAME");
        AppUser ??= Environment.GetEnvironmentVariable($"{envPrefix}_APP_USER");
        AppPassword ??= Environment.GetEnvironmentVariable($"{envPrefix}_APP_PASSWORD");
        SaPassword ??= Environment.GetEnvironmentVariable($"{envPrefix}_SA_PASSWORD");
    }

    public bool Validate(out string errorMessage)
    {
        errorMessage = string.Empty;

        // Project slug is always required
        if (string.IsNullOrEmpty(ProjectSlug))
        {
            errorMessage = "Project slug is required. This should not happen - check default value.";
            return false;
        }

        // Set default server
        Server ??= "localhost";

        // Remaining validation will be done in interactive mode or Program.cs
        return true;
    }
}
