using Bonemm2;

Updater.CleanupOldBinary();

string? directUrl = null;
string outputDir = "./bonelab_mods";
string? apiKeyArg = null;
string? apiBaseArg = null;
bool extract = false;
bool extractToGame = false;
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
        case "--dest-game": extractToGame = true; break;
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

if (skipMenu && directUrl != null)
{
    await ModDownloader.Run(directUrl, outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
    if (extractToGame)
    {
        ModDownloader.ExtractAllToGameFolder(outputDir);
    }
}
else
{
    await RunMenu();
}

async Task RunMenu()
{
    int selected = 0;
    string[] options = { 
        "1. Mod Downloader", 
        "2. Extract All Downloads to BONELAB Mods Folder", 
        "3. MelonLoader Setup", 
        "4. Fusion Quick Setup (Multiplayer)", 
        "5. Download BONELAB Game", 
        "6. Settings", 
        "7. Check for Updates", 
        "8. Exit" 
    };

    while (true)
    {
        Console.Clear();
        Console.WriteLine("=== BONEMM2 ===");
        Console.WriteLine("Use Up/Down arrows to select, Enter to confirm (or type number and press Enter).\n");

        for (int i = 0; i < options.Length; i++)
        {
            if (i == selected)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  > {options[i]}");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"    {options[i]}");
            }
        }

        ConsoleKey key = ConsoleKey.Enter;
        
        try
        {
            key = Console.ReadKey(true).Key;
            
            if (key == ConsoleKey.UpArrow) selected = Math.Max(0, selected - 1);
            if (key == ConsoleKey.DownArrow) selected = Math.Min(options.Length - 1, selected + 1);
            
            if (key == ConsoleKey.D1 || key == ConsoleKey.NumPad1) { selected = 0; key = ConsoleKey.Enter; }
            if (key == ConsoleKey.D2 || key == ConsoleKey.NumPad2) { selected = 1; key = ConsoleKey.Enter; }
            if (key == ConsoleKey.D3 || key == ConsoleKey.NumPad3) { selected = 2; key = ConsoleKey.Enter; }
            if (key == ConsoleKey.D4 || key == ConsoleKey.NumPad4) { selected = 3; key = ConsoleKey.Enter; }
            if (key == ConsoleKey.D5 || key == ConsoleKey.NumPad5) { selected = 4; key = ConsoleKey.Enter; }
            if (key == ConsoleKey.D6 || key == ConsoleKey.NumPad6) { selected = 5; key = ConsoleKey.Enter; }
            if (key == ConsoleKey.D7 || key == ConsoleKey.NumPad7) { selected = 6; key = ConsoleKey.Enter; }
            if (key == ConsoleKey.D8 || key == ConsoleKey.NumPad8) { selected = 7; key = ConsoleKey.Enter; }
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
        {
            Console.Write("\n[Basic Mode] Type number (1-8) and press Enter: ");
            string? input = Console.ReadLine()?.Trim();
            
            if (int.TryParse(input, out int parsed) && parsed >= 1 && parsed <= 8)
            {
                selected = parsed - 1;
                key = ConsoleKey.Enter;
            }
            else
            {
                continue;
            }
        }

        if (key == ConsoleKey.Enter)
        {
            Console.Clear();
            if (selected == 0) await ModDownloader.Run(null, outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
            else if (selected == 1) ModDownloader.ExtractAllToGameFolder(outputDir);
            else if (selected == 2) await MelonLoaderSetup.Run();
            else if (selected == 3) await FusionInstaller.Run(apiKeyArg, apiBaseArg);
            else if (selected == 4) await GameInstaller.Run();
            else if (selected == 5) SettingsManager.RunSettingsMenu();
            else if (selected == 6) await Updater.CheckForUpdatesAsync();
            else if (selected == 7) break;

            Console.WriteLine("\nPress any key or press Enter to return to the menu...");
            try { Console.ReadKey(true); } catch { Console.ReadLine(); }
        }
    }
}

void PrintUsage()
{
    Console.WriteLine("""
        Usage:
          dotnet run
          dotnet run -- [collection_or_mod_url] [-o OUTPUT_DIR] [--extract] [--dest-game] [-p PARALLEL_COUNT]

        Example:
          dotnet run -- https://mod.io/g/bonelab/c/my-favorite-mods --dest-game
        """);
}