using Spectre.Console;

namespace Bonemm2;

public static class ModUninstaller
{
    public static void Run(string? defaultDownloadsDir = null)
    {
        AnsiConsole.MarkupLine("[bold cyan]=== Mod Uninstaller ===[/]\n");

        var locationChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which folder would you like to uninstall mods from?")
                .AddChoices(
                    "BONELAB Game Mods Folder (Extracted Mods)",
                    "bonemm2 Downloads Folder (Downloaded Archives)",
                    "Custom Folder Path",
                    "Cancel"));

        if (locationChoice == "Cancel") return;

        string targetFolder = "";
        if (locationChoice.StartsWith("BONELAB Game Mods"))
        {
            targetFolder = Helpers.GetDefaultBonelabModsFolder();
        }
        else if (locationChoice.StartsWith("bonemm2 Downloads"))
        {
            targetFolder = Path.GetFullPath(defaultDownloadsDir ?? "./bonelab_mods");
        }
        else
        {
            targetFolder = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter folder path to uninstall from:\n> ")
                    .PromptStyle("cyan"));
            targetFolder = Path.GetFullPath(targetFolder.Trim().Trim('"', '\''));
        }

        if (!Directory.Exists(targetFolder))
        {
            AnsiConsole.MarkupLine($"[red][[ERROR]][/] Folder does not exist: {Markup.Escape(targetFolder)}");
            return;
        }

        List<string> modDirs = Directory.GetDirectories(targetFolder)
            .Select(p => Path.GetFileName(p) ?? "")
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(n => n)
            .ToList();

        if (modDirs.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No mod folders found in '{Markup.Escape(targetFolder)}'.[/]");
            return;
        }

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select mods to [red]uninstall / delete[/] (Use [green]Space[/] to toggle, [green]Enter[/] to confirm):")
            .Required(false)
            .PageSize(15)
            .MoreChoicesText("[grey](Move up and down to reveal more mods)[/]")
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle a mod, [green]<enter>[/] to accept)[/]")
            .AddChoices(modDirs);

        var selectedMods = AnsiConsole.Prompt(prompt);

        if (selectedMods.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No mods selected for removal.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"\nYou selected [bold red]{selectedMods.Count}[/] mod(s) to permanently delete:");
        foreach (var mod in selectedMods)
        {
            AnsiConsole.MarkupLine($"  [red]•[/] {Markup.Escape(mod)}");
        }

        if (!AnsiConsole.Confirm("\nAre you sure you want to delete these folders?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
            return;
        }

        int deleted = 0;
        int failed = 0;

        foreach (var mod in selectedMods)
        {
            string modPath = Path.Combine(targetFolder, mod);
            try
            {
                Directory.Delete(modPath, recursive: true);
                AnsiConsole.MarkupLine($"[grey]Deleted:[/] [green]{Markup.Escape(mod)}[/]");
                deleted++;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red][[FAILED]][/] {Markup.Escape(mod)}: {Markup.Escape(ex.Message)}");
                failed++;
            }
        }

        AnsiConsole.MarkupLine($"\n[green][[COMPLETE]][/] Successfully removed [green]{deleted}[/] mod(s)" + (failed > 0 ? $", [red]{failed}[/] failed." : "."));
    }
}
