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

    public static (string apiKey, string apiBase) GetCredentials(string? cliKey = null, string? cliBase = null)
    {
        string configPath = GetConfigPath();
        Dictionary<string, string>? saved = null;
        if (File.Exists(configPath))
        {
            try { saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configPath)); } catch { }
        }

        string? key = cliKey ?? Environment.GetEnvironmentVariable("MODIO_API_KEY");
        if (string.IsNullOrWhiteSpace(key) && saved is not null && saved.TryGetValue("api_key", out var savedKey)) key = savedKey;

        string? baseUrl = cliBase ?? Environment.GetEnvironmentVariable("MODIO_API_BASE");
        if (string.IsNullOrWhiteSpace(baseUrl) && saved is not null && saved.TryGetValue("api_base", out var savedBase)) baseUrl = savedBase;

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
            File.WriteAllText(configPath, JsonSerializer.Serialize(
                new Dictionary<string, string> { ["api_key"] = key!, ["api_base"] = baseUrl }));
        }

        return (key!, baseUrl);
    }

    public static void RunSettingsMenu()
    {
        Console.WriteLine("=== Settings ===");
        
        string configPath = GetConfigPath();
        Dictionary<string, string> saved = new();
        
        if (File.Exists(configPath))
        {
            try { saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configPath)) ?? new(); }
            catch { }
        }

        string currentKey = saved.ContainsKey("api_key") ? saved["api_key"] : "Not set";
        string currentBase = saved.ContainsKey("api_base") ? saved["api_base"] : "Not set";

        Console.WriteLine($"\n1. Current API Key: {currentKey}");
        Console.WriteLine($"2. Current API Base: {currentBase}");
        Console.WriteLine("\nLeave blank and press Enter to keep current value.");

        Console.Write("\nEnter new mod.io API Key: ");
        string? newKey = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(newKey)) saved["api_key"] = newKey;

        Console.Write("Enter new API Base URL (e.g. https://u-XXXXXX.modapi.io/v1): ");
        string? newBase = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(newBase)) saved["api_base"] = newBase.TrimEnd('/');

        File.WriteAllText(configPath, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nSettings saved to: {configPath}");
    }
}