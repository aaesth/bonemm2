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
    await Updater.CheckForUpdatesAsync(silentIfLatest: true);
    await RunMenu();
}

async Task RunMenu()
{
    while (true)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[grey]bonemm2 v1.3.1[/]\n");

        var category = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold cyan]Main Menu[/] - Select a category:")
                .AddChoices(
                    "Mods",
                    "Code Mods (Fusion & MelonLoader)",
                    "Settings & System",
                    "Exit"));

        if (category.StartsWith("Mods"))
        {
            await RunModsMenu();
        }
        else if (category.StartsWith("Code Mods"))
        {
            await RunCodeModsMenu();
        }
        else if (category.StartsWith("Settings"))
        {
            await RunSettingsSubMenu();
        }
        else if (category.StartsWith("Exit"))
        {
            break;
        }
    }
}

async Task RunModsMenu()
{
    while (true)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[bold cyan]=== Mods ===[/]\n");

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a mod action:")
                .PageSize(10)
                .AddChoices(
                    "1. Mod Downloader (Paste Link)",
                    "2. Search Mods (mod.io)",
                    "3. List / Inspect Mods",
                    "4. Update Installed Mods",
                    "5. Uninstall Mods",
                    "6. Extract Downloads to BONELAB Mods Folder",
                    "← Back to Main Menu"));

        if (choice.StartsWith("← Back")) return;

        AnsiConsole.Clear();

        if (choice.StartsWith("1.")) await ModDownloader.Run(null, outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("2.")) await ModSearch.Run(null, "bonelab", outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("3.")) await ModLister.Run(null, apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("4.")) await ModUpdater.Run(outputDir, extract, maxParallel, apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("5.")) ModUninstaller.Run(outputDir);
        else if (choice.StartsWith("6.")) ModDownloader.ExtractAllToGameFolder(outputDir);

        AnsiConsole.MarkupLine("\n[dim]Press Enter to continue...[/]");
        Console.ReadLine();
    }
}

async Task RunCodeModsMenu()
{
    while (true)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[bold cyan]=== Code Mods & Game Tools ===[/]\n");

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a tool:")
                .AddChoices(
                    "1. MelonLoader Setup",
                    "2. Fusion Quick Setup (Multiplayer)",
                    "3. Download BONELAB Game",
                    "← Back to Main Menu"));

        if (choice.StartsWith("← Back")) return;

        AnsiConsole.Clear();

        if (choice.StartsWith("1.")) await MelonLoaderSetup.Run();
        else if (choice.StartsWith("2.")) await FusionInstaller.Run(apiKeyArg, apiBaseArg);
        else if (choice.StartsWith("3.")) await GameInstaller.Run();

        AnsiConsole.MarkupLine("\n[dim]Press Enter to continue...[/]");
        Console.ReadLine();
    }
}

async Task RunSettingsSubMenu()
{
    while (true)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[bold cyan]=== Settings & System ===[/]\n");

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select an option:")
                .AddChoices(
                    "1. Configure Settings (API Key, Folders)",
                    "2. Check for Updates",
                    "← Back to Main Menu"));

        if (choice.StartsWith("← Back")) return;

        AnsiConsole.Clear();

        if (choice.StartsWith("1.")) SettingsManager.RunSettingsMenu();
        else if (choice.StartsWith("2.")) await Updater.CheckForUpdatesAsync();

        AnsiConsole.MarkupLine("\n[dim]Press Enter to continue...[/]");
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