using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Logging;
using MotorcycleRAG.DbSetup;

// Create logger factory
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);

    // Wrap every ILoggerProvider registered above (Console) in the central
    // SanitizingLoggerProvider so no structured log state reaches a sink without
    // LogSanitizer escaping. MUST be the last logging-provider call.
    builder.AddSanitizingLogger();
});

var logger = loggerFactory.CreateLogger<Program>();

try
{
    // Parse command line arguments
    var parser = new ArgumentParser(args);
    parser.ApplyEnvironmentOverrides();

    var (isValid, errorMessage) = parser.Validate();
    if (!isValid)
    {
        logger.LogError("Validation failed: {ErrorMessage}", errorMessage);
        Environment.Exit(1);
    }

    logger.LogInformation("Starting Motorcycle RAG Database Setup");
    logger.LogInformation("Project Slug: {ProjectSlug}", parser.ProjectSlug);

    // Create component instances
    var environmentManager = new EnvironmentManager(loggerFactory.CreateLogger<EnvironmentManager>());
    var passwordManager = new PasswordManager(loggerFactory.CreateLogger<PasswordManager>());
    var prompter = new InteractivePrompter(loggerFactory.CreateLogger<InteractivePrompter>());
    var connectionFactory = new SqlDbSetupConnectionFactory();
    var provisioner = new SqlServerProvisioner(loggerFactory.CreateLogger<SqlServerProvisioner>(), connectionFactory);
    var preflightChecker = new PreflightChecker(loggerFactory.CreateLogger<PreflightChecker>(), connectionFactory);
    var catalogRoot = FindCatalogRootFromBaseDirectory();
    var scriptExecutor = new SqlScriptExecutor(
        loggerFactory.CreateLogger<SqlScriptExecutor>(),
        connectionFactory,
        catalogRoot);

    // Prompt for missing values in interactive mode
    if (!parser.NonInteractive)
    {
        // Prompt for SA password
        if (string.IsNullOrEmpty(parser.SaPassword))
        {
            Console.WriteLine();
            parser.SaPassword = prompter.PromptForPassword("Enter SA password for SQL Server", confirm: false);
            SensitiveLogRedactor.RegisterSecret(parser.SaPassword);
        }

        // Prompt for database name
        if (string.IsNullOrEmpty(parser.DatabaseName))
        {
            parser.DatabaseName = prompter.PromptForInput("Database name", "MotorcycleRAG", required: true);
        }

        // Prompt for app user
        if (string.IsNullOrEmpty(parser.AppUser))
        {
            parser.AppUser = prompter.PromptForInput("Application user name", "motorcyclerag_app", required: true);
        }
    }
    else
    {
        // Non-interactive mode: require all values or use defaults
        if (string.IsNullOrEmpty(parser.SaPassword))
        {
            logger.LogError("SA password is required in non-interactive mode. Provide --sa-password or set MOTORCYCLERAG_DB_SA_PASSWORD environment variable.");
            Environment.Exit(1);
        }

        parser.DatabaseName ??= "MotorcycleRAG";
        parser.AppUser ??= "motorcyclerag_app";
    }

    logger.LogInformation("Server: {Server}:{Port}", parser.Server, parser.Port);
    logger.LogInformation("Database: {Database}", parser.DatabaseName);
    logger.LogInformation("App User: {AppUser}", parser.AppUser);

    // Check administrator privileges
    if (environmentManager.IsAdministrator())
    {
        logger.LogInformation("Running with administrator privileges");
    }
    else
    {
        logger.LogWarning("Not running with administrator privileges. Some operations may fail.");
    }

    // Build SA connection string
    SensitiveLogRedactor.RegisterSecret(parser.SaPassword);
    var saConnectionString = $"Server={parser.Server},{parser.Port};Database=master;User Id=sa;Password={parser.SaPassword};TrustServerCertificate=true;";
    SensitiveLogRedactor.RegisterSecret(saConnectionString);

    // Run preflight checks
    logger.LogInformation("Running preflight checks...");
    var preflightPassed = await preflightChecker.PerformPreflightChecksAsync(saConnectionString, parser.DatabaseName!, CancellationToken.None);
    if (!preflightPassed)
    {
        logger.LogError("Preflight checks failed. Please review the errors above.");
        logger.LogInformation("{RemediationInstructions}", preflightChecker.RemediationInstructions);
        Environment.Exit(1);
    }

    // Generate or get password for app user
    var appPassword = parser.AppPassword ?? passwordManager.GeneratePassword();
    SensitiveLogRedactor.RegisterSecret(appPassword);
    if (string.IsNullOrEmpty(parser.AppPassword))
    {
        logger.LogInformation("Generated password for application user (password hidden)");
    }
    else
    {
        logger.LogInformation("Using provided password for application user");
    }

    // Provision the database
    logger.LogInformation("Starting database provisioning...");
    var success = await provisioner.ProvisionDatabaseAsync(
        saConnectionString,
        parser.DatabaseName!,
        parser.AppUser!,
        appPassword);

    if (!success)
    {
        logger.LogError("Database provisioning failed");
        Environment.Exit(1);
    }

    // Execute schema.sql
    logger.LogInformation("Deploying database schema...");
    var dbConnectionString = $"Server={parser.Server},{parser.Port};Database={parser.DatabaseName};User Id=sa;Password={parser.SaPassword};TrustServerCertificate=true;";
    SensitiveLogRedactor.RegisterSecret(dbConnectionString);
    
    var schemaSuccess = await scriptExecutor.ExecuteScriptAsync(dbConnectionString, DbSetupScript.Schema, CancellationToken.None);
    if (!schemaSuccess)
    {
        logger.LogError("Failed to deploy database schema");
        Environment.Exit(1);
    }

    // Execute test-data.sql if requested
    if (parser.SeedTestData)
    {
        logger.LogInformation("Seeding test data...");
        var testDataSuccess = await scriptExecutor.ExecuteScriptAsync(dbConnectionString, DbSetupScript.TestData, CancellationToken.None);
        if (!testDataSuccess)
        {
            logger.LogWarning("Failed to seed test data (continuing anyway)");
        }
    }

    // Build application connection string
    var appConnectionString = $"Server={parser.Server},{parser.Port};Database={parser.DatabaseName};User Id={parser.AppUser};Password={appPassword};TrustServerCertificate=true;";
    SensitiveLogRedactor.RegisterSecret(appConnectionString);

    // Set environment variables
    Console.WriteLine();
    Console.WriteLine("Setting environment variables...");

    var envPrefix = parser.ProjectSlug!.ToUpperInvariant();
    var envTarget = parser.EnvVarsInProcess ? EnvironmentVariableTarget.Process : EnvironmentVariableTarget.User;

    if (parser.EnvVarsInProcess)
    {
        logger.LogInformation("Setting environment variables at process level (--env-vars-in-proc)");
    }
    else
    {
        logger.LogInformation("Setting environment variables at user level for persistence");
    }

    try
    {
        environmentManager.SetEnvironmentVariable($"{envPrefix}_DB_SERVER", parser.Server!, envTarget);
        Console.WriteLine($"  ✓ {envPrefix}_DB_SERVER");

        environmentManager.SetEnvironmentVariable($"{envPrefix}_DB_PORT", parser.Port.ToString(), envTarget);
        Console.WriteLine($"  ✓ {envPrefix}_DB_PORT");

        environmentManager.SetEnvironmentVariable($"{envPrefix}_DB_NAME", parser.DatabaseName!, envTarget);
        Console.WriteLine($"  ✓ {envPrefix}_DB_NAME");

        environmentManager.SetEnvironmentVariable($"{envPrefix}_DB_APP_USER", parser.AppUser!, envTarget);
        Console.WriteLine($"  ✓ {envPrefix}_DB_APP_USER");

        environmentManager.SetEnvironmentVariable($"{envPrefix}_DB_APP_PASSWORD", appPassword, envTarget);
        Console.WriteLine($"  ✓ {envPrefix}_DB_APP_PASSWORD");

        environmentManager.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", appConnectionString, envTarget);
        Console.WriteLine($"  ✓ ConnectionStrings__DefaultConnection");

        // Also set with uppercase for cross-platform compatibility
        environmentManager.SetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULTCONNECTION", appConnectionString, envTarget);
        Console.WriteLine($"  ✓ CONNECTIONSTRINGS__DEFAULTCONNECTION");

        environmentManager.SetEnvironmentVariable($"{envPrefix}_DB_SA_PASSWORD", parser.SaPassword!, envTarget);
        Console.WriteLine($"  ✓ {envPrefix}_DB_SA_PASSWORD");

        Console.WriteLine();
        Console.WriteLine("✓ All environment variables set successfully");

        // If GITHUB_ENV is detected, also write variables there
        var githubEnv = Environment.GetEnvironmentVariable("GITHUB_ENV");
        if (!string.IsNullOrEmpty(githubEnv))
        {
            logger.LogInformation("Detected GitHub Actions environment, writing to GITHUB_ENV");
            try
            {
                var envLines = new[]
                {
                    $"ConnectionStrings__DefaultConnection={appConnectionString}",
                    $"CONNECTIONSTRINGS__DEFAULTCONNECTION={appConnectionString}",
                    $"{envPrefix}_DB_SERVER={parser.Server}",
                    $"{envPrefix}_DB_PORT={parser.Port}",
                    $"{envPrefix}_DB_NAME={parser.DatabaseName}",
                    $"{envPrefix}_DB_APP_USER={parser.AppUser}",
                    $"{envPrefix}_DB_APP_PASSWORD={appPassword}",
                    $"{envPrefix}_DB_SA_PASSWORD={parser.SaPassword}"
                };

                await File.AppendAllLinesAsync(githubEnv, envLines);
                logger.LogInformation("Environment variables written to GitHub Actions GITHUB_ENV");
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to write to GITHUB_ENV file. ExceptionType={ExceptionType}", ex.GetType().Name);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError("Failed to set environment variables. ExceptionType={ExceptionType}", ex.GetType().Name);
        Console.WriteLine();
        Console.WriteLine($"✗ Failed to set environment variables. Exception type: {ex.GetType().Name}");
        Console.WriteLine("You may need to run as administrator or set them manually.");
    }

    // Display summary
    if (!parser.NonInteractive)
    {
        Console.WriteLine();
        Console.WriteLine("✓ Setup complete!");
        Console.WriteLine();
        Console.WriteLine("Connection string:");
        var maskedConnectionString = $"Server={parser.Server},{parser.Port};Database={parser.DatabaseName};User Id={parser.AppUser};Password=<hidden>;TrustServerCertificate=true;";
        Console.WriteLine($"  {maskedConnectionString}");
        Console.WriteLine();
        Console.WriteLine($"Environment variables set (using project slug '{envPrefix}'):");
        Console.WriteLine($"  {envPrefix}_DB_SERVER={parser.Server}");
        Console.WriteLine($"  {envPrefix}_DB_PORT={parser.Port}");
        Console.WriteLine($"  {envPrefix}_DB_NAME={parser.DatabaseName}");
        Console.WriteLine($"  {envPrefix}_DB_APP_USER={parser.AppUser}");
        Console.WriteLine($"  {envPrefix}_DB_APP_PASSWORD=<set>");
        Console.WriteLine($"  {envPrefix}_DB_SA_PASSWORD=<set>");
        Console.WriteLine($"  ConnectionStrings__DefaultConnection=<set>");
        Console.WriteLine();
        Console.WriteLine("Note: You may need to restart your terminal/IDE for the environment variables to take effect.");
    }

    logger.LogInformation("Database setup completed successfully!");
}
catch (Exception ex)
{
    logger.LogError("An error occurred during database setup. ExceptionType={ExceptionType}", ex.GetType().Name);
    Environment.Exit(1);
}

static string FindCatalogRootFromBaseDirectory()
{
    for (var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
         currentDirectory is not null;
         currentDirectory = currentDirectory.Parent)
    {
        var schemaPath = Path.Combine(
            currentDirectory.FullName,
            "4-Persistence",
            "MotorcycleRAG.Persistence",
            "Sql",
            "schema.sql");
        var testDataPath = Path.Combine(
            currentDirectory.FullName,
            "7-Deployment",
            "DbSetup",
            "sql",
            "test-data.sql");

        if (File.Exists(schemaPath) && File.Exists(testDataPath))
        {
            return currentDirectory.FullName;
        }
    }

    throw new InvalidOperationException("Could not locate the SQL setup script catalog from AppContext.BaseDirectory.");
}
