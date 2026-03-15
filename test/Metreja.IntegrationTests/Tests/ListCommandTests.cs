using Metreja.Tool.Config;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class ListCommandTests : IAsyncLifetime
{
    private string _tempDir = "";

    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"metreja-list-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task List_ShowsSessions()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("load-test");

        var sessions = manager.ListSessions().ToList();

        Assert.Single(sessions);
        Assert.Equal(sessionId, sessions[0]);
    }

    [Fact]
    public async Task List_ShowsScenario()
    {
        var manager = new ConfigManager(_tempDir);
        await manager.CreateSessionAsync("perf-baseline");

        var sessions = manager.ListSessions().ToList();
        var config = await manager.LoadConfigAsync(sessions[0]);

        Assert.Equal("perf-baseline", config.Metadata.Scenario);
    }

    [Fact]
    public void List_EmptyDirectory()
    {
        var manager = new ConfigManager(_tempDir);
        var sessions = manager.ListSessions().ToList();

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task List_HandlesCorruptConfig()
    {
        var sessionsDir = Path.Combine(_tempDir, ".metreja", "sessions");
        Directory.CreateDirectory(sessionsDir);
        await File.WriteAllTextAsync(Path.Combine(sessionsDir, "corrupt.json"), "not valid json");

        var manager = new ConfigManager(_tempDir);
        var sessions = manager.ListSessions().ToList();

        Assert.Single(sessions);
        Assert.Equal("corrupt", sessions[0]);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => manager.LoadConfigAsync("corrupt"));
    }
}
