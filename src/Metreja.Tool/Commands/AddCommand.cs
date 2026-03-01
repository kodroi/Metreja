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
            Description = "Assembly name pattern (default: *)",
            Arity = ArgumentArity.ZeroOrMore
        };
        var namespaceOption = new Option<string[]>("--namespace")
        {
            Description = "Namespace pattern (default: *)",
            Arity = ArgumentArity.ZeroOrMore
        };
        var classOption = new Option<string[]>("--class")
        {
            Description = "Class name pattern (default: *)",
            Arity = ArgumentArity.ZeroOrMore
        };
        var methodOption = new Option<string[]>("--method")
        {
            Description = "Method name pattern (default: *)",
            Arity = ArgumentArity.ZeroOrMore
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
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];
            var namespaces = parseResult.GetValue(namespaceOption) ?? [];
            var classes = parseResult.GetValue(classOption) ?? [];
            var methods = parseResult.GetValue(methodOption) ?? [];
            if (!TryBuildRules(assemblies, namespaces, classes, methods, out var rules))
            {
                Console.Error.WriteLine(
                    "Error: Only one filter option can have multiple values per command. " +
                    "Run the command multiple times for complex combinations.");
                Environment.ExitCode = 1;
                return;
            }

            var manager = new ConfigManager();
            var config = await manager.LoadConfigAsync(session);

            var updatedInstrumentation = name == "include"
                ? config.Instrumentation with { Includes = [.. config.Instrumentation.Includes, .. rules] }
                : config.Instrumentation with { Excludes = [.. config.Instrumentation.Excludes, .. rules] };

            var updatedConfig = config with { Instrumentation = updatedInstrumentation };
            await manager.SaveConfigAsync(session, updatedConfig);

            var ruleWord = rules.Count == 1 ? "rule" : "rules";
            Console.WriteLine($"Added {rules.Count} {name} {ruleWord} to session {session}");
        });

        return command;
    }

    private static bool TryBuildRules(
        string[] assemblies, string[] namespaces, string[] classes, string[] methods,
        out List<FilterRule> rules)
    {
        rules = [];
        var multiValueOptions = new (string[] values, string label)[]
        {
            (assemblies, "assembly"),
            (namespaces, "namespace"),
            (classes, "class"),
            (methods, "method")
        };

        var multiCount = multiValueOptions.Count(o => o.values.Length > 1);
        if (multiCount > 1)
            return false;

        var multiOption = multiValueOptions.FirstOrDefault(o => o.values.Length > 1);

        if (multiOption.values is null || multiOption.values.Length == 0)
        {
            rules =
            [
                new FilterRule
                {
                    Assembly = assemblies.Length == 1 ? assemblies[0] : "*",
                    Namespace = namespaces.Length == 1 ? namespaces[0] : "*",
                    Class = classes.Length == 1 ? classes[0] : "*",
                    Method = methods.Length == 1 ? methods[0] : "*"
                }
            ];
            return true;
        }

        var baseAssembly = assemblies.Length == 1 ? assemblies[0] : "*";
        var baseNamespace = namespaces.Length == 1 ? namespaces[0] : "*";
        var baseClass = classes.Length == 1 ? classes[0] : "*";
        var baseMethod = methods.Length == 1 ? methods[0] : "*";

        rules = multiOption.values.Select(value => new FilterRule
        {
            Assembly = multiOption.label == "assembly" ? value : baseAssembly,
            Namespace = multiOption.label == "namespace" ? value : baseNamespace,
            Class = multiOption.label == "class" ? value : baseClass,
            Method = multiOption.label == "method" ? value : baseMethod
        }).ToList();
        return true;
    }
}
