using System.IO.Compression;

namespace Bonemm2;

public static class FusionInstaller
{
    private const string LabFusionDllUrl = "https://github.com/Lakatrazz/BONELAB-Fusion/releases/latest/download/LabFusion.dll";
    private const string FusionContentModIoUrl = "https://mod.io/g/bonelab/m/fusion-content";

    public static async Task Run(string? cliKey, string? cliBase)
    {
        Console.WriteLine("=== BONELAB Fusion Quick Setup ===");
        string gamePath = Helpers.GetGameFolder();

        // 1. Check if MelonLoader is installed
        string melonLoaderFolder = Path.Combine(gamePath, "MelonLoader");
        string melonLoaderDll = Path.Combine(gamePath, "MelonLoader", "MelonLoader.dll");

        if (!Directory.Exists(melonLoaderFolder) && !File.Exists(melonLoaderDll))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n[ERROR] MelonLoader was not detected in this BONELAB directory!");
            Console.ResetColor();
            Console.WriteLine("Please run 'MelonLoader Setup' from the main menu first before installing Fusion.");
            return;
        }

        Console.WriteLine("\n[CHECK] MelonLoader detected successfully.");

        // 2. Prepare MelonLoader Mods directory
        string melonModsFolder = Path.Combine(gamePath, "Mods");
        Directory.CreateDirectory(melonModsFolder);
        string labFusionDestPath = Path.Combine(melonModsFolder, "LabFusion.dll");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "bonemm2-fusion-installer");

        // 3. Download LabFusion Code Mod (.dll)
        Console.WriteLine("\nDownloading latest LabFusion code mod (LabFusion.dll)...");
        try
        {
            using var response = await http.GetAsync(LabFusionDllUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? 0;
            long bytesDownloaded = 0;

            await using (var httpStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(labFusionDestPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await httpStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    bytesDownloaded += read;

                    if (totalBytes > 0)
                    {
                        double pct = (double)bytesDownloaded / totalBytes;
                        Console.Write($"\r  [{Helpers.GetBar(pct, 30)}] {pct:P0} | {Helpers.HumanSize(bytesDownloaded)} / {Helpers.HumanSize(totalBytes)}");
                    }
                    else
                    {
                        Console.Write($"\r  Downloaded {Helpers.HumanSize(bytesDownloaded)}...");
                    }
                }
                Console.WriteLine("\n[DONE] Installed LabFusion.dll into MelonLoader Mods folder.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Failed to download LabFusion code mod: {ex.Message}");
            return;
        }

        // 4. Download Fusion Content from mod.io into user's Mods directory
        Console.WriteLine("\nDownloading Fusion Content from mod.io...");
        string gameModsFolder = Helpers.GetDefaultBonelabModsFolder();
        
        try
        {
            await ModDownloader.Run(FusionContentModIoUrl, gameModsFolder, extract: true, maxParallel: 4, cliKey: cliKey, cliBase: cliBase);
            Console.WriteLine("\n[SUCCESS] Fusion code mod and mod.io Content installed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Failed to download Fusion mod.io content: {ex.Message}");
        }
    }
}