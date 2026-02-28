using System.CommandLine;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

public static class RemoveCommand
{
    public static Command Create()
    {
        var sessionOption = new Option<string>("--session", "-s")
        {
            Description = "Session ID",
            Required = true
        };

        var command = new Command("remove", "Remove include or exclude filter rules");

        var includeCommand = CreateFilterRemoveCommand("include", "Remove an include filter rule", sessionOption);
        var excludeCommand = CreateFilterRemoveCommand("exclude", "Remove an exclude filter rule", sessionOption);

        command.Subcommands.Add(includeCommand);
        command.Subcommands.Add(excludeCommand);

        return command;
    }

    private static Command CreateFilterRemoveCommand(string name, string description, Option<string> sessionOption)
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

            var list = name == "include"
                ? config.Instrumentation.Includes
                : config.Instrumentation.Excludes;

            var index = list.IndexOf(rule);
            if (index < 0)
            {
                Console.Error.WriteLine($"Error: No matching {name} rule found in session {session}");
                Environment.ExitCode = 1;
                return;
            }

            var updatedList = list.Where((_, i) => i != index).ToList();

            var updatedInstrumentation = name == "include"
                ? config.Instrumentation with { Includes = updatedList }
                : config.Instrumentation with { Excludes = updatedList };

            var updatedConfig = config with { Instrumentation = updatedInstrumentation };
            await manager.SaveConfigAsync(session, updatedConfig);

            Console.WriteLine($"Removed {name} rule from session {session} ({updatedList.Count} {name} rules remaining)");
        });

        return command;
    }
}
