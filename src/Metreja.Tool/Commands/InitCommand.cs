using System.CommandLine;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

public static class InitCommand
{
    public static Command Create()
    {
        var scenarioOption = new Option<string?>("--scenario")
        {
            Description = "Optional scenario name for this profiling session"
        };

        var command = new Command("init", "Initialize a new profiling session");
        command.Options.Add(scenarioOption);

        command.SetAction(async (parseResult, _) =>
        {
            var scenario = parseResult.GetValue(scenarioOption);
            var manager = new ConfigManager();
            var sessionId = await manager.CreateSessionAsync(scenario);
            Console.WriteLine(sessionId);
        });

        return command;
    }
}
