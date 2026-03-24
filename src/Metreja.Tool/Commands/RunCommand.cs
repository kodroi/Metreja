using System.CommandLine;
using System.Diagnostics;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

public static class RunCommand
{
    public static Command Create()
    {
        var sessionOption = SharedOptions.SessionOption();

        var detachOption = new Option<bool>("--detach")
        {
            Description = "Launch and return immediately (for GUI/long-running apps)"
        };

        var exeArgument = new Argument<string>("exe-path")
        {
            Description = "Path to the executable to profile"
        };

        var extraArgsArgument = new Argument<string[]>("extra-args")
        {
            Description = "Additional arguments passed to the executable",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("run", "Launch an executable with profiler environment variables attached");
        command.Options.Add(sessionOption);
        command.Options.Add(detachOption);
        command.Arguments.Add(exeArgument);
        command.Arguments.Add(extraArgsArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var detach = parseResult.GetValue(detachOption);
            var exePath = parseResult.GetValue(exeArgument)!;
            var extraArgs = parseResult.GetValue(extraArgsArgument) ?? [];

            // 1. Resolve profiler DLL
            var absoluteDllPath = ProfilerLocator.ResolveProfilerPath();
            if (absoluteDllPath is null)
                return 1;

            DebugLog.Write("run", $"profiler: {absoluteDllPath}");

            // 2. Resolve session config and make output path absolute
            var manager = ConfigManager.Default;
            var configPath = manager.GetSessionPath(session);
            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"Error: Session '{session}' not found at {configPath}");
                return 1;
            }

            var config = await manager.LoadConfigAsync(session);
            if (!Path.IsPathFullyQualified(config.Output.Path))
            {
                var absoluteOutputPath = Path.GetFullPath(config.Output.Path);
                config = config with { Output = config.Output with { Path = absoluteOutputPath } };
                await manager.SaveConfigAsync(session, config);
            }

            var absoluteConfigPath = Path.GetFullPath(configPath);

            DebugLog.Write("run", $"config: {absoluteConfigPath}");
            DebugLog.Write("run", $"output: {config.Output.Path}");
            DebugLog.Write("run", $"events: [{string.Join(", ", config.Instrumentation.Events ?? [])}]");
            DebugLog.Write("run", $"includes: {config.Instrumentation.Includes.Count} rules, excludes: {config.Instrumentation.Excludes.Count} rules");

            // 3. Resolve exe path
            var absoluteExePath = Path.GetFullPath(exePath);
            if (!File.Exists(absoluteExePath))
            {
                Console.Error.WriteLine($"Error: Executable not found at {absoluteExePath}");
                return 1;
            }

            // 4. Create ProcessStartInfo with profiler env vars
            var psi = new ProcessStartInfo
            {
                FileName = absoluteExePath,
                Arguments = string.Join(' ', extraArgs),
                UseShellExecute = false
            };
            psi.Environment["CORECLR_ENABLE_PROFILING"] = "1";
            psi.Environment["CORECLR_PROFILER"] = MetrejaConstants.ProfilerClsid;
            psi.Environment["CORECLR_PROFILER_PATH"] = absoluteDllPath;
            psi.Environment["METREJA_CONFIG"] = absoluteConfigPath;

            // 5. Launch
            DebugLog.Write("run", $"launching: {absoluteExePath} {string.Join(' ', extraArgs)}");
            var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine("Error: Failed to start process.");
                return 1;
            }

            if (detach)
            {
                Console.WriteLine($"Launched with PID {process.Id}");
                return 0;
            }

            DebugLog.Write("run", $"PID {process.Id} started, waiting for exit...");
            await process.WaitForExitAsync(cancellationToken);
            DebugLog.Write("run", $"PID {process.Id} exited with code {process.ExitCode}");
            return process.ExitCode;
        });

        return command;
    }
}
