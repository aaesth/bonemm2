using System.Text.Json;
using Spectre.Console;

namespace Bonemm2;

public static class SettingsManager
{
    private static string GetConfigPath()
    {
        string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bonemm2");
        Directory.CreateDirectory(configDir);
        return Path.Combine(configDir, "api_key.json");
    }

    public static Dictionary<string, string> LoadConfig()
    {
        string configPath = GetConfigPath();
        if (File.Exists(configPath))
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configPath)) ?? new();
            }
            catch { }
        }
        return new();
    }

    public static void SaveConfigValue(string key, string value)
    {
        var config = LoadConfig();
        config[key] = value;
        File.WriteAllText(GetConfigPath(), JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static (string apiKey, string apiBase) GetCredentials(string? cliKey = null, string? cliBase = null)
    {
        var saved = LoadConfig();

        string? key = cliKey ?? Environment.GetEnvironmentVariable("MODIO_API_KEY");
        if (string.IsNullOrWhiteSpace(key) && saved.TryGetValue("api_key", out var savedKey)) key = savedKey;

        string? baseUrl = cliBase ?? Environment.GetEnvironmentVariable("MODIO_API_BASE");
        if (string.IsNullOrWhiteSpace(baseUrl) && saved.TryGetValue("api_base", out var savedBase)) baseUrl = savedBase;

        bool needSave = false;

        if (string.IsNullOrWhiteSpace(key))
        {
            key = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter your mod.io API key (from [link=https://mod.io/me/access]https://mod.io/me/access[/]): ")
                    .PromptStyle("cyan"));
            if (string.IsNullOrWhiteSpace(key)) Environment.Exit(1);
            needSave = true;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            AnsiConsole.MarkupLine("[yellow]mod.io retired the old api.mod.io domain — every key now has its own API path.[/]");
            baseUrl = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter it from [link=https://mod.io/me/access]https://mod.io/me/access[/] (e.g. [dim]https://u-XXXXXX.modapi.io/v1[/]): ")
                    .PromptStyle("cyan"));
            if (string.IsNullOrWhiteSpace(baseUrl)) Environment.Exit(1);
            needSave = true;
        }

        baseUrl = baseUrl!.TrimEnd('/');

        if (needSave)
        {
            SaveConfigValue("api_key", key!);
            SaveConfigValue("api_base", baseUrl);
        }

        return (key!, baseUrl);
    }

    public static void RunSettingsMenu()
    {
        AnsiConsole.MarkupLine("[bold cyan]=== Settings ===[/]\n");
        
        var saved = LoadConfig();

        string currentKey = saved.GetValueOrDefault("api_key", "[dim]Not set[/]");
        string currentBase = saved.GetValueOrDefault("api_base", "[dim]Not set[/]");
        string currentGame = saved.GetValueOrDefault("game_path", "[dim]Not set[/]");
        string currentMods = saved.GetValueOrDefault("mods_path", "[dim]Not set[/]");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Setting[/]");
        table.AddColumn("[bold]Current Value[/]");
        table.AddRow("1. API Key", Markup.Escape(currentKey));
        table.AddRow("2. API Base URL", Markup.Escape(currentBase));
        table.AddRow("3. Game Path", Markup.Escape(currentGame));
        table.AddRow("4. Mods Path", Markup.Escape(currentMods));
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("\n[grey]Press Enter without typing to keep current value.[/]\n");

        string newKey = AnsiConsole.Prompt(
            new TextPrompt<string>("New mod.io API Key: ")
                .AllowEmpty());
        if (!string.IsNullOrWhiteSpace(newKey)) saved["api_key"] = newKey.Trim();

        string newBase = AnsiConsole.Prompt(
            new TextPrompt<string>("New API Base URL: ")
                .AllowEmpty());
        if (!string.IsNullOrWhiteSpace(newBase)) saved["api_base"] = newBase.Trim().TrimEnd('/');

        string newGame = AnsiConsole.Prompt(
            new TextPrompt<string>("New BONELAB Game Folder: ")
                .AllowEmpty());
        if (!string.IsNullOrWhiteSpace(newGame)) saved["game_path"] = newGame.Trim().Trim('"', '\'');

        string newMods = AnsiConsole.Prompt(
            new TextPrompt<string>("New BONELAB Mods Folder: ")
                .AllowEmpty());
        if (!string.IsNullOrWhiteSpace(newMods)) saved["mods_path"] = newMods.Trim().Trim('"', '\'');

        File.WriteAllText(GetConfigPath(), JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        AnsiConsole.MarkupLine($"\n[green][[SUCCESS]][/] Settings saved to: [cyan]{Markup.Escape(GetConfigPath())}[/]");
    }
}