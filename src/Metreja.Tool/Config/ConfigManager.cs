using System.Security.Cryptography;
using System.Text.Json;

namespace Metreja.Tool.Config;

public class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _sessionsDir;

    public ConfigManager(string? baseDir = null)
    {
        var root = baseDir ?? Directory.GetCurrentDirectory();
        _sessionsDir = Path.Combine(root, ".metreja", "sessions");
    }

    public async Task<string> CreateSessionAsync(string? scenario = null)
    {
        Directory.CreateDirectory(_sessionsDir);

        var sessionId = GenerateSessionId();
        var config = new ProfilerConfig
        {
            SessionId = sessionId,
            Metadata = new MetadataConfig
            {
                Scenario = scenario ?? ""
            },
            Instrumentation = new InstrumentationConfig
            {
                Excludes = DefaultFilters.Excludes
            }
        };

        await SaveConfigAsync(sessionId, config);
        return sessionId;
    }

    public async Task<ProfilerConfig> LoadConfigAsync(string sessionId)
    {
        var path = GetSessionPath(sessionId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Session '{sessionId}' not found at {path}");

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<ProfilerConfig>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize config");
    }

    public async Task SaveConfigAsync(string sessionId, ProfilerConfig config)
    {
        Directory.CreateDirectory(_sessionsDir);
        var path = GetSessionPath(sessionId);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public string GetSessionPath(string sessionId)
    {
        return Path.Combine(_sessionsDir, $"{sessionId}.json");
    }

    public IEnumerable<string> ListSessions()
    {
        if (!Directory.Exists(_sessionsDir))
            yield break;

        foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
        {
            yield return Path.GetFileNameWithoutExtension(file);
        }
    }

    public void DeleteSession(string sessionId)
    {
        var path = GetSessionPath(sessionId);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void DeleteAllSessions()
    {
        if (Directory.Exists(_sessionsDir))
            Directory.Delete(_sessionsDir, true);
    }

    private static string GenerateSessionId()
    {
        var bytes = RandomNumberGenerator.GetBytes(3);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
