using System.Diagnostics;
using Metreja.Tool;
using Metreja.Tool.Config;

namespace Metreja.IntegrationTests.Infrastructure;

public sealed class ProfilerRunner : IAsyncDisposable
{
    private const string OutputFileName = "trace.ndjson";

    private readonly string _tempDir;

    private ProfilerRunner(string tempDir)
    {
        _tempDir = tempDir;
    }

    public static async Task<(string OutputPath, ProfilerRunner Runner)> RunAsync(
        string solutionRoot,
        string? scenario = null,
        List<string>? events = null,
        int? statsFlushIntervalSeconds = null,
        int timeoutMs = 30_000)
    {
        var profilerDll = ProfilerPrerequisites.GetProfilerDllPath(solutionRoot);
        var testApp = ProfilerPrerequisites.GetTestAppPath(solutionRoot);

        var tempDir = Path.Combine(Path.GetTempPath(), "metreja-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var runner = new ProfilerRunner(tempDir);

        try
        {
            var outputPath = Path.Combine(tempDir, OutputFileName);

            // Create session config using ConfigManager
            var configManager = new ConfigManager(tempDir);
            var sessionId = await configManager.CreateSessionAsync(scenario ?? "integration-test");
            var config = await configManager.LoadConfigAsync(sessionId);

            // Update config with our test settings
            var instrumentation = config.Instrumentation with
            {
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
            instrumentation = instrumentation with { Events = events ?? ["enter", "leave", "exception"] };
            if (statsFlushIntervalSeconds.HasValue)
                instrumentation = instrumentation with { StatsFlushIntervalSeconds = statsFlushIntervalSeconds.Value };

            config = config with
            {
                Instrumentation = instrumentation,
                Output = config.Output with
                {
                    Path = outputPath
                }
            };

            await configManager.SaveConfigAsync(sessionId, config);
            var configPath = configManager.GetSessionPath(sessionId);

            // Launch TestApp with profiler environment
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

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(timeoutMs);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"TestApp exited with code {process.ExitCode}.\nStdout: {stdout}\nStderr: {stderr}");
            }

            if (!File.Exists(outputPath))
            {
                throw new FileNotFoundException(
                    $"Expected output file not found at {outputPath}.\nStdout: {stdout}\nStderr: {stderr}");
            }

            return (outputPath, runner);
        }
        catch
        {
            await runner.DisposeAsync();
            throw;
        }
    }

    public static async Task<InteractiveSession> RunInteractiveAsync(
        string solutionRoot,
        List<string>? events = null,
        int? statsFlushIntervalSeconds = null,
        int readyTimeoutMs = 30_000)
    {
        var profilerDll = ProfilerPrerequisites.GetProfilerDllPath(solutionRoot);
        var testApp = ProfilerPrerequisites.GetTestAppPath(solutionRoot);

        var tempDir = Path.Combine(Path.GetTempPath(), "metreja-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var runner = new ProfilerRunner(tempDir);

        try
        {
            var outputPath = Path.Combine(tempDir, OutputFileName);

            var configManager = new ConfigManager(tempDir);
            var sessionId = await configManager.CreateSessionAsync("interactive-test");
            var config = await configManager.LoadConfigAsync(sessionId);

            var instrumentation = config.Instrumentation with
            {
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
            instrumentation = instrumentation with { Events = events ?? ["method_stats"] };
            if (statsFlushIntervalSeconds.HasValue)
                instrumentation = instrumentation with { StatsFlushIntervalSeconds = statsFlushIntervalSeconds.Value };

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
                Arguments = "--wait",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            psi.Environment["CORECLR_ENABLE_PROFILING"] = "1";
            psi.Environment["CORECLR_PROFILER"] = MetrejaConstants.ProfilerClsid;
            psi.Environment["CORECLR_PROFILER_PATH"] = profilerDll;
            psi.Environment["METREJA_CONFIG"] = Path.GetFullPath(configPath);

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start TestApp process");

            try
            {
                // Wait for "READY" on stdout (all test methods have completed)
                using var cts = new CancellationTokenSource(readyTimeoutMs);
                while (!cts.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cts.Token)
                        ?? throw new InvalidOperationException("TestApp process exited before signaling READY");
                    if (line == "READY")
                        break;
                }

                return new InteractiveSession(outputPath, process, runner);
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(true);
                }
                catch
                {
                    // Best effort
                }
                process.Dispose();
                throw;
            }
        }
        catch
        {
            await runner.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
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

public sealed class InteractiveSession : IAsyncDisposable
{
    public string OutputPath { get; }
    public int Pid => _process.Id;

    private readonly Process _process;
    private readonly ProfilerRunner _runner;

    internal InteractiveSession(string outputPath, Process process, ProfilerRunner runner)
    {
        OutputPath = outputPath;
        _process = process;
        _runner = runner;
    }

    public async Task ReleaseAndWaitForExitAsync(int timeoutMs = 10_000)
    {
        if (!_process.HasExited)
            await _process.StandardInput.WriteLineAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        await _process.WaitForExitAsync(cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync();
                _process.WaitForExit(5_000);
                if (!_process.HasExited)
                    _process.Kill();
            }
        }
        catch
        {
            // Best effort
        }
        finally
        {
            _process.Dispose();
        }

        await _runner.DisposeAsync();
    }
}
