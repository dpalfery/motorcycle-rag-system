using Spectre.Console.Cli;
using SkillForge.Cli.Commands;
using SkillForge.Cli.Commands.Agents;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("skillforge");

    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Check skills against the Agent Skills open-format spec.")
        .WithExample("validate", "./skills");

    config.AddCommand<LintCommand>("lint")
        .WithDescription("Score routing-readiness and detect name/description collisions.")
        .WithExample("lint", "./skills", "--explain");

    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Scan skills as a trust surface (injection, secrets, risky scripts).")
        .WithExample("scan", "./skills", "--format", "sarif");

    config.AddCommand<RouteCommand>("route")
        .WithDescription("Simulate which skill fires for a prompt, or run a routing eval set.")
        .WithExample("route", "\"reset my password\"", "--skills", "./skills")
        .WithExample("route", "--eval", "./routing-tests.yml", "--skills", "./skills");

    config.AddCommand<CatalogCommand>("catalog")
        .WithDescription("Inventory all skills under a path (governance view).")
        .WithExample("catalog", "./skills");

    config.AddCommand<PackCommand>("pack")
        .WithDescription("Bundle a skill into a Copilot Studio-compatible .zip.")
        .WithExample("pack", "./skills/password-reset");

    config.AddCommand<NewCommand>("new")
        .WithDescription("Scaffold a spec-correct skill from a template.")
        .WithExample("new", "password-reset", "--template", "sop");

    // Agent Harness Governance Command Branch
    config.AddBranch("agent", agent =>
    {
        agent.SetDescription("Manage and audit AI coding harness agent definitions (.codex, .cursor, .claude, etc.).");

        agent.AddCommand<AgentValidateCommand>("validate")
            .WithDescription("Validate individual harness agent manifests.");

        agent.AddCommand<AgentSyncCheckCommand>("sync-check")
            .WithDescription("Verify role parity and instruction drift across harness folders.");

        agent.AddCommand<AgentCatalogCommand>("catalog")
            .WithDescription("Display the role x harness governance matrix.");
    });
});

return app.Run(args);
