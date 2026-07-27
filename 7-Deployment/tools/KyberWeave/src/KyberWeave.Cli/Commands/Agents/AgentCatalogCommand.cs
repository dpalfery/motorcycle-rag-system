using System.ComponentModel;
using KyberWeave.Core.Agents.Model;
using KyberWeave.Core.Agents.Parsing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KyberWeave.Cli.Commands.Agents;

public sealed class AgentCatalogSettings : CommandSettings
{
    [CommandArgument(0, "[path]")]
    [Description("Root project directory containing harness agent folders (.codex, .cursor, .claude, etc.). Defaults to current directory.")]
    public string Path { get; set; } = ".";
}

public sealed class AgentCatalogCommand : Command<AgentCatalogSettings>
{
    public override int Execute(CommandContext context, AgentCatalogSettings settings)
    {
        var agentSet = AgentLoader.LoadAll(settings.Path);
        var matrix = agentSet.GetRoleHarnessMatrix();
        var profiles = HarnessCapabilityProfile.DefaultProfiles;

        var table = new Table();
        table.Title("[bold blue]Agent Harness Parity Matrix[/]");
        table.AddColumn("[bold]Role Name[/]");

        foreach (var (harnessKind, _) in profiles)
        {
            table.AddColumn(new TableColumn($"[bold]{harnessKind}[/]").Centered());
        }

        foreach (var (role, harnessMap) in matrix.OrderBy(m => m.Key))
        {
            var row = new List<string> { $"[bold]{Markup.Escape(role)}[/]" };

            foreach (var (harnessKind, profile) in profiles)
            {
                if (harnessMap.ContainsKey(harnessKind))
                {
                    row.Add("[green]✔ Native[/]");
                }
                else if (profile.MappedRoleSkillOverrides.ContainsKey(role))
                {
                    row.Add("[cyan]⚡ Skill[/]");
                }
                else
                {
                    row.Add("[red]✘ Missing[/]");
                }
            }

            table.AddRow(row.ToArray());
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\nTotal agent roles: [bold]{matrix.Count}[/]");
        return 0;
    }
}
