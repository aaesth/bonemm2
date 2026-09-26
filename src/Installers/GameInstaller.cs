using System.IO.Compression;
using Spectre.Console;

namespace Bonemm2;

public static class GameInstaller
{
    private const string BonelabCdnUrl = "https://cdn.aesth.cc/files/bonelab.zip";

    public static async Task Run()
    {
        AnsiConsole.MarkupLine("[bold cyan]=== BONELAB Game Downloader ===[/]");
        AnsiConsole.MarkupLine($"Source: [link={BonelabCdnUrl}]{BonelabCdnUrl}[/]\n");

        string inputPath = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter target installation folder path:\n> ")
                .PromptStyle("cyan"));

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            AnsiConsole.MarkupLine("[red][[ERROR]][/] Installation path cannot be empty.");
            return;
        }

        string targetDir = Path.GetFullPath(inputPath.Trim().Trim('"', '\''));
        Directory.CreateDirectory(targetDir);

        string zipPath = Path.Combine(targetDir, "bonelab_game.zip");

        using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        client.DefaultRequestHeaders.Add("User-Agent", "bonemm2-downloader");

        AnsiConsole.MarkupLine($"\nDownloading BONELAB archive to:\n  [grey]{Markup.Escape(zipPath)}[/]\n");

        try
        {
            using (var response = await client.GetAsync(BonelabCdnUrl, HttpCompletionOption.ResponseHeadersRead))
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
                        var task = ctx.AddTask("[green]Downloading BONELAB[/]", maxValue: totalBytes > 0 ? totalBytes : 100);
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

            AnsiConsole.MarkupLine("\n[cyan]Extracting game files...[/]");
            ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);

            AnsiConsole.MarkupLine("[grey]Cleaning up archive...[/]");
            File.Delete(zipPath);

            AnsiConsole.MarkupLine($"\n[green][[SUCCESS]][/] BONELAB downloaded and extracted to:\n  [cyan]{Markup.Escape(targetDir)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red][[ERROR]][/] Download/Extraction failed: {Markup.Escape(ex.Message)}");
            if (File.Exists(zipPath))
            {
                try { File.Delete(zipPath); } catch { }
            }
        }
    }
}