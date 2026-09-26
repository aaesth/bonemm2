using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

namespace Bonemm2;

public static class ModDownloader
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static async Task Run(string? targetUrl, string outputDir, bool extract, int maxParallel, string? cliKey, string? cliBase, bool updateMode = false)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            targetUrl = AnsiConsole.Prompt(
                new TextPrompt<string>("Paste the mod.io collection OR single mod link:\n> ")
                    .PromptStyle("cyan"));
            if (string.IsNullOrWhiteSpace(targetUrl)) return;
        }

        var (apiKey, apiBase) = SettingsManager.GetCredentials(cliKey, cliBase);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.Add("X-Modio-Platform", "windows");

        var (game, plan) = await ResolvePlanAsync(http, apiBase, apiKey, targetUrl);
        if (game is null || plan.Count == 0) return;

        long totalBytes = 0;
        int unavailableCount = 0;

        foreach (var p in plan)
        {
            if (p.Modfile?.Download?.BinaryUrl is null) unavailableCount++;
            else if (p.Modfile.Filesize.HasValue) totalBytes += p.Modfile.Filesize.Value;
        }

        AnsiConsole.MarkupLine($"\n[bold cyan]{plan.Count}[/] mod(s) total, [bold green]{Helpers.HumanSize(totalBytes)}[/] to download");
        if (unavailableCount > 0)
        {
            AnsiConsole.MarkupLine($"[yellow][!] {unavailableCount} mod(s) have no available Windows download file.[/]");
        }

        if (!updateMode && !AnsiConsole.Confirm("Start download?", defaultValue: true))
            return;

        await ExecuteParallelDownload(http, game, plan, outputDir, extract, maxParallel, totalBytes, updateMode, targetUrl);
    }

    public static async Task<(GameObject? game, List<(ModObject Mod, string Name, ModfileObject? Modfile)> plan)> ResolvePlanAsync(
        HttpClient http, string apiBase, string apiKey, string targetUrl)
    {
        var parsed = Helpers.ParseUrl(targetUrl);
        
        if (parsed.Type == TargetType.Unknown)
        {
            AnsiConsole.MarkupLine("\n[red]Couldn't understand that link.[/] Supported formats:");
            AnsiConsole.MarkupLine("  [dim]https://mod.io/g/bonelab/c/my-collection[/]");
            AnsiConsole.MarkupLine("  [dim]https://mod.io/g/bonelab/m/my-mod[/]");
            return (null, new());
        }

        AnsiConsole.MarkupLine($"\nLooking up game '[cyan]{Markup.Escape(parsed.GameSlug)}[/]'...");
        var gamesResp = await ApiGet<PagedResponse<GameObject>>(http, apiBase, "/games", apiKey, ("name_id", parsed.GameSlug));
        var game = gamesResp.Data.FirstOrDefault();
        
        if (game is null)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't find game '{Markup.Escape(parsed.GameSlug)}'.[/]");
            return (null, new());
        }
        
        var mods = new List<ModObject>();
        if (parsed.Type == TargetType.Collection)
        {
            AnsiConsole.MarkupLine($"Looking up collection '[cyan]{Markup.Escape(parsed.TargetSlug)}[/]'...");
            var colResp = await ApiGet<PagedResponse<CollectionObject>>(http, apiBase, $"/games/{game.Id}/collections", apiKey, ("name_id", parsed.TargetSlug));
            var collection = colResp.Data.FirstOrDefault();
            if (collection is null)
            {
                AnsiConsole.MarkupLine("[red]Collection not found.[/]");
                return (game, new());
            }

            mods = await GetAllPages<ModObject>(http, apiBase, $"/games/{game.Id}/collections/{collection.Id}/mods", apiKey);
        }
        else
        {
            AnsiConsole.MarkupLine($"Looking up mod '[cyan]{Markup.Escape(parsed.TargetSlug)}[/]'...");
            var modsResp = await ApiGet<PagedResponse<ModObject>>(http, apiBase, $"/games/{game.Id}/mods", apiKey, ("name_id", parsed.TargetSlug));
            var mod = modsResp.Data.FirstOrDefault();
            if (mod is null)
            {
                AnsiConsole.MarkupLine("[red]Mod not found.[/]");
                return (game, new());
            }
            mods.Add(mod);
        }

        if (mods.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No mods found to download.[/]");
            return (game, new());
        }

        var plan = new List<(ModObject Mod, string Name, ModfileObject? Modfile)>();
        foreach (var mod in mods)
        {
            string name = string.IsNullOrWhiteSpace(mod.Name) ? $"mod-{mod.Id}" : mod.Name;
            ModfileObject? modfile = mod.Modfile;
            if (modfile?.Download?.BinaryUrl is null || !SupportsWindows(modfile))
                modfile = await GetWindowsModfile(http, apiBase, apiKey, game.Id, mod.Id);
            plan.Add((mod, name, modfile));
        }

        return (game, plan);
    }

    public static async Task ExecuteParallelDownload(
        HttpClient http,
        GameObject game,
        List<(ModObject Mod, string Name, ModfileObject? Modfile)> plan,
        string outputDir,
        bool extract,
        int maxParallel,
        long totalBytes,
        bool updateMode = false,
        string? sourceUrl = null)
    {
        Directory.CreateDirectory(outputDir);
        AnsiConsole.MarkupLine($"\nStarting downloads (up to [cyan]{maxParallel}[/] concurrently)...");

        var downloadLog = new DownloadLog
        {
            SourceUrl = sourceUrl
        };
        var failures = new ConcurrentBag<string>();
        var freeSlots = new ConcurrentQueue<int>(Enumerable.Range(0, maxParallel));

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
                var overallTask = ctx.AddTask($"[bold cyan]Overall Progress[/]", maxValue: totalBytes > 0 ? totalBytes : Math.Max(1, plan.Count));
                var slotTasks = Enumerable.Range(0, maxParallel)
                    .Select(i => ctx.AddTask($"[grey]Slot {i + 1}: Idle[/]", maxValue: 100))
                    .ToArray();

                await Parallel.ForEachAsync(plan, new ParallelOptions { MaxDegreeOfParallelism = maxParallel }, async (item, ct) =>
                {
                    var (mod, name, modfile) = item;
                    long expectedSize = modfile?.Filesize ?? 0;

                    freeSlots.TryDequeue(out int slotIndex);
                    var task = slotTasks[slotIndex];
                    task.MaxValue = expectedSize > 0 ? expectedSize : 100;
                    task.Value = 0;
                    task.Description = Markup.Escape(Helpers.Truncate(name, 35));

                    try
                    {
                        if (modfile?.Download?.BinaryUrl is null)
                        {
                            failures.Add($"{name} (No binary available)");
                            downloadLog.Results.Add(new DownloadLogEntry { Name = name, Status = "failed", Error = "No binary available" });
                            return;
                        }

                        string safeName = Helpers.SafeFolderName(name);
                        string filename = string.IsNullOrWhiteSpace(modfile.Filename) ? $"{safeName}.zip" : modfile.Filename;
                        string modFolder = Path.Combine(outputDir, safeName);
                        Directory.CreateDirectory(modFolder);
                        string destPath = Path.Combine(modFolder, filename);

                        // If not in updateMode, skip if already exists with matching size
                        if (!updateMode && File.Exists(destPath) && expectedSize > 0 && new FileInfo(destPath).Length == expectedSize)
                        {
                            task.Increment(expectedSize);
                            overallTask.Increment(expectedSize);
                            downloadLog.Results.Add(new DownloadLogEntry
                            {
                                Name = name,
                                Status = "skipped",
                                Bytes = expectedSize,
                                Path = destPath
                            });
                            return;
                        }

                        await DownloadFileChunked(http, modfile.Download.BinaryUrl, destPath, bytesRead =>
                        {
                            task.Increment(bytesRead);
                            overallTask.Increment(bytesRead);
                        });

                        // Write mod manifest for update tracking
                        var manifest = new ModManifest
                        {
                            ModId = mod.Id,
                            GameId = game.Id,
                            ModfileId = modfile.Id,
                            Name = name,
                            Filename = filename,
                            DownloadedAt = DateTime.UtcNow
                        };
                        try
                        {
                            File.WriteAllText(Path.Combine(modFolder, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
                        }
                        catch { }

                        if (extract && Path.GetExtension(destPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                ZipFile.ExtractToDirectory(destPath, modFolder, overwriteFiles: true);
                            }
                            catch (InvalidDataException)
                            {
                                failures.Add($"{name} (Corrupt zip archive)");
                            }
                        }

                        downloadLog.Results.Add(new DownloadLogEntry
                        {
                            Name = name,
                            Status = "success",
                            Bytes = expectedSize > 0 ? expectedSize : new FileInfo(destPath).Length,
                            Path = destPath
                        });
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{name} ({ex.Message})");
                        downloadLog.Results.Add(new DownloadLogEntry
                        {
                            Name = name,
                            Status = "failed",
                            Error = ex.Message
                        });
                    }
                    finally
                    {
                        task.Description = $"[grey]Slot {slotIndex + 1}: Idle[/]";
                        task.Value = 0;
                        freeSlots.Enqueue(slotIndex);
                    }
                });
            });

        // Save log
        DownloadLogger.Write(outputDir, downloadLog);

        if (!failures.IsEmpty)
        {
            AnsiConsole.MarkupLine("\n[yellow][!] Some items had issues:[/]");
            foreach (var fail in failures)
            {
                AnsiConsole.MarkupLine($"  [red]•[/] {Markup.Escape(fail)}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("\n[green][[SUCCESS]][/] All mods processed successfully!");
        }
    }

    public static void ExtractAllToGameFolder(string sourceFolder)
    {
        AnsiConsole.MarkupLine("[bold cyan]=== Bulk Extract Mods ===[/]");
        
        string defaultPath = Helpers.GetDefaultBonelabModsFolder();
        AnsiConsole.MarkupLine($"Default BONELAB Mods path detected:\n  [green]{Markup.Escape(defaultPath)}[/]\n");
        
        string inputPath = AnsiConsole.Prompt(
            new TextPrompt<string>("Press Enter to use default path, or paste custom Mods path:\n> ")
                .AllowEmpty());
        string targetPath = string.IsNullOrWhiteSpace(inputPath) ? defaultPath : inputPath.Trim().Trim('"', '\'');

        if (!Directory.Exists(sourceFolder))
        {
            AnsiConsole.MarkupLine($"\n[red][[ERROR]][/] Download directory '{Markup.Escape(sourceFolder)}' does not exist yet.");
            return;
        }

        Directory.CreateDirectory(targetPath);

        string[] zipFiles = Directory.GetFiles(sourceFolder, "*.zip", SearchOption.AllDirectories);
        if (zipFiles.Length == 0)
        {
            AnsiConsole.MarkupLine($"\n[yellow]No .zip files found in '{Markup.Escape(sourceFolder)}'.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"\nExtracting [cyan]{zipFiles.Length}[/] mod archive(s) to:\n  [grey]{Markup.Escape(targetPath)}[/]\n");

        int success = 0;
        int failed = 0;

        foreach (var zipPath in zipFiles)
        {
            string fileName = Path.GetFileName(zipPath);
            try
            {
                AnsiConsole.Markup($"Extracting [bold]{Markup.Escape(fileName)}[/]... ");
                ZipFile.ExtractToDirectory(zipPath, targetPath, overwriteFiles: true);
                AnsiConsole.MarkupLine("[green][[DONE]][/]");
                success++;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red][[FAILED: {Markup.Escape(ex.Message)}]][/]");
                failed++;
            }
        }

        AnsiConsole.MarkupLine($"\nBulk extraction complete! Successfully extracted: [green]{success}[/], Failed: [red]{failed}[/]");
    }

    private static async Task DownloadFileChunked(HttpClient client, string url, string destPath, Action<int> onProgress)
    {
        string tmpPath = destPath + ".part";
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var httpStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            int read;
            while ((read = await httpStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                onProgress(read);
            }

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmpPath, destPath);
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    public static async Task<T> ApiGet<T>(HttpClient client, string apiBase, string path, string apiKey, params (string key, string value)[] queryParams)
    {
        var query = string.Join("&", queryParams.Select(p => $"{Uri.EscapeDataString(p.key)}={Uri.EscapeDataString(p.value)}"));
        string url = $"{apiBase}{path}?api_key={Uri.EscapeDataString(apiKey)}" + (query.Length > 0 ? $"&{query}" : "");

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            AnsiConsole.MarkupLine($"\n[red]mod.io API error {(int)response.StatusCode}:[/] {Markup.Escape(body[..Math.Min(300, body.Length)])}");
            Environment.Exit(1);
        }
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    public static async Task<List<T>> GetAllPages<T>(HttpClient client, string apiBase, string path, string apiKey)
    {
        var results = new List<T>();
        int offset = 0;
        while (true)
        {
            var page = await ApiGet<PagedResponse<T>>(client, apiBase, path, apiKey, ("_limit", "100"), ("_offset", offset.ToString()));
            if (page.Data.Count == 0) break;
            results.AddRange(page.Data);
            offset += 100;
            if (offset >= page.ResultTotal) break;
        }
        return results;
    }

    public static async Task<ModfileObject?> GetWindowsModfile(HttpClient client, string apiBase, string apiKey, int gameId, int modId)
    {
        var page = await ApiGet<PagedResponse<ModfileObject>>(client, apiBase, $"/games/{gameId}/mods/{modId}/files", apiKey, ("_sort", "-date_added"), ("_limit", "10"));
        return page.Data.FirstOrDefault(SupportsWindows) ?? page.Data.FirstOrDefault();
    }

    public static bool SupportsWindows(ModfileObject? modfile)
    {
        if (modfile?.Platforms is null || modfile.Platforms.Count == 0) return true;
        return modfile.Platforms.Any(p => p.Platform.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase) || p.Platform.Equals("ALL", StringComparison.OrdinalIgnoreCase));
    }
}