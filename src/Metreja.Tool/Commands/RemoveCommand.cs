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
        var assemblyOption = new Option<string>("--assembly") { Description = "Assembly name pattern" };
        var namespaceOption = new Option<string>("--namespace") { Description = "Namespace pattern" };
        var classOption = new Option<string>("--class") { Description = "Class name pattern" };
        var methodOption = new Option<string>("--method") { Description = "Method name pattern" };
        var command = new Command(name, description);
        command.Options.Add(sessionOption);
        command.Options.Add(assemblyOption);
        command.Options.Add(namespaceOption);
        command.Options.Add(classOption);
        command.Options.Add(methodOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;

            var provided = new (string? value, string level)[]
            {
                (parseResult.GetValue(assemblyOption), "assembly"),
                (parseResult.GetValue(namespaceOption), "namespace"),
                (parseResult.GetValue(classOption), "class"),
                (parseResult.GetValue(methodOption), "method")
            };

            var active = provided.Where(o => o.value is not null).ToArray();

            if (active.Length == 0)
            {
                Console.Error.WriteLine("Error: Specify one of --assembly, --namespace, --class, or --method.");
                Environment.ExitCode = 1;
                return;
            }

            if (active.Length > 1)
            {
                Console.Error.WriteLine("Error: Only one level option (--assembly, --namespace, --class, --method) can be used per command.");
                Environment.ExitCode = 1;
                return;
            }

            var (pattern, level) = active[0];
            var rule = new FilterRule { Level = level, Pattern = pattern! };

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
