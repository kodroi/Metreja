using System.CommandLine;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

public static class ClearCommand
{
    public static Command Create()
    {
        var sessionOption = new Option<string>("--session", "-s")
        {
            Description = "Session ID to delete"
        };

        var allOption = new Option<bool>("--all")
        {
            Description = "Delete all sessions"
        };

        var command = new Command("clear", "Delete profiling session(s)");
        command.Options.Add(sessionOption);
        command.Options.Add(allOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption);
            var all = parseResult.GetValue(allOption);
            var manager = new ConfigManager();

            if (all)
            {
                await manager.DeleteAllSessionsAsync();
                Console.WriteLine("All sessions deleted.");
            }
            else if (!string.IsNullOrEmpty(session))
            {
                await manager.DeleteSessionAsync(session);
                Console.WriteLine($"Session {session} deleted.");
            }
            else
            {
                Console.Error.WriteLine("Error: specify --session <id> or --all");
                Environment.ExitCode = 1;
            }
        });

        return command;
    }
}
