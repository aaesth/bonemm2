using Bonemm2;

// 1. Cleanup old binary from a previous update if it exists
Updater.CleanupOldBinary();

// 2. Argument parsing
string? directUrl = null;
string outputDir = "./bonelab_mods";
string? apiKeyArg = null;
string? apiBaseArg = null;
bool extract = false;
int maxParallel = 4;
bool skipMenu = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-o": case "--output": outputDir = args[++i]; break;
        case "--api-key": apiKeyArg = args[++i]; break;
        case "--api-base": apiBaseArg = args[++i]; break;
        case "--extract": extract = true; break;
        case "-p": case "--parallel": if (int.TryParse(args[++i], out int p)) maxParallel = p; break;
        case "-h": case "--help": PrintUsage(); return;
        default:
            if (directUrl is null && !args[i].StartsWith("-"))
            {
                directUrl = args[i];
                skipMenu = true;
            }
            break;
    }
}

Directory.CreateDirectory(outputDir);
outputDir = Path.GetFullPath(outputDir);

// 3. Main Flow
if (skipMenu && directUrl != null)
{
    await ModDownloader.Run(directUrl, outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
}
else
{
    await RunMenu();
}

async Task RunMenu()
{
    int selected = 0;
    string[] options = { "1. Mod Downloader", "2. MelonLoader Setup", "3. Settings", "4. Check for Updates", "5. Exit" };

    while (true)
    {
        Console.Clear();
        Console.WriteLine("bonemm2");

        for (int i = 0; i < options.Length; i++)
        {
            if (i == selected)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"> {options[i]}");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"    {options[i]}");
            }
        }

        var key = Console.ReadKey(true).Key;
        if (key == ConsoleKey.UpArrow) selected = Math.Max(0, selected - 1);
        if (key == ConsoleKey.DownArrow) selected = Math.Min(options.Length - 1, selected + 1);
        
        if (key == ConsoleKey.Enter)
        {
            Console.Clear();
            if (selected == 0) await ModDownloader.Run(null, outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
            else if (selected == 1) await MelonLoaderSetup.Run();
            else if (selected == 2) SettingsManager.RunSettingsMenu();
            else if (selected == 3) await Updater.CheckForUpdatesAsync();
            else if (selected == 4) break;

            Console.WriteLine("\nPress any key to return to the menu...");
            Console.ReadKey(true);
        }
    }
}

void PrintUsage()
{
    Console.WriteLine("""
        Usage:
          dotnet run
          dotnet run -- [collection_or_mod_url] [-o OUTPUT_DIR] [--extract] [-p PARALLEL_COUNT]

        Example:
          dotnet run -- https://mod.io/g/bonelab/c/my-favorite-mods -o "C:\BonelabMods" --extract -p 8
          dotnet run -- https://mod.io/g/bonelab/m/rexmeck-weapon-pack
        """);
}