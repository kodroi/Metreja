using System.CommandLine;
using Metreja.Cli.Config;

namespace Metreja.Cli.Commands;

public static class AddCommand
{
    public static Command Create()
    {
        var sessionOption = new Option<string>("--session", "-s")
        {
            Description = "Session ID",
            Required = true
        };

        var command = new Command("add", "Add include or exclude filter rules");

        var includeCommand = CreateFilterCommand("include", "Add an include filter rule", sessionOption);
        var excludeCommand = CreateFilterCommand("exclude", "Add an exclude filter rule", sessionOption);

        command.Subcommands.Add(includeCommand);
        command.Subcommands.Add(excludeCommand);

        return command;
    }

    private static Command CreateFilterCommand(string name, string description, Option<string> sessionOption)
    {
        var assemblyOption = new Option<string>("--assembly") { Description = "Assembly name pattern (default: *)" };
        assemblyOption.DefaultValueFactory = _ => "*";
        var namespaceOption = new Option<string>("--namespace") { Description = "Namespace pattern (default: *)" };
        namespaceOption.DefaultValueFactory = _ => "*";
        var classOption = new Option<string>("--class") { Description = "Class name pattern (default: *)" };
        classOption.DefaultValueFactory = _ => "*";
        var methodOption = new Option<string>("--method") { Description = "Method name pattern (default: *)" };
        methodOption.DefaultValueFactory = _ => "*";
        var logLinesOption = new Option<bool>("--log-lines") { Description = "Enable line-level logging" };

        var command = new Command(name, description);
        command.Options.Add(sessionOption);
        command.Options.Add(assemblyOption);
        command.Options.Add(namespaceOption);
        command.Options.Add(classOption);
        command.Options.Add(methodOption);
        command.Options.Add(logLinesOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var rule = new FilterRule
            {
                Assembly = parseResult.GetValue(assemblyOption)!,
                Namespace = parseResult.GetValue(namespaceOption)!,
                Class = parseResult.GetValue(classOption)!,
                Method = parseResult.GetValue(methodOption)!,
                LogLines = parseResult.GetValue(logLinesOption)
            };

            var manager = new ConfigManager();
            var config = await manager.LoadConfigAsync(session);

            var updatedInstrumentation = name == "include"
                ? config.Instrumentation with { Includes = [.. config.Instrumentation.Includes, rule] }
                : config.Instrumentation with { Excludes = [.. config.Instrumentation.Excludes, rule] };

            var updatedConfig = config with { Instrumentation = updatedInstrumentation };
            await manager.SaveConfigAsync(session, updatedConfig);

            Console.WriteLine($"Added {name} rule to session {session}");
        });

        return command;
    }
}
