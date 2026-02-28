using System.CommandLine;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

public static class GenerateEnvCommand
{
    public static Command Create()
    {
        var sessionOption = new Option<string>("--session", "-s")
        {
            Description = "Session ID",
            Required = true
        };

        var dllPathOption = new Option<string>("--dll-path")
        {
            Description = "Path to Metreja.Profiler.dll (auto-detected if not specified)"
        };
        dllPathOption.DefaultValueFactory = _ => ProfilerLocator.GetDefaultProfilerPath() ?? "";

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: batch or powershell"
        };
        formatOption.DefaultValueFactory = _ => "batch";

        var forceOption = new Option<bool>("--force")
        {
            Description = "Generate script even if profiler DLL is not found"
        };

        var command = new Command("generate-env", "Generate environment variable script for profiling");
        command.Options.Add(sessionOption);
        command.Options.Add(dllPathOption);
        command.Options.Add(formatOption);
        command.Options.Add(forceOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var dllPath = parseResult.GetValue(dllPathOption)!;
            var format = parseResult.GetValue(formatOption)!;
            var force = parseResult.GetValue(forceOption);

            var manager = new ConfigManager();
            var configPath = Path.GetFullPath(manager.GetSessionPath(session));

            // Resolve dll path to absolute
            var absoluteDllPath = string.IsNullOrEmpty(dllPath) ? "" : Path.GetFullPath(dllPath);

            if (string.IsNullOrEmpty(absoluteDllPath) || !File.Exists(absoluteDllPath))
            {
                Console.Error.WriteLine($"Error: Profiler DLL not found at '{absoluteDllPath}'");
                if (!force)
                {
                    Console.Error.WriteLine("Use --force to generate the script anyway.");
                    Environment.ExitCode = 1;
                    return;
                }
            }

            if (format.Equals("powershell", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"$env:CORECLR_ENABLE_PROFILING = \"1\"");
                Console.WriteLine($"$env:CORECLR_PROFILER = \"{MetrejaConstants.ProfilerClsid}\"");
                Console.WriteLine($"$env:CORECLR_PROFILER_PATH = \"{absoluteDllPath}\"");
                Console.WriteLine($"$env:METREJA_CONFIG = \"{configPath}\"");
            }
            else
            {
                Console.WriteLine($"set CORECLR_ENABLE_PROFILING=1");
                Console.WriteLine($"set CORECLR_PROFILER={MetrejaConstants.ProfilerClsid}");
                Console.WriteLine($"set CORECLR_PROFILER_PATH={absoluteDllPath}");
                Console.WriteLine($"set METREJA_CONFIG={configPath}");
            }
        });

        return command;
    }
}
