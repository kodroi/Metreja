using System.CommandLine;
using Metreja.Cli.Config;

namespace Metreja.Cli.Commands;

public static class ValidateCommand
{
    public static Command Create()
    {
        var sessionOption = new Option<string>("--session", "-s")
        {
            Description = "Session ID",
            Required = true
        };

        var command = new Command("validate", "Validate session configuration");
        command.Options.Add(sessionOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var manager = new ConfigManager();

            ProfilerConfig config;
            try
            {
                config = await manager.LoadConfigAsync(session);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return;
            }

            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(config.Metadata.RunId))
                errors.Add("metadata.runId is required");

            if (string.IsNullOrWhiteSpace(config.Output.Path))
                errors.Add("output.path is required");

            if (config.Instrumentation.Includes.Count == 0)
                errors.Add("At least one include rule is recommended (currently includes everything)");

            // Check output directory is writable
            var outputDir = Path.GetDirectoryName(config.Output.Path);
            if (!string.IsNullOrEmpty(outputDir) && !outputDir.Contains('{'))
            {
                try
                {
                    Directory.CreateDirectory(outputDir);
                }
                catch (Exception ex)
                {
                    errors.Add($"Cannot create output directory '{outputDir}': {ex.Message}");
                }
            }

            if (errors.Count == 0)
            {
                Console.WriteLine("Validation passed. 0 errors.");
            }
            else
            {
                Console.WriteLine($"Validation found {errors.Count} issue(s):");
                foreach (var error in errors)
                {
                    Console.WriteLine($"  - {error}");
                }
            }
        });

        return command;
    }
}
