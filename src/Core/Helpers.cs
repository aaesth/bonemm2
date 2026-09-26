using System.Text.RegularExpressions;
using Spectre.Console;

namespace Bonemm2;

public static class Helpers
{
    public static int GetConsoleWidth()
    {
        try { return Math.Max(50, Console.WindowWidth - 1); }
        catch { return 80; }
    }

    public static string GetBar(double pct, int size)
    {
        pct = Math.Clamp(pct, 0, 1);
        int filled = (int)(pct * size);
        return new string('#', filled) + new string('-', size - filled);
    }

    public static string Truncate(string val, int max)
    {
        if (val.Length <= max) return val;
        return val.Substring(0, max - 3) + "...";
    }

    public static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.0}{units[unit]}";
    }

    public static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "mod" : cleaned;
    }

    public static (TargetType Type, string GameSlug, string TargetSlug) ParseUrl(string url)
    {
        url = url.Trim();
        
        var colRegex = new[] {
            new Regex(@"mod\.io/g/([^/]+)/c/([^/?#]+)", RegexOptions.IgnoreCase),
            new Regex(@"([^./]+)\.mod\.io/c/([^/?#]+)", RegexOptions.IgnoreCase)
        };
        foreach (var r in colRegex)
        {
            var m = r.Match(url);
            if (m.Success) return (TargetType.Collection, m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.ToLowerInvariant());
        }

        var modRegex = new[] {
            new Regex(@"mod\.io/g/([^/]+)/m/([^/?#]+)", RegexOptions.IgnoreCase),
            new Regex(@"([^./]+)\.mod\.io/m/([^/?#]+)", RegexOptions.IgnoreCase),
            new Regex(@"([^./]+)\.mod\.io/([^/?#]+)", RegexOptions.IgnoreCase)
        };
        foreach (var r in modRegex)
        {
            var m = r.Match(url);
            if (m.Success) return (TargetType.Mod, m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.ToLowerInvariant());
        }

        return (TargetType.Unknown, "", "");
    }

    public static string GetDefaultBonelabModsFolder()
    {
        var saved = SettingsManager.LoadConfig();

        // 1. Check if we already have a saved mods_path that exists
        if (saved.TryGetValue("mods_path", out var savedMods) && !string.IsNullOrWhiteSpace(savedMods) && Directory.Exists(savedMods))
        {
            return savedMods;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 2. Try default locations
        if (OperatingSystem.IsLinux())
        {
            string[] possibleLinuxPaths = new[]
            {
                Path.Combine(home, ".local/share/Steam/steamapps/compatdata/1592190/pfx/drive_c/users/steamuser/AppData/LocalLow/Stress Level Zero/BONELAB/Mods"),
                Path.Combine(home, ".steam/steam/steamapps/compatdata/1592190/pfx/drive_c/users/steamuser/AppData/LocalLow/Stress Level Zero/BONELAB/Mods"),
                Path.Combine(home, ".steam/root/steamapps/compatdata/1592190/pfx/drive_c/users/steamuser/AppData/LocalLow/Stress Level Zero/BONELAB/Mods"),
                Path.Combine(home, ".var/app/com.valvesoftware.Steam/data/Steam/steamapps/compatdata/1592190/pfx/drive_c/users/steamuser/AppData/LocalLow/Stress Level Zero/BONELAB/Mods"),
                Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/1592190/pfx/drive_c/users/steamuser/AppData/LocalLow/Stress Level Zero/BONELAB/Mods")
            };

            foreach (string path in possibleLinuxPaths)
            {
                if (Directory.Exists(path))
                {
                    SettingsManager.SaveConfigValue("mods_path", path);
                    return path;
                }
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localLow = Path.Combine(Directory.GetParent(appData)?.FullName ?? "", "LocalLow");
            string winPath = Path.Combine(localLow, "Stress Level Zero", "BONELAB", "Mods");

            if (Directory.Exists(winPath))
            {
                SettingsManager.SaveConfigValue("mods_path", winPath);
                return winPath;
            }
        }

        // 3. Prompt user if auto-detection failed
        AnsiConsole.MarkupLine("\n[yellow][!] Could not automatically locate your BONELAB Mods folder.[/]");
        string customInput = AnsiConsole.Prompt(
            new TextPrompt<string>("Please enter/paste your target BONELAB Mods directory path:\n> ")
                .PromptStyle("cyan"));

        string fullPath = Path.GetFullPath(customInput.Trim().Trim('"', '\''));
        Directory.CreateDirectory(fullPath);
        
        // Save for future runs
        SettingsManager.SaveConfigValue("mods_path", fullPath);

        return fullPath;
    }

    public static string GetGameFolder()
    {
        var saved = SettingsManager.LoadConfig();

        // Check if we have a saved valid game_path
        if (saved.TryGetValue("game_path", out var savedGame) && !string.IsNullOrWhiteSpace(savedGame) && Directory.Exists(savedGame))
        {
            AnsiConsole.MarkupLine($"Using saved game path: [green]{Markup.Escape(savedGame)}[/]");
            return savedGame;
        }

        string fullPath = "";
        while (true)
        {
            string gamePath = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter your BONELAB game folder path\n(Where BONELAB_Steam_Windows64.exe is located):\n> ")
                    .PromptStyle("cyan"));

            string cleaned = gamePath.Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(cleaned) && Directory.Exists(cleaned))
            {
                fullPath = Path.GetFullPath(cleaned);
                break;
            }
            AnsiConsole.MarkupLine("[red]Invalid path or directory does not exist. Please try again.[/]");
        }
        
        // Save for future runs
        SettingsManager.SaveConfigValue("game_path", fullPath);

        return fullPath;
    }
}