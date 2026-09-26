using System.IO.Compression;
using Spectre.Console;

namespace Bonemm2;

public static class MelonLoaderSetup
{
    public static async Task Run()
    {
        AnsiConsole.MarkupLine("[bold cyan]=== MelonLoader Setup ===[/]");
        string gamePath = Helpers.GetGameFolder();

        string downloadUrl = "https://github.com/LavaGang/MelonLoader/releases/latest/download/MelonLoader.x64.zip";
        string zipPath = Path.Combine(gamePath, "MelonLoader.x64.zip");

        using var mlHttp = new HttpClient();
        mlHttp.DefaultRequestHeaders.Add("User-Agent", "bonemm2-cli");

        AnsiConsole.MarkupLine("\nDownloading latest MelonLoader (x64)...");
        
        try
        {
            using (var response = await mlHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? 0;

                await using var httpStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

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
                        var task = ctx.AddTask("[green]Downloading MelonLoader.x64.zip[/]", maxValue: totalBytes > 0 ? totalBytes : 100);
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await httpStream.ReadAsync(buffer)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, read));
                            if (totalBytes > 0)
                                task.Increment(read);
                        }
                    });
            }

            AnsiConsole.MarkupLine("\n[cyan]Extracting to game folder...[/]");
            ZipFile.ExtractToDirectory(zipPath, gamePath, overwriteFiles: true);

            AnsiConsole.MarkupLine("[grey]Cleaning up zip archive...[/]");
            File.Delete(zipPath);

            AnsiConsole.MarkupLine("\n[green][[SUCCESS]][/] MelonLoader installed!");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red][[ERROR]][/] Failed to install MelonLoader: {Markup.Escape(ex.Message)}");
            if (File.Exists(zipPath))
            {
                try { File.Delete(zipPath); } catch { }
            }
        }
    }
}