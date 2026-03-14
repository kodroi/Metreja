using System.CommandLine;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

public static class ClearFiltersCommand
{
    public static Command Create()
    {
        var sessionOption = SharedOptions.SessionOption();

        var typeOption = new Option<string>("--type")
        {
            Description = "Filter type to clear: 'include' or 'exclude' (omit to clear both)"
        };

        var command = new Command("clear-filters", "Clear all filter rules from a session");
        command.Options.Add(sessionOption);
        command.Options.Add(typeOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var type = parseResult.GetValue(typeOption);

            if (type is not (null or "include" or "exclude"))
            {
                Console.Error.WriteLine("Error: --type must be 'include' or 'exclude'");
                return 1;
            }

            var manager = ConfigManager.Default;
            var config = await manager.LoadConfigAsync(session);

            var clearIncludes = type is null or "include";
            var clearExcludes = type is null or "exclude";

            var updatedInstrumentation = config.Instrumentation with
            {
                Includes = clearIncludes ? [] : config.Instrumentation.Includes,
                Excludes = clearExcludes ? [] : config.Instrumentation.Excludes
            };

            var updatedConfig = config with { Instrumentation = updatedInstrumentation };
            await manager.SaveConfigAsync(session, updatedConfig);

            var cleared = (clearIncludes, clearExcludes) switch
            {
                (true, true) => "all",
                (true, false) => "include",
                _ => "exclude"
            };

            Console.WriteLine($"Cleared {cleared} filters from session {session}");
            return 0;
        });

        return command;
    }
}
