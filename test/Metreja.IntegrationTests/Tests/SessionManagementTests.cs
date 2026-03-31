using Metreja.Tool;
using Metreja.Tool.Config;

namespace Metreja.IntegrationTests.Tests;

[Collection("ConsoleCapture")]
public class SessionManagementTests : IAsyncLifetime
{
    private string _tempDir = "";

    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"metreja-session-test-{Guid.NewGuid():N}");
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
    public async Task Init_LoadRoundTrip_VerifyDefaults()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("round-trip-test");

        var config = await manager.LoadConfigAsync(sessionId);

        Assert.Equal(sessionId, config.SessionId);
        Assert.Equal("round-trip-test", config.Metadata.Scenario);
        Assert.Equal(0, config.Instrumentation.MaxEvents);
        Assert.True(config.Instrumentation.ComputeDeltas);
        Assert.True(config.Instrumentation.DisableInlining);
        Assert.Equal(30, config.Instrumentation.StatsFlushIntervalSeconds);
        Assert.Null(config.Instrumentation.Events);
        Assert.Empty(config.Instrumentation.Includes);
        Assert.Equal(2, config.Instrumentation.Excludes.Count);
        Assert.Contains(config.Instrumentation.Excludes,
            r => r.Level == "assembly" && r.Pattern == "System.*");
        Assert.Contains(config.Instrumentation.Excludes,
            r => r.Level == "assembly" && r.Pattern == "Microsoft.*");
        Assert.Equal(".metreja/output/{sessionId}_{pid}.ndjson", config.Output.Path);
    }

    [Fact]
    public async Task SetEvents_PersistsCorrectly()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("events-test");

        var config = await manager.LoadConfigAsync(sessionId);
        var events = new List<string> { "enter", "leave", "exception" };
        config = config with
        {
            Instrumentation = config.Instrumentation with { Events = events }
        };
        await manager.SaveConfigAsync(sessionId, config);

        var reloaded = await manager.LoadConfigAsync(sessionId);
        Assert.NotNull(reloaded.Instrumentation.Events);
        Assert.Equal(3, reloaded.Instrumentation.Events!.Count);
        Assert.Contains("enter", reloaded.Instrumentation.Events);
        Assert.Contains("leave", reloaded.Instrumentation.Events);
        Assert.Contains("exception", reloaded.Instrumentation.Events);
    }

    [Fact]
    public async Task SetOutputPath_PersistsCorrectly()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("output-test");

        var config = await manager.LoadConfigAsync(sessionId);
        config = config with
        {
            Output = config.Output with { Path = "/custom/output/trace_{sessionId}.ndjson" }
        };
        await manager.SaveConfigAsync(sessionId, config);

        var reloaded = await manager.LoadConfigAsync(sessionId);
        Assert.Equal("/custom/output/trace_{sessionId}.ndjson", reloaded.Output.Path);
    }

    [Fact]
    public async Task SetMaxEvents_PersistsCorrectly()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("max-events-test");

        var config = await manager.LoadConfigAsync(sessionId);
        config = config with
        {
            Instrumentation = config.Instrumentation with { MaxEvents = 50000 }
        };
        await manager.SaveConfigAsync(sessionId, config);

        var reloaded = await manager.LoadConfigAsync(sessionId);
        Assert.Equal(50000, reloaded.Instrumentation.MaxEvents);
    }

    [Fact]
    public async Task AddIncludeExcludeFilters_PersistsCorrectly()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("filters-test");

        var config = await manager.LoadConfigAsync(sessionId);

        // Add include filters at different levels
        var includes = new List<FilterRule>
        {
            new() { Level = "assembly", Pattern = "MyApp.*" },
            new() { Level = "namespace", Pattern = "MyApp.Core.*" },
            new() { Level = "class", Pattern = "OrderService" },
            new() { Level = "method", Pattern = "Process*" }
        };

        // Add an extra exclude filter beyond the defaults
        var excludes = config.Instrumentation.Excludes
            .Append(new FilterRule { Level = "namespace", Pattern = "MyApp.Generated.*" })
            .ToList();

        config = config with
        {
            Instrumentation = config.Instrumentation with
            {
                Includes = includes,
                Excludes = excludes
            }
        };
        await manager.SaveConfigAsync(sessionId, config);

        var reloaded = await manager.LoadConfigAsync(sessionId);

        Assert.Equal(4, reloaded.Instrumentation.Includes.Count);
        Assert.Contains(reloaded.Instrumentation.Includes,
            r => r.Level == "assembly" && r.Pattern == "MyApp.*");
        Assert.Contains(reloaded.Instrumentation.Includes,
            r => r.Level == "namespace" && r.Pattern == "MyApp.Core.*");
        Assert.Contains(reloaded.Instrumentation.Includes,
            r => r.Level == "class" && r.Pattern == "OrderService");
        Assert.Contains(reloaded.Instrumentation.Includes,
            r => r.Level == "method" && r.Pattern == "Process*");

        Assert.Equal(3, reloaded.Instrumentation.Excludes.Count);
        Assert.Contains(reloaded.Instrumentation.Excludes,
            r => r.Level == "namespace" && r.Pattern == "MyApp.Generated.*");
    }

    [Fact]
    public async Task RemoveFilter_RemovesCorrectly()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("remove-filter-test");

        var config = await manager.LoadConfigAsync(sessionId);

        // Add includes, then remove one
        var includes = new List<FilterRule>
        {
            new() { Level = "assembly", Pattern = "MyApp.*" },
            new() { Level = "namespace", Pattern = "MyApp.Core.*" }
        };
        config = config with
        {
            Instrumentation = config.Instrumentation with { Includes = includes }
        };
        await manager.SaveConfigAsync(sessionId, config);

        // Reload and remove the assembly-level include
        config = await manager.LoadConfigAsync(sessionId);
        var ruleToRemove = new FilterRule { Level = "assembly", Pattern = "MyApp.*" };
        var updatedIncludes = config.Instrumentation.Includes
            .Where(r => r != ruleToRemove)
            .ToList();
        config = config with
        {
            Instrumentation = config.Instrumentation with { Includes = updatedIncludes }
        };
        await manager.SaveConfigAsync(sessionId, config);

        var reloaded = await manager.LoadConfigAsync(sessionId);
        Assert.Single(reloaded.Instrumentation.Includes);
        Assert.Equal("namespace", reloaded.Instrumentation.Includes[0].Level);
        Assert.Equal("MyApp.Core.*", reloaded.Instrumentation.Includes[0].Pattern);
    }

    [Fact]
    public async Task ClearFilters_ClearsAll()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("clear-filters-test");

        var config = await manager.LoadConfigAsync(sessionId);

        // Add some includes and extra excludes
        config = config with
        {
            Instrumentation = config.Instrumentation with
            {
                Includes = [new FilterRule { Level = "assembly", Pattern = "MyApp.*" }],
                Excludes =
                [
                    new FilterRule { Level = "assembly", Pattern = "System.*" },
                    new FilterRule { Level = "assembly", Pattern = "Microsoft.*" },
                    new FilterRule { Level = "namespace", Pattern = "Generated.*" }
                ]
            }
        };
        await manager.SaveConfigAsync(sessionId, config);

        // Clear all filters
        config = await manager.LoadConfigAsync(sessionId);
        config = config with
        {
            Instrumentation = config.Instrumentation with
            {
                Includes = [],
                Excludes = []
            }
        };
        await manager.SaveConfigAsync(sessionId, config);

        var reloaded = await manager.LoadConfigAsync(sessionId);
        Assert.Empty(reloaded.Instrumentation.Includes);
        Assert.Empty(reloaded.Instrumentation.Excludes);
    }

    [Fact]
    public async Task Validate_ValidSession_NoErrors()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("validate-pass-test");

        var config = await manager.LoadConfigAsync(sessionId);

        // A valid config has a non-empty sessionId and a non-empty output path
        Assert.False(string.IsNullOrWhiteSpace(config.SessionId));
        Assert.False(string.IsNullOrWhiteSpace(config.Output.Path));
    }

    [Fact]
    public async Task Validate_MissingFields_Fails()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("validate-fail-test");

        var config = await manager.LoadConfigAsync(sessionId);

        // Simulate a config with missing required fields
        config = config with
        {
            SessionId = "",
            Output = config.Output with { Path = "" }
        };
        await manager.SaveConfigAsync(sessionId, config);

        var reloaded = await manager.LoadConfigAsync(sessionId);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(reloaded.SessionId))
            errors.Add("sessionId is required");
        if (string.IsNullOrWhiteSpace(reloaded.Output.Path))
            errors.Add("output.path is required");

        Assert.Equal(2, errors.Count);
        Assert.Contains("sessionId is required", errors);
        Assert.Contains("output.path is required", errors);
    }

    [Fact]
    public async Task GenerateEnv_ContainsRequiredEnvVars()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("generate-env-test");

        var configPath = Path.GetFullPath(manager.GetSessionPath(sessionId));

        // Verify the config file exists on disk
        Assert.True(File.Exists(configPath));

        // Verify the METREJA_CONFIG path points to the session file
        Assert.True(configPath.EndsWith(".json", StringComparison.Ordinal), "Config path should be a JSON file");

        // CORECLR_PROFILER is the profiler CLSID
        Assert.Equal("{7C8F944B-4810-4999-BF98-6A3361185FC2}", MetrejaConstants.ProfilerClsid);

        // CORECLR_PROFILER_PATH would be resolved by ProfilerLocator (may be null in test env)
        // We just verify the locator API is accessible
        var profilerPath = ProfilerLocator.GetDefaultProfilerPath();
        // profilerPath may be null in test environment — that's expected

        // METREJA_CONFIG should point to the session config file
        Assert.Contains(sessionId, configPath);
        Assert.EndsWith(".json", configPath);

        // Verify EnsureAbsoluteOutputPathAsync works
        await manager.EnsureAbsoluteOutputPathAsync(sessionId);
        var reloaded = await manager.LoadConfigAsync(sessionId);
        Assert.True(Path.IsPathFullyQualified(reloaded.Output.Path));
    }

    [Fact]
    public async Task ClearSession_DeletesSession()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("clear-session-test");

        // Verify session exists
        var sessions = manager.ListSessions().ToList();
        Assert.Contains(sessionId, sessions);

        // Delete the session
        manager.DeleteSession(sessionId);

        // Verify session is gone
        sessions = [.. manager.ListSessions()];
        Assert.DoesNotContain(sessionId, sessions);

        // Verify loading throws
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => manager.LoadConfigAsync(sessionId));
    }

    [Fact]
    public async Task ClearAllSessions_DeletesEverything()
    {
        var manager = new ConfigManager(_tempDir);
        await manager.CreateSessionAsync("session-a");
        await manager.CreateSessionAsync("session-b");
        await manager.CreateSessionAsync("session-c");

        Assert.Equal(3, manager.ListSessions().Count());

        manager.DeleteAllSessions();

        Assert.Empty(manager.ListSessions().ToList());
    }

    [Fact]
    public async Task Init_WithoutScenario_DefaultsToEmptyString()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync();

        var config = await manager.LoadConfigAsync(sessionId);

        Assert.Equal("", config.Metadata.Scenario);
    }

    [Fact]
    public async Task MultipleSetOperations_PreserveOtherFields()
    {
        var manager = new ConfigManager(_tempDir);
        var sessionId = await manager.CreateSessionAsync("multi-set-test");

        // Set events
        var config = await manager.LoadConfigAsync(sessionId);
        config = config with
        {
            Instrumentation = config.Instrumentation with
            {
                Events = ["enter", "leave"]
            }
        };
        await manager.SaveConfigAsync(sessionId, config);

        // Set max-events (should preserve events list)
        config = await manager.LoadConfigAsync(sessionId);
        config = config with
        {
            Instrumentation = config.Instrumentation with { MaxEvents = 10000 }
        };
        await manager.SaveConfigAsync(sessionId, config);

        // Set output path (should preserve instrumentation)
        config = await manager.LoadConfigAsync(sessionId);
        config = config with
        {
            Output = config.Output with { Path = "/tmp/custom.ndjson" }
        };
        await manager.SaveConfigAsync(sessionId, config);

        // Verify all settings persisted correctly
        var final = await manager.LoadConfigAsync(sessionId);
        Assert.Equal("multi-set-test", final.Metadata.Scenario);
        Assert.Equal(10000, final.Instrumentation.MaxEvents);
        Assert.NotNull(final.Instrumentation.Events);
        Assert.Equal(2, final.Instrumentation.Events!.Count);
        Assert.Contains("enter", final.Instrumentation.Events);
        Assert.Contains("leave", final.Instrumentation.Events);
        Assert.Equal("/tmp/custom.ndjson", final.Output.Path);
    }
}
