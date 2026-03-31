using System.Diagnostics;
using System.Text.Json;
using Metreja.IntegrationTests.Infrastructure;
using Metreja.Tool;
using Metreja.Tool.Config;

namespace Metreja.IntegrationTests.Tests;

public class MaxEventsTests : IAsyncLifetime
{
    private string _solutionRoot = "";
    private string? _skipReason;

    public Task InitializeAsync()
    {
        _solutionRoot = ProfilerPrerequisites.FindSolutionRoot();
        _skipReason = ProfilerPrerequisites.GetSkipReason(_solutionRoot);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MaxEvents_CapsPerCallEvents()
    {
        if (_skipReason is not null) return;

        const int maxEvents = 10;

        var (outputPath, tempDir) = await RunWithMaxEventsAsync(
            maxEvents,
            events: ["enter", "leave"]);

        try
        {
            var lines = await File.ReadAllLinesAsync(outputPath);
            var enterLeaveCount = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var eventType = doc.RootElement.GetProperty("event").GetString();
                if (eventType is "enter" or "leave")
                    enterLeaveCount++;
            }

            Assert.Equal(maxEvents, enterLeaveCount);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public async Task MaxEvents_GcEventsBypassCap()
    {
        if (_skipReason is not null) return;

        const int maxEvents = 4;

        var (outputPath, tempDir) = await RunWithMaxEventsAsync(
            maxEvents,
            events: ["enter", "leave", "gc_start", "gc_end"]);

        try
        {
            var lines = await File.ReadAllLinesAsync(outputPath);
            var enterLeaveCount = 0;
            var gcCount = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var eventType = doc.RootElement.GetProperty("event").GetString();
                if (eventType is "enter" or "leave")
                    enterLeaveCount++;
                else if (eventType is "gc_start" or "gc_end")
                    gcCount++;
            }

            Assert.Equal(maxEvents, enterLeaveCount);
            Assert.True(gcCount > 0,
                "Expected GC events to appear even when per-call events are capped");
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    private async Task<(string OutputPath, string TempDir)> RunWithMaxEventsAsync(
        int maxEvents, List<string> events)
    {
        var profilerDll = ProfilerPrerequisites.GetProfilerDllPath(_solutionRoot);
        var testApp = ProfilerPrerequisites.GetTestAppPath(_solutionRoot);

        var tempDir = Path.Combine(Path.GetTempPath(), "metreja-maxevents-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var outputPath = Path.Combine(tempDir, "trace.ndjson");

        var configManager = new ConfigManager(tempDir);
        var sessionId = await configManager.CreateSessionAsync("maxevents-test");
        var config = await configManager.LoadConfigAsync(sessionId);

        var instrumentation = config.Instrumentation with
        {
            MaxEvents = maxEvents,
            Events = events,
            Includes =
            [
                new FilterRule { Level = "assembly", Pattern = "Metreja.TestApp" }
            ],
            Excludes =
            [
                new FilterRule { Level = "assembly", Pattern = "System.*" },
                new FilterRule { Level = "assembly", Pattern = "Microsoft.*" }
            ]
        };

        config = config with
        {
            Instrumentation = instrumentation,
            Output = config.Output with { Path = outputPath }
        };

        await configManager.SaveConfigAsync(sessionId, config);
        var configPath = configManager.GetSessionPath(sessionId);

        var psi = new ProcessStartInfo
        {
            FileName = testApp,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.Environment["CORECLR_ENABLE_PROFILING"] = "1";
        psi.Environment["CORECLR_PROFILER"] = MetrejaConstants.ProfilerClsid;
        psi.Environment["CORECLR_PROFILER_PATH"] = profilerDll;
        psi.Environment["METREJA_CONFIG"] = Path.GetFullPath(configPath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start TestApp process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(30_000);
        await process.WaitForExitAsync(cts.Token);
        await Task.WhenAll(stdoutTask, stderrTask);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"TestApp exited with code {process.ExitCode}");

        if (!File.Exists(outputPath))
            throw new FileNotFoundException($"Expected output file not found at {outputPath}");

        return (outputPath, tempDir);
    }

    private static void CleanupTempDir(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
