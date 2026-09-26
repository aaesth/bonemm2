using Bonemm2;
using Spectre.Console;

Updater.CleanupOldBinary();

string? directUrl = null;
string outputDir = "./bonelab_mods";
string? apiKeyArg = null;
string? apiBaseArg = null;
bool extract = false;
bool extractToGame = false;
int maxParallel = 4;
bool skipMenu = false;

bool runList = false;
bool runUpdate = false;
bool runUninstall = false;
string? searchQuery = null;

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
        case "--list":
            runList = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                directUrl = args[++i];
            skipMenu = true;
            break;
        case "--update":
            runUpdate = true;
            skipMenu = true;
            break;
        case "--search":
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                searchQuery = args[++i];
            skipMenu = true;
            break;
        case "--uninstall":
            runUninstall = true;
            skipMenu = true;
            break;
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

if (skipMenu)
{
    if (runList)
    {
        await ModLister.Run(directUrl, apiKeyArg, apiBaseArg);
    }
    else if (runUpdate)
    {
        await ModUpdater.Run(outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
    }
    else if (runUninstall)
    {
        ModUninstaller.Run(outputDir);
    }
    else if (searchQuery != null)
    {
        await ModSearch.Run(searchQuery, "bonelab", outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
    }
    else if (directUrl != null)
    {
        await ModDownloader.Run(directUrl, outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
        if (extractToGame)
        {
            ModDownloader.ExtractAllToGameFolder(outputDir);
        }
    }
}
else
{
    await RunMenu();
}

async Task RunMenu()
{
    while (true)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[grey]bonemm2 v1.3.0[/]\n");

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select an option:")
                .PageSize(12)
                .AddChoices(
                    "1. Mod Downloader",
                    "2. List Mods (Inspect Collection/Mod)",
                    "3. Search Mods (mod.io)",
                    "4. Update Installed Mods",
                    "5. Uninstall Mods",
                    "6. Extract All Downloads to BONELAB Mods Folder",
                    "7. MelonLoader Setup",
                    "8. Fusion Quick Setup (Multiplayer)",
                    "9. Download BONELAB Game",
                    "10. Settings",
                    "11. Check for Updates",
                    "12. Exit"));

        AnsiConsole.Clear();

        if (choice.StartsWith("1.")) await ModDownloader.Run(null, outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("2.")) await ModLister.Run(null, apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("3.")) await ModSearch.Run(null, "bonelab", outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("4.")) await ModUpdater.Run(outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("5.")) ModUninstaller.Run(outputDir);
        else if (choice.StartsWith("6.")) ModDownloader.ExtractAllToGameFolder(outputDir);
        else if (choice.StartsWith("7.")) await MelonLoaderSetup.Run();
        else if (choice.StartsWith("8.")) await FusionInstaller.Run(apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("9.")) await GameInstaller.Run();
        else if (choice.StartsWith("10.")) SettingsManager.RunSettingsMenu();
        else if (choice.StartsWith("11.")) await Updater.CheckForUpdatesAsync();
        else if (choice.StartsWith("12.")) break;

        AnsiConsole.MarkupLine("\n[dim]Press Enter to return to the menu...[/]");
        Console.ReadLine();
    }
}

void PrintUsage()
{
    AnsiConsole.MarkupLine("""
[bold cyan]bonemm2[/] - BONELAB Mod & Game Manager

[bold]Usage:[/]
  bonemm2
  bonemm2 [[link]] [[flags]]
  bonemm2 --list [[link]]
  bonemm2 --update
  bonemm2 --search <query>
  bonemm2 --uninstall

[bold]Flags:[/]
  [cyan]-o, --output <dir>[/]      Output directory for downloads (default: ./bonelab_mods)
  [cyan]-p, --parallel <count>[/]  Max concurrent downloads (default: 4)
  [cyan]--extract[/]               Extract downloaded zip files into mod subdirectories
  [cyan]--dest-game[/]             Automatically extract downloads directly to game Mods folder
  [cyan]--list [[link]][/]           Inspect and display contents of a collection or mod
  [cyan]--update[/]                Check mod.io and update outdated downloaded mods
  [cyan]--search <query>[/]        Search mod.io for BONELAB mods
  [cyan]--uninstall[/]             Interactively select and delete installed mods
  [cyan]--api-key <key>[/]         Override mod.io API key
  [cyan]--api-base <url>[/]        Override mod.io API base URL
  [cyan]-h, --help[/]              Show this help message
""");
}