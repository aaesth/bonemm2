using Spectre.Console;

namespace Bonemm2;

public static class ModSearch
{
    public static async Task Run(
        string? query,
        string? gameSlug,
        string outputDir,
        bool extract,
        int maxParallel,
        string? cliKey,
        string? cliBase)
    {
        if (string.IsNullOrWhiteSpace(gameSlug))
        {
            gameSlug = "bonelab";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            query = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter search term:\n> ")
                    .PromptStyle("cyan"));
            if (string.IsNullOrWhiteSpace(query)) return;
        }

        var (apiKey, apiBase) = SettingsManager.GetCredentials(cliKey, cliBase);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.Add("X-Modio-Platform", "windows");

        AnsiConsole.MarkupLine($"\nSearching '[cyan]{Markup.Escape(gameSlug)}[/]' for '[green]{Markup.Escape(query)}[/]'...");

        var gamesResp = await ModDownloader.ApiGet<PagedResponse<GameObject>>(http, apiBase, "/games", apiKey, ("name_id", gameSlug));
        var game = gamesResp.Data.FirstOrDefault();
        if (game is null)
        {
            AnsiConsole.MarkupLine($"[red]Game '{Markup.Escape(gameSlug)}' not found on mod.io.[/]");
            return;
        }

        var searchResp = await ModDownloader.ApiGet<PagedResponse<ModObject>>(
            http, apiBase, $"/games/{game.Id}/mods", apiKey,
            ("_q", query),
            ("_sort", "-downloads"),
            ("_limit", "25"));

        var mods = searchResp.Data;
        if (mods.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No mods found matching '{Markup.Escape(query)}'.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("#").Centered());
        table.AddColumn(new TableColumn("[bold]Mod Name[/]"));
        table.AddColumn(new TableColumn("[bold]Downloads[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Summary[/]"));

        for (int i = 0; i < mods.Count; i++)
        {
            var m = mods[i];
            string downloads = m.Stats?.DownloadsTotal.ToString("N0") ?? "-";
            string summary = !string.IsNullOrWhiteSpace(m.Summary)
                ? Helpers.Truncate(m.Summary.Replace("\r", " ").Replace("\n", " "), 50)
                : "[grey]No summary[/]";

            table.AddRow(
                (i + 1).ToString(),
                Markup.Escape(m.Name),
                downloads,
                Markup.Escape(summary));
        }

        AnsiConsole.Write(table);

        string choice = AnsiConsole.Prompt(
            new TextPrompt<string>("\nEnter mod number to download (or leave blank to return):\n> ")
                .AllowEmpty());

        if (int.TryParse(choice.Trim(), out int selectedIndex) && selectedIndex >= 1 && selectedIndex <= mods.Count)
        {
            var chosenMod = mods[selectedIndex - 1];
            string name = string.IsNullOrWhiteSpace(chosenMod.Name) ? $"mod-{chosenMod.Id}" : chosenMod.Name;
            
            var modfile = chosenMod.Modfile;
            if (modfile?.Download?.BinaryUrl is null || !ModDownloader.SupportsWindows(modfile))
                modfile = await ModDownloader.GetWindowsModfile(http, apiBase, apiKey, game.Id, chosenMod.Id);

            var plan = new List<(ModObject Mod, string Name, ModfileObject? Modfile)>
            {
                (chosenMod, name, modfile)
            };

            long totalBytes = modfile?.Filesize ?? 0;
            await ModDownloader.ExecuteParallelDownload(
                http,
                game,
                plan,
                outputDir,
                extract,
                maxParallel,
                totalBytes,
                updateMode: false,
                sourceUrl: $"Search: {query}");
        }
    }
}
