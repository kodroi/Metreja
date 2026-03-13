using System.Text.Json;
using Metreja.Tool.Config;

namespace Metreja.IntegrationTests.Tests;

public class StatsFlushConfigTests : IAsyncDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "metreja-config-test-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public async Task StatsFlushIntervalSeconds_DefaultsTo30()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("test");
        var config = await manager.LoadConfigAsync(sessionId);

        Assert.Equal(30, config.Instrumentation.StatsFlushIntervalSeconds);
    }

    [Fact]
    public async Task StatsFlushIntervalSeconds_RoundTrips()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("test");
        var config = await manager.LoadConfigAsync(sessionId);

        var updated = config with
        {
            Instrumentation = config.Instrumentation with { StatsFlushIntervalSeconds = 5 }
        };
        await manager.SaveConfigAsync(sessionId, updated);

        var reloaded = await manager.LoadConfigAsync(sessionId);
        Assert.Equal(5, reloaded.Instrumentation.StatsFlushIntervalSeconds);
    }

    [Fact]
    public async Task StatsFlushIntervalSeconds_Zero_RoundTrips()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("test");
        var config = await manager.LoadConfigAsync(sessionId);

        var updated = config with
        {
            Instrumentation = config.Instrumentation with { StatsFlushIntervalSeconds = 0 }
        };
        await manager.SaveConfigAsync(sessionId, updated);

        var reloaded = await manager.LoadConfigAsync(sessionId);
        Assert.Equal(0, reloaded.Instrumentation.StatsFlushIntervalSeconds);
    }

    [Fact]
    public async Task StatsFlushIntervalSeconds_AppearsInJson()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("test");
        var config = await manager.LoadConfigAsync(sessionId);

        var updated = config with
        {
            Instrumentation = config.Instrumentation with { StatsFlushIntervalSeconds = 10 }
        };
        await manager.SaveConfigAsync(sessionId, updated);

        var json = await File.ReadAllTextAsync(manager.GetSessionPath(sessionId));
        using var doc = JsonDocument.Parse(json);
        var inst = doc.RootElement.GetProperty("instrumentation");
        Assert.True(inst.TryGetProperty("statsFlushIntervalSeconds", out var value));
        Assert.Equal(10, value.GetInt32());
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best effort cleanup
        }

        return ValueTask.CompletedTask;
    }
}
