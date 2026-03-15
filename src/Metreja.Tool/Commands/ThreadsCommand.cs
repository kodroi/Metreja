using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class ThreadsCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var sortOption = new Option<string>("--sort") { Description = "Sort by: calls or time", DefaultValueFactory = _ => "calls" };

        var command = new Command("threads", "Per-thread breakdown: call counts, timing, activity windows");
        command.Arguments.Add(fileArg);
        command.Options.Add(sortOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var sort = parseResult.GetValue(sortOption)!;
            await ThreadsAnalyzer.AnalyzeAsync(file, sort);
        });

        return command;
    }
}
