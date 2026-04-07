using System.CommandLine;
using System.Diagnostics;
using Metreja.Tool.Analysis;
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

            await manager.EnsureAbsoluteOutputPathAsync(session);
            var config = await manager.LoadConfigAsync(session);

            var absoluteConfigPath = Path.GetFullPath(configPath);

            DebugLog.Write("run", $"config: {absoluteConfigPath}");
            DebugLog.Write("run", $"output: {config.Output.Path}");
            DebugLog.Write("run", $"events: [{string.Join(", ", config.Instrumentation.Events ?? [])}]");
            DebugLog.Write("run", $"includes: {config.Instrumentation.Includes.Count} rules, excludes: {config.Instrumentation.Excludes.Count} rules");

            // 3. Resolve exe path
            string resolvedExePath;
            var isBareName = !Path.IsPathRooted(exePath)
                && !exePath.Contains(Path.DirectorySeparatorChar)
                && !exePath.Contains(Path.AltDirectorySeparatorChar);

            if (isBareName)
            {
                // Bare command name (e.g. "dotnet") — let Process.Start resolve from PATH
                resolvedExePath = exePath;
            }
            else
            {
                resolvedExePath = Path.GetFullPath(exePath);
                if (!File.Exists(resolvedExePath))
                {
                    Console.Error.WriteLine($"Error: Executable not found at {resolvedExePath}");
                    return 1;
                }
            }

            // 4. Create ProcessStartInfo with profiler env vars
            var psi = new ProcessStartInfo
            {
                FileName = resolvedExePath,
                Arguments = string.Join(' ', extraArgs),
                UseShellExecute = false
            };
            psi.Environment["CORECLR_ENABLE_PROFILING"] = "1";
            psi.Environment["CORECLR_PROFILER"] = MetrejaConstants.ProfilerClsid;
            psi.Environment["CORECLR_PROFILER_PATH"] = absoluteDllPath;
            psi.Environment["METREJA_CONFIG"] = absoluteConfigPath;

            // 5. Launch
            DebugLog.Write("run", $"launching: {resolvedExePath} {string.Join(' ', extraArgs)}");
            Process process;
            try
            {
                process = Process.Start(psi)!;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                var message = ex.NativeErrorCode switch
                {
                    2 or 3 => $"Error: Executable '{exePath}' not found. Ensure it exists or is on your PATH.",
                    5 => $"Error: Access denied when trying to run '{exePath}'.",
                    _ => $"Error: Failed to start '{exePath}': {ex.Message}"
                };
                Console.Error.WriteLine(message);
                return 1;
            }

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
            var processExitCode = process.ExitCode;

            // 6. Auto-merge output files
            try
            {
                var outputTemplate = config.Output.Path;
                var sessionFiles = NdjsonMerger.FindSessionOutputFiles(outputTemplate, session);

                if (sessionFiles.Length > 1)
                {
                    var mergedPath = NdjsonMerger.ComputeMergedPath(outputTemplate, session);
                    DebugLog.Write("run", $"Merging {sessionFiles.Length} output files into {mergedPath}");

                    var mergeResult = await NdjsonMerger.MergeFilesAsync(sessionFiles, mergedPath, cancellationToken);

                    DebugLog.Write("run", $"Merged {mergeResult.EventCount} events, {mergeResult.SkippedCount} skipped");
                    Console.Error.WriteLine($"Merged {sessionFiles.Length} trace files into {Path.GetFileName(mergedPath)}");

                    foreach (var file in sessionFiles)
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { DebugLog.Write("run", $"Failed to delete {file}: {ex.Message}"); }
                    }
                }
                else if (sessionFiles.Length == 1)
                {
                    var mergedPath = NdjsonMerger.ComputeMergedPath(outputTemplate, session);
                    if (!string.Equals(Path.GetFullPath(mergedPath), Path.GetFullPath(sessionFiles[0]), StringComparison.OrdinalIgnoreCase))
                    {
                        DebugLog.Write("run", $"Renaming single output file to {mergedPath}");
                        File.Move(sessionFiles[0], mergedPath, overwrite: true);
                    }
                }
                else
                {
                    DebugLog.Write("run", "No output files found for session");
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write("run", $"Auto-merge failed: {ex.Message}");
                Console.Error.WriteLine($"Warning: Auto-merge failed: {ex.Message}");
            }

            return processExitCode;
        });

        return command;
    }
}
