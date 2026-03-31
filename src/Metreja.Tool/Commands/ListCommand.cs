using System.CommandLine;
using System.Globalization;
using Metreja.Tool.Analysis;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

public static class ListCommand
{
    public static Command Create()
    {
        var command = new Command("list", "List existing profiling sessions");
        command.SetAction(async (parseResult, _) =>
        {
            var manager = ConfigManager.Default;
            var sessions = manager.ListSessions().ToList();

            if (sessions.Count == 0)
            {
                Console.WriteLine("No sessions found.");
                return;
            }

            Console.WriteLine(
                $"{"Session",-12} {"Scenario",-25} {"Modified",-22} {"Output Path",-45} {"Includes",9} {"Excludes",9}");
            Console.WriteLine(new string('-', 125));

            foreach (var sessionId in sessions)
            {
                try
                {
                    var config = await manager.LoadConfigAsync(sessionId);
                    var path = manager.GetSessionPath(sessionId);
                    var modified = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    var includes = config.Instrumentation.Includes.Count;
                    var excludes = config.Instrumentation.Excludes.Count;

                    Console.WriteLine(
                        $"{sessionId,-12} {FormatUtils.Truncate(config.Metadata.Scenario, 25),-25} {modified,-22} {FormatUtils.Truncate(config.Output.Path, 45),-45} {includes,9} {excludes,9}");
                }
                catch (Exception)
                {
                    Console.WriteLine($"{sessionId,-12} {"(corrupt config)",-25}");
                }
            }

            Console.WriteLine(new string('-', 125));
            Console.WriteLine($"{sessions.Count} session(s)");
        });

        return command;
    }
}
