using System.CommandLine;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

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
        var assemblyOption = new Option<string[]>("--assembly")
        {
            Description = "Assembly name pattern(s)",
            Arity = ArgumentArity.OneOrMore
        };
        var namespaceOption = new Option<string[]>("--namespace")
        {
            Description = "Namespace pattern(s)",
            Arity = ArgumentArity.OneOrMore
        };
        var classOption = new Option<string[]>("--class")
        {
            Description = "Class name pattern(s)",
            Arity = ArgumentArity.OneOrMore
        };
        var methodOption = new Option<string[]>("--method")
        {
            Description = "Method name pattern(s)",
            Arity = ArgumentArity.OneOrMore
        };
        var command = new Command(name, description);
        command.Options.Add(sessionOption);
        command.Options.Add(assemblyOption);
        command.Options.Add(namespaceOption);
        command.Options.Add(classOption);
        command.Options.Add(methodOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;

            var provided = new (string[] values, string level)[]
            {
                (parseResult.GetValue(assemblyOption) ?? [], "assembly"),
                (parseResult.GetValue(namespaceOption) ?? [], "namespace"),
                (parseResult.GetValue(classOption) ?? [], "class"),
                (parseResult.GetValue(methodOption) ?? [], "method")
            };

            var active = provided.Where(o => o.values.Length > 0).ToArray();

            if (active.Length == 0)
            {
                Console.Error.WriteLine("Error: Specify one of --assembly, --namespace, --class, or --method.");
                return 1;
            }

            if (active.Length > 1)
            {
                Console.Error.WriteLine("Error: Only one level option (--assembly, --namespace, --class, --method) can be used per command.");
                return 1;
            }

            var (values, level) = active[0];
            var rules = values.Select(v => new FilterRule { Level = level, Pattern = v }).ToList();

            var manager = new ConfigManager();
            var config = await manager.LoadConfigAsync(session);

            var updatedInstrumentation = name == "include"
                ? config.Instrumentation with { Includes = [.. config.Instrumentation.Includes, .. rules] }
                : config.Instrumentation with { Excludes = [.. config.Instrumentation.Excludes, .. rules] };

            var updatedConfig = config with { Instrumentation = updatedInstrumentation };
            await manager.SaveConfigAsync(session, updatedConfig);

            var ruleWord = rules.Count == 1 ? "rule" : "rules";
            Console.WriteLine($"Added {rules.Count} {name} {ruleWord} to session {session}");
            return 0;
        });

        return command;
    }
}
