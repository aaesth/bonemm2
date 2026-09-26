using System.IO.Compression;
using Spectre.Console;

namespace Bonemm2;

public static class FusionInstaller
{
    private const string LabFusionDllUrl = "https://github.com/Lakatrazz/BONELAB-Fusion/releases/latest/download/LabFusion.dll";
    private const string FusionContentModIoUrl = "https://mod.io/g/bonelab/m/fusion-content";

    public static async Task Run(string? cliKey, string? cliBase)
    {
        AnsiConsole.MarkupLine("[bold cyan]=== BONELAB Fusion Quick Setup ===[/]");
        string gamePath = Helpers.GetGameFolder();

        // 1. Check if MelonLoader is installed
        string melonLoaderFolder = Path.Combine(gamePath, "MelonLoader");
        string melonLoaderDll = Path.Combine(gamePath, "MelonLoader", "MelonLoader.dll");

        if (!Directory.Exists(melonLoaderFolder) && !File.Exists(melonLoaderDll))
        {
            AnsiConsole.MarkupLine("\n[red][[ERROR]][/] MelonLoader was not detected in this BONELAB directory!");
            AnsiConsole.MarkupLine("Please run '[cyan]MelonLoader Setup[/]' from the main menu first before installing Fusion.");
            return;
        }

        AnsiConsole.MarkupLine("\n[green][[CHECK]][/] MelonLoader detected successfully.");

        // 2. Prepare MelonLoader Mods directory
        string melonModsFolder = Path.Combine(gamePath, "Mods");
        Directory.CreateDirectory(melonModsFolder);
        string labFusionDestPath = Path.Combine(melonModsFolder, "LabFusion.dll");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "bonemm2-fusion-installer");

        // 3. Download LabFusion Code Mod (.dll)
        AnsiConsole.MarkupLine("\nDownloading latest LabFusion code mod (LabFusion.dll)...");
        try
        {
            using var response = await http.GetAsync(LabFusionDllUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? 0;

            await using (var httpStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(labFusionDestPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new RemainingTimeColumn(),
                        new SpinnerColumn())
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask("[green]Downloading LabFusion.dll[/]", maxValue: totalBytes > 0 ? totalBytes : 100);
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await httpStream.ReadAsync(buffer)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, read));
                            if (totalBytes > 0)
                                task.Increment(read);
                        }
                    });
                AnsiConsole.MarkupLine("\n[green][[DONE]][/] Installed LabFusion.dll into MelonLoader Mods folder.");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red][[ERROR]][/] Failed to download LabFusion code mod: {Markup.Escape(ex.Message)}");
            return;
        }

        // 4. Download Fusion Content from mod.io into user's Mods directory
        AnsiConsole.MarkupLine("\nDownloading Fusion Content from mod.io...");
        string gameModsFolder = Helpers.GetDefaultBonelabModsFolder();
        
        try
        {
            await ModDownloader.Run(FusionContentModIoUrl, gameModsFolder, extract: true, maxParallel: 4, cliKey: cliKey, cliBase: cliBase);
            AnsiConsole.MarkupLine("\n[green][[SUCCESS]][/] Fusion code mod and mod.io Content installed successfully!");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red][[ERROR]][/] Failed to download Fusion mod.io content: {Markup.Escape(ex.Message)}");
        }
    }
}