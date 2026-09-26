using System.IO.Compression;

namespace Bonemm2;

public static class GameInstaller
{
    private const string BonelabCdnUrl = "https://cdn.aesth.cc/files/bonelab.zip";

    public static async Task Run()
    {
        Console.WriteLine("=== BONELAB Game Downloader ===");
        Console.WriteLine($"Source: {BonelabCdnUrl}\n");

        Console.Write("Enter target installation folder path:\n> ");
        string? inputPath = Console.ReadLine()?.Trim().Trim('"', '\'');

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            Console.WriteLine("[ERROR] Installation path cannot be empty.");
            return;
        }

        string targetDir = Path.GetFullPath(inputPath);
        Directory.CreateDirectory(targetDir);

        string zipPath = Path.Combine(targetDir, "bonelab_game.zip");

        using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        client.DefaultRequestHeaders.Add("User-Agent", "bonemm2-downloader");

        Console.WriteLine($"\nDownloading BONELAB archive to:\n  {zipPath}\n");

        try
        {
            using (var response = await client.GetAsync(BonelabCdnUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? 0;
                long bytesDownloaded = 0;

                await using var httpStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920]; // 80KB chunks
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

            Console.WriteLine("\nExtracting game files...");
            ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);

            Console.WriteLine("Cleaning up archive...");
            File.Delete(zipPath);

            Console.WriteLine($"\n[SUCCESS] BONELAB downloaded and extracted to:\n  {targetDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Download/Extraction failed: {ex.Message}");
            if (File.Exists(zipPath))
            {
                try { File.Delete(zipPath); } catch { }
            }
        }
    }
}