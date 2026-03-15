using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class SummaryCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var command = new Command("summary", "Show trace overview: events, threads, methods, duration");
        command.Arguments.Add(fileArg);
        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            await SummaryAnalyzer.AnalyzeAsync(file);
        });
        return command;
    }
}
