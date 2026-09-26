using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using Spectre.Console;

namespace Bonemm2;

public static class Updater
{
    private const string RepoOwner = "aaesth";
    private const string RepoName = "bonemm2";

    public static void CleanupOldBinary()
    {
        try
        {
            string? currentExePath = Environment.ProcessPath;
            if (currentExePath != null)
            {
                string oldFilePath = currentExePath + ".old";
                if (File.Exists(oldFilePath)) File.Delete(oldFilePath);
            }
        }
        catch { /* Ignore lock errors */ }
    }

    public static async Task CheckForUpdatesAsync(bool silentIfLatest = false)
    {
        string currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        if (!silentIfLatest)
        {
            AnsiConsole.MarkupLine($"Checking latest version from GitHub... (Current: [cyan]v{currentVersion}[/])");
        }

        try
        {
            using var client = new HttpClient { Timeout = silentIfLatest ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("User-Agent", "bonemm2-updater");

            // Fetch ALL releases (includes pre-releases)
            string apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
            var releases = await client.GetFromJsonAsync<List<GitHubRelease>>(apiUrl);

            if (releases == null || releases.Count == 0)
            {
                if (!silentIfLatest)
                    AnsiConsole.MarkupLine("\n[yellow][[INFO]][/] No releases found on GitHub.");
                return;
            }

            // Get the absolute newest release (top of the list)
            var latestRelease = releases.First();
            string tag = latestRelease.TagName;

            string cleanTag = tag.TrimStart('v');
            
            if (Version.TryParse(cleanTag, out var latestVersion) && Version.TryParse(currentVersion, out var localVersion))
            {
                if (latestVersion <= localVersion)
                {
                    if (!silentIfLatest)
                        AnsiConsole.MarkupLine("[green]You are running the latest version![/]");
                    return;
                }
            }
            else if (string.Equals(cleanTag, currentVersion, StringComparison.OrdinalIgnoreCase))
            {
                if (!silentIfLatest)
                    AnsiConsole.MarkupLine("[green]You are running the latest version![/]");
                return;
            }

            if (!silentIfLatest)
            {
                AnsiConsole.MarkupLine($"Latest release on GitHub: [cyan]{tag}[/]{(latestRelease.Prerelease ? " [yellow][[Pre-release]][/]" : "")}");
            }

            AnsiConsole.MarkupLine($"\n[bold green][[UPDATE AVAILABLE]][/] Version [bold cyan]{tag}[/] is ready!");
            if (!AnsiConsole.Confirm("Would you like to update now?", defaultValue: true))
                return;

            // Prefer the platform-specific binary; only fall back to a zip as a last resort.
            GitHubAsset? asset;
            if (OperatingSystem.IsWindows())
            {
                asset = latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                     ?? latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                asset = latestRelease.Assets.FirstOrDefault(a =>
                            !a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            !a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            a.Name.StartsWith("bonemm2", StringComparison.OrdinalIgnoreCase))
                     ?? latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            }

            string downloadUrl = asset?.BrowserDownloadUrl
                ?? $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}/"
                 + (OperatingSystem.IsWindows() ? "bonemm2.exe" : "bonemm2");

            bool isZip = asset?.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ?? false;
            await ApplyUpdateAsync(client, downloadUrl, isZip);
        }
        catch (Exception ex)
        {
            if (!silentIfLatest)
            {
                AnsiConsole.MarkupLine($"\n[red]Update check failed: {Markup.Escape(ex.Message)}[/]");
            }
        }
    }

    private static async Task ApplyUpdateAsync(HttpClient client, string downloadUrl, bool isZip = false)
    {
        string currentExePath = Process.GetCurrentProcess().MainModule?.FileName 
                                ?? Environment.ProcessPath 
                                ?? throw new InvalidOperationException("Could not detect executable path.");
        
        string tempFilePath = currentExePath + ".tmp";
        string oldFilePath = currentExePath + ".old";

        AnsiConsole.MarkupLine($"\nDownloading binary payload from:\n[dim]{downloadUrl}[/]");
        
        using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"\n[red][[ERROR]][/] Failed to download asset (HTTP {(int)response.StatusCode}).");
                return;
            }

            long totalBytes = response.Content.Headers.ContentLength ?? 0;

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            
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
                    var task = ctx.AddTask("[green]Downloading update[/]", maxValue: totalBytes > 0 ? totalBytes : 100);
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read));
                        if (totalBytes > 0)
                            task.Increment(read);
                    }
                });
        }

        // If the downloaded file is a zip, extract the correct binary from it.
        if (isZip)
        {
            string extractedBinary = ExtractBinaryFromZip(tempFilePath, Path.GetDirectoryName(currentExePath)!);
            File.Delete(tempFilePath);
            File.Move(extractedBinary, tempFilePath);
        }

        AnsiConsole.MarkupLine("[cyan]Applying update...[/]");
        if (File.Exists(oldFilePath)) File.Delete(oldFilePath);

        File.Move(currentExePath, oldFilePath);
        File.Move(tempFilePath, currentExePath);

        if (!OperatingSystem.IsWindows())
        {
            Process.Start("chmod", $"+x \"{currentExePath}\"")?.WaitForExit();
        }

        AnsiConsole.MarkupLine("\n[green][[SUCCESS]][/] Update applied successfully!");
        AnsiConsole.MarkupLine("Please run the application again to use the new version.");
        Environment.Exit(0);
    }

    /// <summary>
    /// Extracts the platform-appropriate binary from a zip archive and returns the path to it.
    /// </summary>
    private static string ExtractBinaryFromZip(string zipPath, string extractDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        // On Windows look for *.exe; on Linux look for a file without extension named bonemm2.
        var entry = OperatingSystem.IsWindows()
            ? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            : archive.Entries.FirstOrDefault(e =>
                !e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                e.Name.StartsWith("bonemm2", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            throw new InvalidOperationException("Could not find a suitable binary inside the update zip.");

        string dest = Path.Combine(extractDir, entry.Name + ".extracted");
        entry.ExtractToFile(dest, overwrite: true);
        return dest;
    }
}