using Spectre.Console;

namespace Bonemm2;

public static class ModLister
{
    public static async Task Run(string? targetUrl, string? cliKey, string? cliBase)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            targetUrl = AnsiConsole.Prompt(
                new TextPrompt<string>("Paste the mod.io collection OR single mod link to list:\n> ")
                    .PromptStyle("cyan"));
            if (string.IsNullOrWhiteSpace(targetUrl)) return;
        }

        var (apiKey, apiBase) = SettingsManager.GetCredentials(cliKey, cliBase);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.Add("X-Modio-Platform", "windows");

        var (game, plan) = await ModDownloader.ResolvePlanAsync(http, apiBase, apiKey, targetUrl);
        if (game is null || plan.Count == 0) return;

        AnsiConsole.MarkupLine($"\n[bold cyan]=== Mod Listing for {Markup.Escape(game.Name)} ===[/]\n");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("#").Centered());
        table.AddColumn(new TableColumn("[bold]Mod Name[/]"));
        table.AddColumn(new TableColumn("[bold]Filename[/]"));
        table.AddColumn(new TableColumn("[bold]Size[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Status[/]").Centered());

        long totalBytes = 0;
        int index = 1;

        foreach (var (mod, name, modfile) in plan)
        {
            string sizeStr = modfile?.Filesize.HasValue == true
                ? Helpers.HumanSize(modfile.Filesize.Value)
                : "[grey]Unknown[/]";

            if (modfile?.Filesize.HasValue == true)
                totalBytes += modfile.Filesize.Value;

            string status = modfile?.Download?.BinaryUrl is not null
                ? "[green]Available[/]"
                : "[red]No Windows file[/]";

            string filename = !string.IsNullOrWhiteSpace(modfile?.Filename)
                ? modfile.Filename
                : "-";

            table.AddRow(
                index.ToString(),
                Markup.Escape(name),
                Markup.Escape(filename),
                sizeStr,
                status);

            index++;
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[bold]Total:[/] [cyan]{plan.Count}[/] mods | [green]{Helpers.HumanSize(totalBytes)}[/]");
    }
}
