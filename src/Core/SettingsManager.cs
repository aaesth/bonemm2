using System.Text.Json;

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
            Console.Write("Enter your mod.io API key (from https://mod.io/me/access): ");
            key = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(key)) Environment.Exit(1);
            needSave = true;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Console.WriteLine("mod.io retired the old api.mod.io domain — every key now has its own API path.");
            Console.Write("Enter it from https://mod.io/me/access (looks like https://u-XXXXXX.modapi.io/v1): ");
            baseUrl = Console.ReadLine()?.Trim();
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
        Console.WriteLine("=== Settings ===");
        
        var saved = LoadConfig();

        string currentKey = saved.GetValueOrDefault("api_key", "Not set");
        string currentBase = saved.GetValueOrDefault("api_base", "Not set");
        string currentGame = saved.GetValueOrDefault("game_path", "Not set");
        string currentMods = saved.GetValueOrDefault("mods_path", "Not set");

        Console.WriteLine($"1. Current API Key:   {currentKey}");
        Console.WriteLine($"2. Current API Base:  {currentBase}");
        Console.WriteLine($"3. Saved Game Path:   {currentGame}");
        Console.WriteLine($"4. Saved Mods Path:  {currentMods}");
        Console.WriteLine("\nLeave blank and press Enter to keep current value.");

        Console.Write("\nEnter new mod.io API Key: ");
        string? newKey = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(newKey)) saved["api_key"] = newKey;

        Console.Write("Enter new API Base URL: ");
        string? newBase = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(newBase)) saved["api_base"] = newBase.TrimEnd('/');

        Console.Write("Enter BONELAB Game Folder Path: ");
        string? newGame = Console.ReadLine()?.Trim().Trim('"', '\'');
        if (!string.IsNullOrWhiteSpace(newGame)) saved["game_path"] = newGame;

        Console.Write("Enter BONELAB Mods Folder Path: ");
        string? newMods = Console.ReadLine()?.Trim().Trim('"', '\'');
        if (!string.IsNullOrWhiteSpace(newMods)) saved["mods_path"] = newMods;

        File.WriteAllText(GetConfigPath(), JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nSettings saved to: {GetConfigPath()}");
    }
}