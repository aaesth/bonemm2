using System.Text.Json.Serialization;

namespace Bonemm2;

public enum TargetType { Collection, Mod, Unknown }

public class SlotState
{
    public bool IsActive = false;
    public string Name = "";
    public long BytesDownloaded = 0;
    public long TotalBytes = 0;
    public string? CustomStatus = null;

    public void Reset(string name, long totalBytes)
    {
        Name = name;
        TotalBytes = totalBytes;
        BytesDownloaded = 0;
        CustomStatus = null;
        IsActive = true;
    }
}

public class PagedResponse<T>
{
    [JsonPropertyName("data")] public List<T> Data { get; set; } = new();
    [JsonPropertyName("result_total")] public int ResultTotal { get; set; }
}

public class GameObject
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public class CollectionObject
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public class ModObject
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("modfile")] public ModfileObject? Modfile { get; set; }
}

public class ModfileObject
{
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("filesize")] public long? Filesize { get; set; }
    [JsonPropertyName("download")] public DownloadObject? Download { get; set; }
    [JsonPropertyName("platforms")] public List<ModfilePlatformObject>? Platforms { get; set; }
}

public class ModfilePlatformObject
{
    [JsonPropertyName("platform")] public string Platform { get; set; } = "";
    [JsonPropertyName("status")] public int Status { get; set; }
}

public class DownloadObject
{
    [JsonPropertyName("binary_url")] public string BinaryUrl { get; set; } = "";
}

public class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
}

public class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
}