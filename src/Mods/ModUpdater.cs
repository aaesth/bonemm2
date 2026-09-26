using System.Text.Json;
using Spectre.Console;

namespace Bonemm2;

public static class ModUpdater
{
    public static async Task Run(string outputDir, bool extract, int maxParallel, string? cliKey, string? cliBase)
    {
        AnsiConsole.MarkupLine("[bold cyan]=== Mod Updater ===[/]");
        AnsiConsole.MarkupLine($"Scanning for mod manifests in: [grey]{Markup.Escape(outputDir)}[/]\n");

        if (!Directory.Exists(outputDir))
        {
            AnsiConsole.MarkupLine($"[yellow]Output directory '{Markup.Escape(outputDir)}' does not exist yet.[/]");
            return;
        }

        string[] manifestFiles = Directory.GetFiles(outputDir, "manifest.json", SearchOption.AllDirectories);
        if (manifestFiles.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No mod manifests found. Mods must be downloaded with bonemm2 v1.3.0+ to enable updates.[/]");
            return;
        }

        var manifests = new List<(string ManifestPath, ModManifest Manifest)>();
        foreach (var mPath in manifestFiles)
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(mPath), ModDownloader.JsonOptions);
                if (manifest is not null && manifest.ModId > 0 && manifest.GameId > 0)
                {
                    manifests.Add((mPath, manifest));
                }
            }
            catch { }
        }

        if (manifests.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No valid mod manifests found to check.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"Found [cyan]{manifests.Count}[/] installed mod(s). Checking mod.io for updates...\n");

        var (apiKey, apiBase) = SettingsManager.GetCredentials(cliKey, cliBase);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.Add("X-Modio-Platform", "windows");

        var updates = new List<(ModManifest OldManifest, ModObject Mod, ModfileObject NewModfile)>();
        GameObject? targetGame = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Checking for mod updates...", async ctx =>
            {
                foreach (var (mPath, manifest) in manifests)
                {
                    ctx.Status($"Checking [cyan]{Markup.Escape(manifest.Name)}[/]...");
                    try
                    {
                        var modResp = await ModDownloader.ApiGet<ModObject>(http, apiBase, $"/games/{manifest.GameId}/mods/{manifest.ModId}", apiKey);
                        if (modResp is null) continue;

                        var latestFile = modResp.Modfile;
                        if (latestFile is null || latestFile.Download?.BinaryUrl is null || !ModDownloader.SupportsWindows(latestFile))
                        {
                            latestFile = await ModDownloader.GetWindowsModfile(http, apiBase, apiKey, manifest.GameId, manifest.ModId);
                        }

                        if (latestFile is not null && latestFile.Id != 0 && latestFile.Id != manifest.ModfileId)
                        {
                            updates.Add((manifest, modResp, latestFile));
                        }

                        if (targetGame is null)
                        {
                            targetGame = new GameObject { Id = manifest.GameId, Name = "BONELAB" };
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[grey]Warning checking {Markup.Escape(manifest.Name)}: {Markup.Escape(ex.Message)}[/]");
                    }
                }
            });

        if (updates.Count == 0)
        {
            AnsiConsole.MarkupLine("[green][[ALL UP TO DATE]][/] All checked mods are running the latest version!");
            return;
        }

        AnsiConsole.MarkupLine($"\n[bold green]{updates.Count}[/] update(s) available!\n");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold]Mod Name[/]"));
        table.AddColumn(new TableColumn("[bold]Current File[/]"));
        table.AddColumn(new TableColumn("[bold]New File[/]"));
        table.AddColumn(new TableColumn("[bold]New Size[/]").RightAligned());

        long totalBytes = 0;
        var plan = new List<(ModObject Mod, string Name, ModfileObject? Modfile)>();

        foreach (var (oldManifest, mod, newFile) in updates)
        {
            string newSizeStr = newFile.Filesize.HasValue
                ? Helpers.HumanSize(newFile.Filesize.Value)
                : "[grey]Unknown[/]";

            if (newFile.Filesize.HasValue) totalBytes += newFile.Filesize.Value;

            table.AddRow(
                Markup.Escape(oldManifest.Name),
                Markup.Escape(oldManifest.Filename),
                Markup.Escape(newFile.Filename),
                newSizeStr);

            plan.Add((mod, oldManifest.Name, newFile));
        }

        AnsiConsole.Write(table);

        if (!AnsiConsole.Confirm("\nDownload and apply updates now?", defaultValue: true))
            return;

        targetGame ??= new GameObject { Id = updates[0].OldManifest.GameId, Name = "BONELAB" };

        await ModDownloader.ExecuteParallelDownload(
            http,
            targetGame,
            plan,
            outputDir,
            extract,
            maxParallel,
            totalBytes,
            updateMode: true,
            sourceUrl: "Mod Updater");
    }
}
