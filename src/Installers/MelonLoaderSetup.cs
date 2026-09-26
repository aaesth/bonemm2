using System.IO.Compression;

namespace Bonemm2;

public static class MelonLoaderSetup
{
    public static async Task Run()
    {
        Console.WriteLine("=== MelonLoader Setup ===");
        string gamePath = Helpers.GetGameFolder();

        string downloadUrl = "https://github.com/LavaGang/MelonLoader/releases/latest/download/MelonLoader.x64.zip";
        string zipPath = Path.Combine(gamePath, "MelonLoader.x64.zip");

        using var mlHttp = new HttpClient();
        mlHttp.DefaultRequestHeaders.Add("User-Agent", "bonemm2-cli");

        Console.WriteLine("\nDownloading latest MelonLoader (x64)...");
        
        try
        {
            using (var response = await mlHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? 0;
                long bytesDownloaded = 0;

                await using (var httpStream = await response.Content.ReadAsStreamAsync())
                await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
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
                    Console.WriteLine();
                }
            }

            Console.WriteLine("Extracting to game folder...");
            ZipFile.ExtractToDirectory(zipPath, gamePath, overwriteFiles: true);

            Console.WriteLine("Cleaning up zip archive...");
            File.Delete(zipPath);

            Console.WriteLine("\n[SUCCESS] MelonLoader installed!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Failed to install MelonLoader: {ex.Message}");
        }
    }
}