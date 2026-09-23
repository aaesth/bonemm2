using System.Text.RegularExpressions;

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
}