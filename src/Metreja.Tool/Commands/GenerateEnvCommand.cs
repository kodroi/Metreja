using System.CommandLine;
using System.Runtime.InteropServices;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

public static class GenerateEnvCommand
{
    public static Command Create()
    {
        var sessionOption = SharedOptions.SessionOption();

        var defaultFormat = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "batch" : "shell";
        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: batch, powershell, or shell"
        };
        formatOption.DefaultValueFactory = _ => defaultFormat;

        var forceOption = new Option<bool>("--force")
        {
            Description = "Generate script even if profiler DLL is not found"
        };

        var command = new Command("generate-env", "Generate environment variable script for profiling");
        command.Options.Add(sessionOption);
        command.Options.Add(formatOption);
        command.Options.Add(forceOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var format = parseResult.GetValue(formatOption)!;
            var force = parseResult.GetValue(forceOption);

            var manager = ConfigManager.Default;
            var configPath = Path.GetFullPath(manager.GetSessionPath(session));

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

            var absoluteDllPath = ProfilerLocator.ResolveProfilerPath();
            if (absoluteDllPath is null)
            {
                if (!force)
                {
                    Console.Error.WriteLine("Use --force to generate the script anyway.");
                    return 1;
                }
                absoluteDllPath = "";
            }

            if (format.Equals("powershell", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"$env:CORECLR_ENABLE_PROFILING = \"1\"");
                Console.WriteLine($"$env:CORECLR_PROFILER = \"{MetrejaConstants.ProfilerClsid}\"");
                Console.WriteLine($"$env:CORECLR_PROFILER_PATH = \"{absoluteDllPath}\"");
                Console.WriteLine($"$env:METREJA_CONFIG = \"{configPath}\"");
            }
            else if (format.Equals("shell", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"export CORECLR_ENABLE_PROFILING=\"1\"");
                Console.WriteLine($"export CORECLR_PROFILER=\"{MetrejaConstants.ProfilerClsid}\"");
                Console.WriteLine($"export CORECLR_PROFILER_PATH=\"{absoluteDllPath}\"");
                Console.WriteLine($"export METREJA_CONFIG=\"{configPath}\"");
            }
            else
            {
                Console.WriteLine($"set \"CORECLR_ENABLE_PROFILING=1\"");
                Console.WriteLine($"set \"CORECLR_PROFILER={MetrejaConstants.ProfilerClsid}\"");
                Console.WriteLine($"set \"CORECLR_PROFILER_PATH={absoluteDllPath}\"");
                Console.WriteLine($"set \"METREJA_CONFIG={configPath}\"");
            }

            return 0;
        });

        return command;
    }
}
