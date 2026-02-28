using System.Text.Json;

namespace Metreja.Tool;

internal static class UpdateChecker
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".metreja");

    private static readonly string CacheFile = Path.Combine(CacheDir, "update-check.json");

    private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(24);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(3);

    private const string NuGetIndexUrl =
        "https://api.nuget.org/v3-flatcontainer/metreja.tool/index.json";

    public static async Task CheckForUpdateAsync()
    {
        try
        {
            var latestVersion = await GetLatestVersionAsync().ConfigureAwait(false);
            if (latestVersion is null)
                return;

            if (!Version.TryParse(GitVersionInformation.MajorMinorPatch, out var current))
                return;

            if (!Version.TryParse(latestVersion, out var latest))
                return;

            if (latest > current)
            {
                await Console.Error.WriteLineAsync(
                    $"A new version of Metreja is available: v{latestVersion} (current: v{GitVersionInformation.MajorMinorPatch})").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(
                    "Run 'dotnet tool update -g Metreja.Tool' to update.").ConfigureAwait(false);
            }
        }
        catch
        {
            // Never block or affect the user's command
        }
    }

    private static async Task<string?> GetLatestVersionAsync()
    {
        var cached = ReadCache();
        if (cached is not null)
            return cached;

        using var httpClient = new HttpClient { Timeout = HttpTimeout };
        var json = await httpClient.GetStringAsync(NuGetIndexUrl).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement.GetProperty("versions");
        var latestVersion = versions[versions.GetArrayLength() - 1].GetString();

        if (latestVersion is not null)
            WriteCache(latestVersion);

        return latestVersion;
    }

    private static string? ReadCache()
    {
        if (!File.Exists(CacheFile))
            return null;

        var json = File.ReadAllText(CacheFile);
        using var doc = JsonDocument.Parse(json);

        var lastCheck = doc.RootElement.GetProperty("lastCheck").GetDateTimeOffset();
        if (DateTimeOffset.UtcNow - lastCheck < CacheExpiry)
            return doc.RootElement.GetProperty("latestVersion").GetString();

        return null;
    }

    private static void WriteCache(string latestVersion)
    {
        Directory.CreateDirectory(CacheDir);

        var cache = new
        {
            lastCheck = DateTimeOffset.UtcNow.ToString("o"),
            latestVersion
        };

        File.WriteAllText(CacheFile, JsonSerializer.Serialize(cache));
    }
}
