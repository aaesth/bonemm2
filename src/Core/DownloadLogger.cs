using System.Text.Json;
using Spectre.Console;

namespace Bonemm2;

public static class DownloadLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Write(string outputDir, DownloadLog log)
    {
        try
        {
            Directory.CreateDirectory(outputDir);
            string path = Path.Combine(outputDir, "download_log.json");
            File.WriteAllText(path, JsonSerializer.Serialize(log, JsonOptions));
            AnsiConsole.MarkupLine($"[grey]Log saved to: {Markup.Escape(path)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow][!][/] Failed to save download log: {Markup.Escape(ex.Message)}");
        }
    }
}
