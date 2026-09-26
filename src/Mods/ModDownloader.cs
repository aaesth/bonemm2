using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;

namespace Bonemm2;

public static class ModDownloader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task Run(string? targetUrl, string outputDir, bool extract, int maxParallel, string? cliKey, string? cliBase)
    {
        if (targetUrl == null)
        {
            Console.Write("Paste the mod.io collection OR single mod link:\n> ");
            targetUrl = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(targetUrl)) return;
        }

        var (apiKey, apiBase) = SettingsManager.GetCredentials(cliKey, cliBase);
        var parsed = Helpers.ParseUrl(targetUrl);
        
        if (parsed.Type == TargetType.Unknown)
        {
            Console.Error.WriteLine("\nCouldn't understand that link. Supported formats:");
            Console.Error.WriteLine("  https://mod.io/g/bonelab/c/my-collection");
            Console.Error.WriteLine("  https://mod.io/g/bonelab/m/my-mod");
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.Add("X-Modio-Platform", "windows");

        Console.WriteLine($"\nLooking up game '{parsed.GameSlug}'...");
        var gamesResp = await ApiGet<PagedResponse<GameObject>>(http, apiBase, "/games", apiKey, ("name_id", parsed.GameSlug));
        var game = gamesResp.Data.FirstOrDefault();
        
        if (game is null) { Console.Error.WriteLine($"Couldn't find game '{parsed.GameSlug}'."); return; }
        
        var mods = new List<ModObject>();
        if (parsed.Type == TargetType.Collection)
        {
            Console.WriteLine($"Looking up collection '{parsed.TargetSlug}'...");
            var colResp = await ApiGet<PagedResponse<CollectionObject>>(http, apiBase, $"/games/{game.Id}/collections", apiKey, ("name_id", parsed.TargetSlug));
            var collection = colResp.Data.FirstOrDefault();
            if (collection is null) { Console.Error.WriteLine("Collection not found."); return; }

            mods = await GetAllPages<ModObject>(http, apiBase, $"/games/{game.Id}/collections/{collection.Id}/mods", apiKey);
        }
        else
        {
            Console.WriteLine($"Looking up mod '{parsed.TargetSlug}'...");
            var modsResp = await ApiGet<PagedResponse<ModObject>>(http, apiBase, $"/games/{game.Id}/mods", apiKey, ("name_id", parsed.TargetSlug));
            var mod = modsResp.Data.FirstOrDefault();
            if (mod is null) { Console.Error.WriteLine("Mod not found."); return; }
            mods.Add(mod);
        }

        if (mods.Count == 0) { Console.WriteLine("Nothing to download."); return; }

        var plan = new List<(ModObject Mod, string Name, ModfileObject? Modfile)>();
        foreach (var mod in mods)
        {
            string name = string.IsNullOrWhiteSpace(mod.Name) ? $"mod-{mod.Id}" : mod.Name;
            ModfileObject? modfile = mod.Modfile;
            if (modfile?.Download?.BinaryUrl is null || !SupportsWindows(modfile))
                modfile = await GetWindowsModfile(http, apiBase, apiKey, game.Id, mod.Id);
            plan.Add((mod, name, modfile));
        }

        long totalBytes = 0;
        int unavailableCount = 0;

        foreach (var p in plan)
        {
            if (p.Modfile?.Download?.BinaryUrl is null) unavailableCount++;
            else if (p.Modfile.Filesize.HasValue) totalBytes += p.Modfile.Filesize.Value;
        }

        Console.WriteLine($"\n{plan.Count} mod(s) total, {Helpers.HumanSize(totalBytes)} to download");
        Console.Write("Start download? [Y/n]: ");
        if (Console.ReadLine()?.Trim().ToLowerInvariant() is "n" or "no") return;

        await ExecuteParallelDownload(http, plan, outputDir, extract, maxParallel, totalBytes);
    }

    public static void ExtractAllToGameFolder(string sourceFolder)
    {
        Console.WriteLine("=== Bulk Extract Mods ===");
        
        string defaultPath = Helpers.GetDefaultBonelabModsFolder();
        Console.WriteLine($"Default BONELAB Mods path detected:\n  {defaultPath}\n");
        Console.Write("Press Enter to use default path, or paste custom Mods path:\n> ");
        
        string? inputPath = Console.ReadLine()?.Trim().Trim('"', '\'');
        string targetPath = string.IsNullOrWhiteSpace(inputPath) ? defaultPath : inputPath;

        if (!Directory.Exists(sourceFolder))
        {
            Console.WriteLine($"\n[ERROR] Download directory '{sourceFolder}' does not exist yet.");
            return;
        }

        Directory.CreateDirectory(targetPath);

        string[] zipFiles = Directory.GetFiles(sourceFolder, "*.zip", SearchOption.AllDirectories);
        if (zipFiles.Length == 0)
        {
            Console.WriteLine($"\nNo .zip files found in '{sourceFolder}'.");
            return;
        }

        Console.WriteLine($"\nExtracting {zipFiles.Length} mod archive(s) to:\n  {targetPath}\n");

        int success = 0;
        int failed = 0;

        foreach (var zipPath in zipFiles)
        {
            string fileName = Path.GetFileName(zipPath);
            try
            {
                Console.Write($"Extracting {fileName}... ");
                ZipFile.ExtractToDirectory(zipPath, targetPath, overwriteFiles: true);
                Console.WriteLine("[DONE]");
                success++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAILED: {ex.Message}]");
                failed++;
            }
        }

        Console.WriteLine($"\nBulk extraction complete! Successfully extracted: {success}, Failed: {failed}");
    }

    private static async Task ExecuteParallelDownload(HttpClient http, List<(ModObject Mod, string Name, ModfileObject? Modfile)> plan, string outputDir, bool extract, int maxParallel, long totalBytes)
    {
        Console.WriteLine($"\nStarting downloads (up to {maxParallel} concurrently)...");
        for (int i = 0; i < maxParallel + 2; i++) Console.WriteLine();

        long sharedDownloadedBytes = 0;
        int sharedCompletedCount = 0;
        var failures = new ConcurrentBag<string>();
        object consoleLock = new object();

        var activeSlots = new SlotState[maxParallel];
        for (int i = 0; i < maxParallel; i++) activeSlots[i] = new SlotState();
        var freeSlots = new ConcurrentQueue<int>(Enumerable.Range(0, maxParallel));

        var uiCts = new CancellationTokenSource();
        var uiTask = Task.Run(async () =>
        {
            while (!uiCts.Token.IsCancellationRequested)
            {
                DrawModUI(totalBytes, plan.Count, ref sharedDownloadedBytes, ref sharedCompletedCount, maxParallel, activeSlots, consoleLock);
                try { await Task.Delay(100, uiCts.Token); } catch { }
            }
            DrawModUI(totalBytes, plan.Count, ref sharedDownloadedBytes, ref sharedCompletedCount, maxParallel, activeSlots, consoleLock);
        });

        await Parallel.ForEachAsync(plan, new ParallelOptions { MaxDegreeOfParallelism = maxParallel }, async (item, ct) =>
        {
            var (mod, name, modfile) = item;
            long expectedSize = modfile?.Filesize ?? 0;

            freeSlots.TryDequeue(out int slotIndex);
            var slot = activeSlots[slotIndex];
            slot.Reset(name, expectedSize);

            try
            {
                if (modfile?.Download?.BinaryUrl is null) { failures.Add($"{name} - no file available"); return; }

                string filename = string.IsNullOrWhiteSpace(modfile.Filename) ? $"{Helpers.SafeFolderName(name)}.zip" : modfile.Filename;
                string modFolder = Path.Combine(outputDir, Helpers.SafeFolderName(name));
                Directory.CreateDirectory(modFolder);
                string destPath = Path.Combine(modFolder, filename);

                if (File.Exists(destPath) && expectedSize > 0 && new FileInfo(destPath).Length == expectedSize)
                {
                    Interlocked.Add(ref sharedDownloadedBytes, expectedSize);
                    return; 
                }

                slot.CustomStatus = "Starting...";
                await DownloadFileChunked(http, modfile.Download.BinaryUrl, destPath, bytesRead => 
                {
                    slot.CustomStatus = null;
                    slot.BytesDownloaded += bytesRead;
                    Interlocked.Add(ref sharedDownloadedBytes, bytesRead);
                });

                if (extract && Path.GetExtension(destPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    slot.CustomStatus = "Extracting...";
                    try { ZipFile.ExtractToDirectory(destPath, modFolder, overwriteFiles: true); }
                    catch (InvalidDataException) { failures.Add($"{name} - bad zip file"); }
                }
            }
            catch (Exception ex) { failures.Add($"{name} - {ex.Message}"); }
            finally
            {
                slot.IsActive = false;
                freeSlots.Enqueue(slotIndex);
                Interlocked.Increment(ref sharedCompletedCount);
            }
        });

        uiCts.Cancel();
        await uiTask;
        Console.WriteLine("\n\nDone.");
    }

    private static void DrawModUI(long totalBytes, int totalMods, ref long sharedDownloadedBytes, ref int sharedCompletedCount, int maxParallel, SlotState[] activeSlots, object consoleLock)
    {
        if (Console.IsOutputRedirected) return;
        lock (consoleLock)
        {
            try
            {
                int width = Helpers.GetConsoleWidth();
                int bottomTop = Console.CursorTop; 
                int overallTop = Math.Max(0, bottomTop - (maxParallel + 2));

                long current = Interlocked.Read(ref sharedDownloadedBytes);
                int completed = Volatile.Read(ref sharedCompletedCount);
                double overallPct = totalBytes > 0 ? (double)current / totalBytes : (completed == totalMods ? 1 : 0);
                
                string overallLine = $"Overall: [{Helpers.GetBar(overallPct, 30)}] {overallPct:P0} | {completed}/{totalMods} mods | {Helpers.HumanSize(current)} / {Helpers.HumanSize(totalBytes)}";
                
                Console.SetCursorPosition(0, overallTop);
                Console.Write(overallLine.PadRight(width));
                Console.SetCursorPosition(0, overallTop + 1);
                Console.Write("".PadRight(width));

                for (int i = 0; i < maxParallel; i++)
                {
                    Console.SetCursorPosition(0, overallTop + 2 + i);
                    Console.Write(FormatSlot(i, activeSlots[i], width).PadRight(width));
                }
                Console.SetCursorPosition(0, bottomTop);
            }
            catch { }
        }
    }

    private static string FormatSlot(int index, SlotState slot, int consoleWidth)
    {
        string prefix = $"  [{index + 1}] ";
        if (!slot.IsActive) return $"{prefix}Waiting...";
        int maxNameLen = Math.Max(5, consoleWidth - 55); 
        string safeName = Helpers.Truncate(slot.Name, maxNameLen);

        if (slot.CustomStatus != null) return $"{prefix}{slot.CustomStatus,-16} {safeName}";

        double pct = slot.TotalBytes > 0 ? (double)slot.BytesDownloaded / slot.TotalBytes : 0;
        return $"{prefix}[{Helpers.GetBar(pct, 16)}] {pct,4:P0} | {Helpers.HumanSize(slot.BytesDownloaded),7} / {Helpers.HumanSize(slot.TotalBytes),7} | {safeName}";
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

    private static async Task<T> ApiGet<T>(HttpClient client, string apiBase, string path, string apiKey, params (string key, string value)[] queryParams)
    {
        var query = string.Join("&", queryParams.Select(p => $"{Uri.EscapeDataString(p.key)}={Uri.EscapeDataString(p.value)}"));
        string url = $"{apiBase}{path}?api_key={Uri.EscapeDataString(apiKey)}" + (query.Length > 0 ? $"&{query}" : "");

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            Console.Error.WriteLine($"\nmod.io API error {(int)response.StatusCode}: {body[..Math.Min(300, body.Length)]}");
            Environment.Exit(1);
        }
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static async Task<List<T>> GetAllPages<T>(HttpClient client, string apiBase, string path, string apiKey)
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

    private static async Task<ModfileObject?> GetWindowsModfile(HttpClient client, string apiBase, string apiKey, int gameId, int modId)
    {
        var page = await ApiGet<PagedResponse<ModfileObject>>(client, apiBase, $"/games/{gameId}/mods/{modId}/files", apiKey, ("_sort", "-date_added"), ("_limit", "10"));
        return page.Data.FirstOrDefault(SupportsWindows) ?? page.Data.FirstOrDefault();
    }

    private static bool SupportsWindows(ModfileObject? modfile)
    {
        if (modfile?.Platforms is null || modfile.Platforms.Count == 0) return true;
        return modfile.Platforms.Any(p => p.Platform.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase) || p.Platform.Equals("ALL", StringComparison.OrdinalIgnoreCase));
    }
}