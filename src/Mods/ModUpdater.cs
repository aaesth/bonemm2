using System.IO.Compression;
using System.Text.Json;
using Spectre.Console;

namespace Bonemm2;

public static class ModUpdater
{
    private class InstalledModInfo
    {
        public string FolderPath { get; set; } = "";
        public string LocationName { get; set; } = "";
        public bool IsGameFolder { get; set; }
        public ModManifest? Manifest { get; set; }
        public BonelabPallet? Pallet { get; set; }
        public string DisplayName { get; set; } = "";
    }

    public static async Task Run(string outputDir, bool extract, int maxParallel, string? cliKey, string? cliBase)
    {
        AnsiConsole.MarkupLine("[bold cyan]=== Mod Updater ===[/]");

        string gameModsFolder = Helpers.GetDefaultBonelabModsFolder();
        outputDir = Path.GetFullPath(outputDir);

        AnsiConsole.MarkupLine($"Scanning BONELAB Game Mods: [green]{Markup.Escape(gameModsFolder)}[/]");
        if (Directory.Exists(outputDir) && !string.Equals(outputDir, gameModsFolder, StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"Scanning Downloads Cache:    [grey]{Markup.Escape(outputDir)}[/]");
        }
        AnsiConsole.WriteLine();

        var discoveredMods = new List<InstalledModInfo>();

        string gameLocationLabel = OperatingSystem.IsWindows() ? "Game (LocalLow)" : "Game (Proton)";

        // 1. Scan BONELAB Game Mods Folder
        if (Directory.Exists(gameModsFolder))
        {
            foreach (var dir in Directory.GetDirectories(gameModsFolder))
            {
                var info = DetectModInDirectory(dir, isGameFolder: true, gameLocationLabel);
                if (info != null) discoveredMods.Add(info);
            }
        }

        // 2. Scan Downloads Cache (if it exists)
        if (Directory.Exists(outputDir) && !string.Equals(outputDir, gameModsFolder, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var dir in Directory.GetDirectories(outputDir))
            {
                // Only add if not already tracked in the game folder
                string folderName = Path.GetFileName(dir);
                if (discoveredMods.Any(m => string.Equals(Path.GetFileName(m.FolderPath), folderName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var info = DetectModInDirectory(dir, isGameFolder: false, "Downloads");
                if (info != null) discoveredMods.Add(info);
            }
        }

        if (discoveredMods.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No installed mods detected in your BONELAB Mods folder or download cache.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"Found [cyan]{discoveredMods.Count}[/] installed mod(s). Checking mod.io for updates...\n");

        var (apiKey, apiBase) = SettingsManager.GetCredentials(cliKey, cliBase);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.Add("X-Modio-Platform", "windows");

        var updates = new List<(InstalledModInfo Installed, ModObject Mod, ModfileObject NewModfile)>();
        int bonelabGameId = 3809; // BONELAB's default mod.io game id

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Checking for mod updates...", async ctx =>
            {
                foreach (var modInfo in discoveredMods)
                {
                    ctx.Status($"Checking [cyan]{Markup.Escape(modInfo.DisplayName)}[/]...");
                    try
                    {
                        ModObject? modResp = null;
                        int gameId = modInfo.Manifest?.GameId ?? bonelabGameId;

                        // Case A: We have a known mod.io ModId from manifest
                        if (modInfo.Manifest != null && modInfo.Manifest.ModId > 0)
                        {
                            modResp = await ModDownloader.ApiGet<ModObject>(http, apiBase, $"/games/{gameId}/mods/{modInfo.Manifest.ModId}", apiKey);
                        }
                        // Case B: Discovered via pallet.json, search mod.io by title / barcode
                        else if (modInfo.Pallet != null)
                        {
                            string searchTerm = modInfo.Pallet.Title ?? modInfo.Pallet.Barcode ?? modInfo.DisplayName;
                            var searchResp = await ModDownloader.ApiGet<PagedResponse<ModObject>>(
                                http, apiBase, $"/games/{gameId}/mods", apiKey,
                                ("_q", searchTerm),
                                ("_limit", "5"));

                            modResp = searchResp.Data.FirstOrDefault();
                            if (modResp != null && modInfo.Manifest == null)
                            {
                                // Tag it for future runs
                                modInfo.Manifest = new ModManifest
                                {
                                    ModId = modResp.Id,
                                    GameId = gameId,
                                    ModfileId = modResp.Modfile?.Id ?? 0,
                                    Name = modResp.Name,
                                    Filename = modResp.Modfile?.Filename ?? "",
                                    DownloadedAt = DateTime.UtcNow
                                };
                                string tagFile = modInfo.IsGameFolder ? ".bonemm2.json" : "manifest.json";
                                try
                                {
                                    File.WriteAllText(Path.Combine(modInfo.FolderPath, tagFile), JsonSerializer.Serialize(modInfo.Manifest, ModDownloader.JsonOptions));
                                }
                                catch { }
                            }
                        }

                        if (modResp is null) continue;

                        var latestFile = modResp.Modfile;
                        if (latestFile is null || latestFile.Download?.BinaryUrl is null || !ModDownloader.SupportsWindows(latestFile))
                        {
                            latestFile = await ModDownloader.GetWindowsModfile(http, apiBase, apiKey, gameId, modResp.Id);
                        }

                        if (latestFile is not null && latestFile.Id != 0)
                        {
                            bool isOutdated = false;
                            if (modInfo.Manifest != null && modInfo.Manifest.ModfileId > 0)
                            {
                                isOutdated = latestFile.Id != modInfo.Manifest.ModfileId;
                            }
                            else if (modInfo.Pallet != null && !string.IsNullOrWhiteSpace(modInfo.Pallet.Version) && !string.IsNullOrWhiteSpace(latestFile.Filename))
                            {
                                isOutdated = !latestFile.Filename.Contains(modInfo.Pallet.Version, StringComparison.OrdinalIgnoreCase);
                            }

                            if (isOutdated)
                            {
                                updates.Add((modInfo, modResp, latestFile));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[grey]Warning checking {Markup.Escape(modInfo.DisplayName)}: {Markup.Escape(ex.Message)}[/]");
                    }
                }
            });

        if (updates.Count == 0)
        {
            AnsiConsole.MarkupLine("[green][[ALL UP TO DATE]][/] All installed BONELAB mods are running the latest version!");
            return;
        }

        AnsiConsole.MarkupLine($"\n[bold green]{updates.Count}[/] update(s) available!\n");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold]Mod Name[/]"));
        table.AddColumn(new TableColumn("[bold]Location[/]"));
        table.AddColumn(new TableColumn("[bold]Current Version[/]"));
        table.AddColumn(new TableColumn("[bold]New Version/File[/]"));
        table.AddColumn(new TableColumn("[bold]Download Size[/]").RightAligned());

        long totalBytes = 0;
        foreach (var (installed, mod, newFile) in updates)
        {
            string sizeStr = newFile.Filesize.HasValue
                ? Helpers.HumanSize(newFile.Filesize.Value)
                : "[grey]Unknown[/]";

            if (newFile.Filesize.HasValue) totalBytes += newFile.Filesize.Value;

            string currentVer = installed.Manifest != null && !string.IsNullOrWhiteSpace(installed.Manifest.Filename)
                ? installed.Manifest.Filename
                : (installed.Pallet?.Version ?? "[grey]Unknown[/]");

            table.AddRow(
                Markup.Escape(installed.DisplayName),
                installed.IsGameFolder ? "[green]Game (LocalLow)[/]" : "[cyan]Downloads[/]",
                Markup.Escape(currentVer),
                Markup.Escape(newFile.Filename),
                sizeStr);
        }

        AnsiConsole.Write(table);

        if (!AnsiConsole.Confirm("\nDownload and install updates directly?", defaultValue: true))
            return;

        AnsiConsole.MarkupLine("\n[cyan]Applying updates...[/]");

        int successCount = 0;
        int failCount = 0;

        foreach (var (installed, mod, newFile) in updates)
        {
            if (newFile.Download?.BinaryUrl is null) continue;

            string tempZip = Path.Combine(Path.GetTempPath(), $"bonemm2_update_{newFile.Id}.zip");
            try
            {
                AnsiConsole.Markup($"Updating [bold]{Markup.Escape(installed.DisplayName)}[/]... ");

                // 1. Download updated zip
                using (var response = await http.GetAsync(newFile.Download.BinaryUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    await using var httpStream = await response.Content.ReadAsStreamAsync();
                    await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
                    await httpStream.CopyToAsync(fileStream);
                }

                var updatedManifest = new ModManifest
                {
                    ModId = mod.Id,
                    GameId = installed.Manifest?.GameId ?? bonelabGameId,
                    ModfileId = newFile.Id,
                    Name = mod.Name,
                    Filename = newFile.Filename,
                    DownloadedAt = DateTime.UtcNow
                };

                // 2. If it's in the BONELAB Game Mods folder, extract and tag in place
                if (installed.IsGameFolder)
                {
                    ModDownloader.ExtractArchiveAndTag(tempZip, gameModsFolder, updatedManifest);
                    AnsiConsole.MarkupLine("[green][[UPDATED IN GAME FOLDER]][/]");
                }
                else
                {
                    // Update in download folder
                    string destZip = Path.Combine(installed.FolderPath, newFile.Filename);
                    if (File.Exists(destZip)) File.Delete(destZip);
                    File.Copy(tempZip, destZip, overwrite: true);
                    File.WriteAllText(Path.Combine(installed.FolderPath, "manifest.json"), JsonSerializer.Serialize(updatedManifest, ModDownloader.JsonOptions));
                    
                    if (extract)
                    {
                        ModDownloader.ExtractArchiveAndTag(destZip, installed.FolderPath, updatedManifest);
                    }
                    AnsiConsole.MarkupLine("[green][[UPDATED IN CACHE]][/]");
                }

                successCount++;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red][[FAILED: {Markup.Escape(ex.Message)}]][/]");
                failCount++;
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }
        }

        AnsiConsole.MarkupLine($"\nUpdate complete: [green]{successCount}[/] succeeded" + (failCount > 0 ? $", [red]{failCount}[/] failed." : "."));
    }

    private static InstalledModInfo? DetectModInDirectory(string dirPath, bool isGameFolder, string locationName)
    {
        try
        {
            string bonemm2Tag = Path.Combine(dirPath, isGameFolder ? ".bonemm2.json" : "manifest.json");
            if (!File.Exists(bonemm2Tag))
            {
                // Also check alt tag name
                bonemm2Tag = Path.Combine(dirPath, isGameFolder ? "manifest.json" : ".bonemm2.json");
            }

            if (File.Exists(bonemm2Tag))
            {
                var manifest = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(bonemm2Tag), ModDownloader.JsonOptions);
                if (manifest != null && manifest.ModId > 0)
                {
                    return new InstalledModInfo
                    {
                        FolderPath = dirPath,
                        LocationName = locationName,
                        IsGameFolder = isGameFolder,
                        Manifest = manifest,
                        DisplayName = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : Path.GetFileName(dirPath)
                    };
                }
            }

            // If in game folder and no manifest, check for BONELAB pallet.json
            string palletPath = Path.Combine(dirPath, "pallet.json");
            if (File.Exists(palletPath))
            {
                var pallet = JsonSerializer.Deserialize<BonelabPallet>(File.ReadAllText(palletPath), ModDownloader.JsonOptions);
                if (pallet != null)
                {
                    return new InstalledModInfo
                    {
                        FolderPath = dirPath,
                        LocationName = locationName,
                        IsGameFolder = isGameFolder,
                        Pallet = pallet,
                        DisplayName = pallet.Title ?? pallet.Barcode ?? Path.GetFileName(dirPath)
                    };
                }
            }
        }
        catch { }

        return null;
    }
}
