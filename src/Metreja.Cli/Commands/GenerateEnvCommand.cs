using System.CommandLine;
using Metreja.Cli.Config;

namespace Metreja.Cli.Commands;

public static class GenerateEnvCommand
{
    private const string ProfilerClsid = "{7C8F944B-4810-4999-BF98-6A3361185FC2}";

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
        dllPathOption.DefaultValueFactory = _ => ProfilerLocator.GetDefaultProfilerPath();

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: batch or powershell"
        };
        formatOption.DefaultValueFactory = _ => "batch";

        var command = new Command("generate-env", "Generate environment variable script for profiling");
        command.Options.Add(sessionOption);
        command.Options.Add(dllPathOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var dllPath = parseResult.GetValue(dllPathOption)!;
            var format = parseResult.GetValue(formatOption)!;

            var manager = new ConfigManager();
            var configPath = Path.GetFullPath(manager.GetSessionPath(session));

            // Resolve dll path to absolute
            var absoluteDllPath = Path.GetFullPath(dllPath);

            if (!File.Exists(absoluteDllPath))
            {
                Console.Error.WriteLine($"WARNING: Profiler DLL not found at '{absoluteDllPath}'");
            }

            if (format.Equals("powershell", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"$env:CORECLR_ENABLE_PROFILING = \"1\"");
                Console.WriteLine($"$env:CORECLR_PROFILER = \"{ProfilerClsid}\"");
                Console.WriteLine($"$env:CORECLR_PROFILER_PATH = \"{absoluteDllPath}\"");
                Console.WriteLine($"$env:METREJA_CONFIG = \"{configPath}\"");
            }
            else
            {
                Console.WriteLine($"set CORECLR_ENABLE_PROFILING=1");
                Console.WriteLine($"set CORECLR_PROFILER={ProfilerClsid}");
                Console.WriteLine($"set CORECLR_PROFILER_PATH={absoluteDllPath}");
                Console.WriteLine($"set METREJA_CONFIG={configPath}");
            }
        });

        return command;
    }
}
